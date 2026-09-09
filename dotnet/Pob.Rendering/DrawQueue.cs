using System.Runtime.InteropServices;

namespace Pob.Rendering;

/// <summary>
/// The buffered, layer-sorted draw queue that replaces SimpleGraphic's immediate-mode host API.
/// </summary>
/// <remarks>
/// <para>
/// It implements the six live drawing entry points of <c>src/_SimpleGraphic.def.lua</c>:
/// <c>SetDrawColor</c>, <c>GetDrawColor</c>, <c>SetDrawLayer</c>, <c>GetDrawLayer</c>,
/// <c>SetViewport</c>, <c>DrawImage</c> and <c>DrawImageQuad</c>. Draw calls are recorded, not
/// executed: PoB relies on <c>SetDrawLayer</c> to draw tooltips (layer 99/100) after the content
/// that requested them, so the queue is sorted by <c>(layer, subLayer, sequence)</c> before it is
/// handed to a backend. Sequence makes the ordering total, so calls sharing a layer keep the order
/// they were emitted in.
/// </para>
/// <para>
/// Every command captures the tint and the clip rectangle in force when it was recorded, because
/// after sorting there is no meaningful "current state" to replay.
/// </para>
/// <para>
/// <b>Viewports.</b> A <c>SetViewport(x, y, w, h)</c> call both scissors drawing and moves the
/// origin, so a control can draw its content at 0,0. The Lua host has a single global viewport:
/// <c>src/Modules/ItemSlotHelper.lua:10-37</c> nests an entire passive-tree render inside an item
/// slot with it and its own comment warns that doing so clobbers the caller's viewport and draw
/// layer. This queue keeps a state stack instead. <c>SetViewport</c> is resolved against the
/// enclosing stack level, so at depth zero it behaves exactly like the Lua original (absolute
/// coordinates, each call replacing the last, no-arg resetting to the full screen), while a nested
/// renderer opened with <see cref="PushState"/> composes with its parent and cannot clobber it.
/// </para>
/// <para>This type is not thread-safe; use <see cref="ToSortedArray"/> to hand a frame to another thread.</para>
/// </remarks>
public sealed class DrawQueue
{
    private readonly List<DrawCmd> _commands = [];
    private readonly List<State> _stack = [];
    private State _state;
    private int _sequence;
    private bool _sorted = true;

    /// <summary>Creates an empty queue for a screen of the given size.</summary>
    /// <param name="screenWidth">Screen width in logical pixels.</param>
    /// <param name="screenHeight">Screen height in logical pixels.</param>
    /// <param name="screenScale">The DPI scale, as the Lua <c>GetScreenScale</c> reports it.</param>
    public DrawQueue(float screenWidth = 0f, float screenHeight = 0f, float screenScale = 1f)
    {
        BeginFrame(screenWidth, screenHeight, screenScale);
    }

    /// <summary>Screen width in logical pixels, as the Lua <c>GetScreenSize</c> reports it.</summary>
    public float ScreenWidth { get; private set; }

    /// <summary>Screen height in logical pixels, as the Lua <c>GetScreenSize</c> reports it.</summary>
    public float ScreenHeight { get; private set; }

    /// <summary>DPI scale, as the Lua <c>GetScreenScale</c> reports it.</summary>
    public float ScreenScale { get; private set; }

    /// <summary>Number of commands recorded so far this frame.</summary>
    public int Count => _commands.Count;

    /// <summary>Current depth of the state stack; zero at the outermost level.</summary>
    public int StateDepth => _stack.Count;

    /// <summary>
    /// The viewport rectangle established by the last <c>SetViewport</c>, in absolute screen
    /// coordinates and before intersection with any enclosing viewport.
    /// </summary>
    public Viewport CurrentViewport => _state.Current.Viewport;

    /// <summary>
    /// The effective scissor rectangle: <see cref="CurrentViewport"/> intersected with every
    /// enclosing viewport. This is what commands are clipped to.
    /// </summary>
    public Viewport CurrentClip => _state.Current.Clip;

    /// <summary>The origin that draw coordinates are offset by, i.e. the viewport's top-left corner.</summary>
    public (float X, float Y) Origin => (_state.Current.Viewport.X, _state.Current.Viewport.Y);

