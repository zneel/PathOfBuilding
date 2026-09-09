namespace Pob.Rendering;

/// <summary>The geometry kind of a buffered draw call.</summary>
public enum DrawCmdKind
{
    /// <summary>A solid, axis-aligned rectangle — <c>DrawImage(nil, ...)</c>.</summary>
    Rect,

    /// <summary>A textured, axis-aligned rectangle — <c>DrawImage(handle, ...)</c>.</summary>
    Image,

    /// <summary>An arbitrary four-corner quad — <c>DrawImageQuad(...)</c>.</summary>
    Quad,
}

/// <summary>
/// One buffered draw call. Every command carries the full state it was recorded with — tint, clip
/// rectangle, layer — because the queue is sorted before it is flushed, so state cannot be
/// replayed in emission order.
/// </summary>
/// <remarks>
/// Geometry is stored uniformly as four corners in clockwise order starting top-left, in absolute
/// screen coordinates (the viewport origin is already applied). <see cref="DrawCmdKind.Rect"/> and
/// <see cref="DrawCmdKind.Image"/> commands are axis-aligned, so corner 1 is the top-left and
/// corner 3 the bottom-right; <see cref="Left"/> and friends read them back.
/// </remarks>
public struct DrawCmd
{
    /// <summary>Geometry kind.</summary>
    public DrawCmdKind Kind;

    /// <summary>Primary sort key — the <c>layer</c> argument of <c>SetDrawLayer</c>.</summary>
    public int Layer;

    /// <summary>Secondary sort key — the <c>subLayer</c> argument of <c>SetDrawLayer</c>.</summary>
    public int SubLayer;

    /// <summary>
    /// Final sort key: the order the command was recorded in. It makes the sort total, so
    /// commands sharing a layer and sub-layer keep emission order.
    /// </summary>
    public int Sequence;

    /// <summary>The tint in effect when the command was recorded.</summary>
    public DrawColor Color;

    /// <summary>The absolute scissor rectangle in effect when the command was recorded.</summary>
    public Viewport Clip;

    /// <summary>The texture, or <see langword="null"/> for an untextured solid fill.</summary>
    public IImageHandle? Image;

    /// <summary>Corner 1 (top-left for axis-aligned commands), X.</summary>
    public float X1;

    /// <summary>Corner 1 Y.</summary>
    public float Y1;

    /// <summary>Corner 2 (top-right for axis-aligned commands), X.</summary>
    public float X2;

    /// <summary>Corner 2 Y.</summary>
    public float Y2;

    /// <summary>Corner 3 (bottom-right for axis-aligned commands), X.</summary>
    public float X3;

    /// <summary>Corner 3 Y.</summary>
    public float Y3;

    /// <summary>Corner 4 (bottom-left for axis-aligned commands), X.</summary>
    public float X4;

    /// <summary>Corner 4 Y.</summary>
    public float Y4;

    /// <summary>Texture coordinate at corner 1, U.</summary>
    public float S1;

    /// <summary>Texture coordinate at corner 1, V.</summary>
    public float T1;

    /// <summary>Texture coordinate at corner 2, U.</summary>
    public float S2;

    /// <summary>Texture coordinate at corner 2, V.</summary>
    public float T2;

    /// <summary>Texture coordinate at corner 3, U.</summary>
    public float S3;

    /// <summary>Texture coordinate at corner 3, V.</summary>
    public float T3;

    /// <summary>Texture coordinate at corner 4, U.</summary>
    public float S4;

    /// <summary>Texture coordinate at corner 4, V.</summary>
    public float T4;

    /// <summary>Left edge of an axis-aligned command.</summary>
    public readonly float Left => X1;

    /// <summary>Top edge of an axis-aligned command.</summary>
    public readonly float Top => Y1;

    /// <summary>Right edge of an axis-aligned command.</summary>
    public readonly float Right => X3;

    /// <summary>Bottom edge of an axis-aligned command.</summary>
    public readonly float Bottom => Y3;

    /// <summary>Width of an axis-aligned command.</summary>
    public readonly float Width => X3 - X1;

    /// <summary>Height of an axis-aligned command.</summary>
    public readonly float Height => Y3 - Y1;

    /// <inheritdoc/>
    public override readonly string ToString() =>
        $"{Kind} layer={Layer}.{SubLayer} seq={Sequence} " +
        $"rect=({X1},{Y1})-({X3},{Y3}) color=({Color.R},{Color.G},{Color.B},{Color.A}) clip={Clip}";
}
