namespace Pob.Rendering;

/// <summary>
/// A snapshot of the draw state that <see cref="DrawQueue.SaveState"/> returns and
/// <see cref="DrawQueue.RestoreState"/> puts back: the tint, the layer pair, and the viewport.
/// </summary>
/// <param name="Color">The tint, as <c>GetDrawColor</c> returns it.</param>
/// <param name="Layer">The layer, as <c>GetDrawLayer</c> returns it.</param>
/// <param name="SubLayer">The sub-layer.</param>
/// <param name="Viewport">The viewport rectangle in absolute screen coordinates.</param>
public readonly record struct DrawState(DrawColor Color, int Layer, int SubLayer, Viewport Viewport);

/// <summary>
/// The scope returned by <see cref="DrawQueue.PushState"/>. Disposing it restores the tint, layer
/// and viewport that were in effect when the scope was opened.
/// </summary>
public readonly struct DrawStateScope : IDisposable
{
    private readonly DrawQueue? _queue;
    private readonly int _depth;

    internal DrawStateScope(DrawQueue queue, int depth)
    {
        _queue = queue;
        _depth = depth;
    }

    /// <summary>Restores the state captured when the scope was opened.</summary>
    public void Dispose() => _queue?.PopStateTo(_depth);
}