    /// <summary>Clears the queue and resets all draw state for a new frame.</summary>
    /// <param name="screenWidth">Screen width in logical pixels.</param>
    /// <param name="screenHeight">Screen height in logical pixels.</param>
    /// <param name="screenScale">The DPI scale, as the Lua <c>GetScreenScale</c> reports it.</param>
    public void BeginFrame(float screenWidth, float screenHeight, float screenScale = 1f)
    {
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        ScreenScale = screenScale;
        _commands.Clear();
        _stack.Clear();
        _sequence = 0;
        _sorted = true;

        var screen = new Frame(new Viewport(0f, 0f, screenWidth, screenHeight), new Viewport(0f, 0f, screenWidth, screenHeight));
        _state = new State(DrawColor.White, 0, 0, screen, screen);
    }

    // ---------------------------------------------------------------- colour

    /// <summary>SimpleGraphic <c>SetDrawColor(r, g, b, a)</c>. Alpha defaults to fully opaque.</summary>
    public void SetDrawColor(float r, float g, float b, float a = 1f) =>
        _state.Color = new DrawColor(r, g, b, a);

    /// <summary>SimpleGraphic <c>SetDrawColor(color)</c>.</summary>
    public void SetDrawColor(DrawColor color) => _state.Color = color;

    /// <summary>
    /// SimpleGraphic <c>SetDrawColor(escapeStr)</c>: a <c>^0</c>..<c>^9</c> or <c>^xRRGGBB</c>
    /// escape. The escape sets an opaque colour, exactly as the Lua host does.
    /// </summary>
    /// <exception cref="ArgumentException">The string is not a well-formed colour escape.</exception>
    public void SetDrawColor(string escape) => _state.Color = ColorEscape.Parse(escape);

    /// <summary>SimpleGraphic <c>GetDrawColor()</c>, used by e.g. <c>src/Classes/Tooltip.lua:613</c> to save and restore the tint.</summary>
    public DrawColor GetDrawColor() => _state.Color;

    // ----------------------------------------------------------------- layer

    /// <summary>SimpleGraphic <c>SetDrawLayer(layer)</c>: sets the layer and resets the sub-layer to zero.</summary>
    public void SetDrawLayer(int layer)
    {
        _state.Layer = layer;
        _state.SubLayer = 0;
    }

    /// <summary>
    /// SimpleGraphic <c>SetDrawLayer(layer, subLayer)</c>. A <see langword="null"/>
    /// <paramref name="layer"/> is the Lua <c>SetDrawLayer(nil, subLayer)</c> form: it keeps the
    /// current layer and changes only the sub-layer, which is how most of PoB moves between the
    /// tree's interleaved sub-layers.
    /// </summary>
    public void SetDrawLayer(int? layer, int subLayer)
    {
        if (layer.HasValue)
        {
            _state.Layer = layer.Value;
        }

        _state.SubLayer = subLayer;
    }

    /// <summary>SimpleGraphic <c>GetDrawLayer()</c>.</summary>
    public int GetDrawLayer() => _state.Layer;

    /// <summary>The current sub-layer. The Lua host exposes no getter for it.</summary>
    public int GetDrawSubLayer() => _state.SubLayer;

    // -------------------------------------------------------------- viewport

    /// <summary>
    /// SimpleGraphic <c>SetViewport(x, y, width, height)</c>: clips drawing to the rectangle and
    /// moves the origin to its top-left corner. The rectangle is relative to the enclosing stack
    /// level, which at depth zero means absolute screen coordinates.
    /// </summary>
    public void SetViewport(float x, float y, float width, float height)
    {
        Frame parent = _state.Base;
        var viewport = new Viewport(parent.Viewport.X + x, parent.Viewport.Y + y, width, height);
        _state.Current = new Frame(viewport, parent.Clip.Intersect(viewport));
    }

    /// <summary>
    /// SimpleGraphic <c>SetViewport()</c>: resets the viewport. At depth zero that is the full
    /// screen; inside a <see cref="PushState"/> scope it is the viewport the scope inherited,
    /// so a nested renderer's reset cannot escape its parent's clip.
    /// </summary>
    public void SetViewport() => _state.Current = _state.Base;

    // ----------------------------------------------------------- state stack

    /// <summary>
    /// Saves the tint, layer and viewport. This is the explicit form of the save/restore that the
    /// Lua host cannot do; see the remarks on <see cref="DrawQueue"/>.
    /// </summary>
    public DrawState SaveState() =>
        new(_state.Color, _state.Layer, _state.SubLayer, _state.Current.Viewport);

    /// <summary>
    /// Restores a state captured by <see cref="SaveState"/>. The viewport is restored as an
    /// absolute rectangle, and is re-clipped against the enclosing stack level.
    /// </summary>
    public void RestoreState(DrawState state)
    {
        _state.Color = state.Color;
        _state.Layer = state.Layer;
        _state.SubLayer = state.SubLayer;
        _state.Current = new Frame(state.Viewport, _state.Base.Clip.Intersect(state.Viewport));
    }

    /// <summary>
    /// Pushes the current state and returns a scope that restores it on disposal. Inside the
    /// scope, the current viewport becomes the base that <c>SetViewport</c> is resolved against,
    /// so a nested render — as <c>src/Modules/ItemSlotHelper.lua</c> does with a whole passive
    /// tree — is contained: it cannot widen the clip, move the origin outside it, or leak its tint
    /// and draw layer back to the caller.
    /// </summary>
    public DrawStateScope PushState()
    {
        _stack.Add(_state);
        _state.Base = _state.Current;
        return new DrawStateScope(this, _stack.Count - 1);
    }

    /// <summary>
    /// Pushes the current state and enters a new viewport, given in the current viewport's
    /// coordinates. The new viewport becomes the scope's base, so code running inside it — a
    /// nested renderer that does not know it is nested — resolves its own <c>SetViewport</c> calls
    /// against the scope and cannot escape it, and its bare <c>SetViewport()</c> reset lands back
    /// on the scope rather than on the full screen.
    /// </summary>
    public DrawStateScope PushViewport(float x, float y, float width, float height)
    {
        DrawStateScope scope = PushState();
        SetViewport(x, y, width, height);
        _state.Base = _state.Current;
        return scope;
    }

    /// <summary>Pops the most recently pushed state.</summary>
    /// <exception cref="InvalidOperationException">The state stack is empty.</exception>
    public void PopState()
    {
        if (_stack.Count == 0)
        {
            throw new InvalidOperationException("Draw state stack underflow: PopState without a matching PushState.");
        }

        PopStateTo(_stack.Count - 1);
    }

    internal void PopStateTo(int depth)
    {
        if (depth < 0 || depth >= _stack.Count)
        {
            // Already unwound past this scope — disposing twice, or out of order, is a no-op.
            return;
        }

        _state = _stack[depth];
        _stack.RemoveRange(depth, _stack.Count - depth);
    }

    // ----------------------------------------------------------------- draws

    /// <summary>
    /// SimpleGraphic <c>DrawImage(imgHandle, left, top, width, height)</c> with the full texture.
    /// A <see langword="null"/> handle draws a solid rectangle in the current tint — the way PoB
    /// draws every solid fill.
    /// </summary>
    public void DrawImage(IImageHandle? image, float left, float top, float width, float height) =>
        DrawImage(image, left, top, width, height, 0f, 0f, 1f, 1f);

    /// <summary>
    /// SimpleGraphic <c>DrawImage(imgHandle, left, top, width, height, tcLeft, tcTop, tcRight, tcBottom)</c>.
    /// Texture coordinates outside 0..1 tile or clamp according to the handle's
    /// <see cref="IImageHandle.TileMode"/>; PoB tiles the tree background that way.
    /// </summary>
    public void DrawImage(
        IImageHandle? image,
        float left,
        float top,
        float width,
        float height,
        float tcLeft,
        float tcTop,
        float tcRight,
        float tcBottom)
    {
        float ox = _state.Current.Viewport.X;
        float oy = _state.Current.Viewport.Y;
        float l = left + ox;
        float t = top + oy;
        float r = l + width;
        float b = t + height;

        Add(new DrawCmd
        {
            Kind = image is null ? DrawCmdKind.Rect : DrawCmdKind.Image,
            Image = image,
            X1 = l, Y1 = t, S1 = tcLeft, T1 = tcTop,
            X2 = r, Y2 = t, S2 = tcRight, T2 = tcTop,
            X3 = r, Y3 = b, S3 = tcRight, T3 = tcBottom,
            X4 = l, Y4 = b, S4 = tcLeft, T4 = tcBottom,
        });
    }

    /// <summary>
    /// SimpleGraphic <c>DrawImageQuad(imgHandle, x1..y4)</c>: an arbitrary four-corner quad using
    /// the whole texture, corners in the order top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    public void DrawImageQuad(
        IImageHandle? image,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float x4, float y4) =>
        DrawImageQuad(image, x1, y1, x2, y2, x3, y3, x4, y4, 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f);

    /// <summary>
    /// SimpleGraphic <c>DrawImageQuad(imgHandle, x1..y4, s1..t4)</c>: an arbitrary four-corner
    /// quad with a texture coordinate per corner. This is how the passive tree draws rotated art
    /// and arc-band connectors out of an atlas — see
    /// <c>src/Classes/PassiveTreeView.lua:735-741</c>, which maps the connector's unit texture
    /// coordinates into the atlas region of the sprite sheet.
    /// </summary>
    public void DrawImageQuad(
        IImageHandle? image,
        float x1, float y1,
        float x2, float y2,
        float x3, float y3,
        float x4, float y4,
        float s1, float t1,
        float s2, float t2,
        float s3, float t3,
        float s4, float t4)
    {
        float ox = _state.Current.Viewport.X;
        float oy = _state.Current.Viewport.Y;

        Add(new DrawCmd
        {
            Kind = DrawCmdKind.Quad,
            Image = image,
            X1 = x1 + ox, Y1 = y1 + oy, S1 = s1, T1 = t1,
            X2 = x2 + ox, Y2 = y2 + oy, S2 = s2, T2 = t2,
            X3 = x3 + ox, Y3 = y3 + oy, S3 = s3, T3 = t3,
            X4 = x4 + ox, Y4 = y4 + oy, S4 = s4, T4 = t4,
        });
    }

    // --------------------------------------------------------------- flushing

    /// <summary>
    /// Sorts the queue by <c>(layer, subLayer, sequence)</c>. Idempotent, and a no-op if nothing
    /// has been recorded since the last sort.
    /// </summary>
    public void Sort()
    {
        if (_sorted)
        {
            return;
        }

        _commands.Sort(static (a, b) =>
        {
            int cmp = a.Layer.CompareTo(b.Layer);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = a.SubLayer.CompareTo(b.SubLayer);
            return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
        });
        _sorted = true;
    }

    /// <summary>
    /// Sorts the queue and returns the commands in draw order. The span is invalidated by any
    /// further recording; use <see cref="ToSortedArray"/> to keep a copy.
    /// </summary>
    public ReadOnlySpan<DrawCmd> GetSortedCommands()
    {
        Sort();
        return CollectionsMarshal.AsSpan(_commands);
    }

    /// <summary>
    /// Sorts the queue and copies the commands into a new array, so a frame can be handed to a
    /// render thread while the UI thread starts recording the next one.
    /// </summary>
    public DrawCmd[] ToSortedArray()
    {
        Sort();
        return [.. _commands];
    }

    private void Add(DrawCmd cmd)
    {
        cmd.Layer = _state.Layer;
        cmd.SubLayer = _state.SubLayer;
        cmd.Sequence = _sequence++;
        cmd.Color = _state.Color;
        cmd.Clip = _state.Current.Clip;
        _commands.Add(cmd);
        _sorted = false;
    }

    /// <summary>A viewport plus the accumulated clip it is contained by.</summary>
    private readonly record struct Frame(Viewport Viewport, Viewport Clip);

    /// <summary>
    /// One level of draw state. <see cref="Base"/> is the frame inherited from the enclosing
    /// level: <c>SetViewport</c> is resolved against it, and <c>SetViewport()</c> resets to it.
    /// </summary>
    private record struct State(DrawColor Color, int Layer, int SubLayer, Frame Base, Frame Current);
}
