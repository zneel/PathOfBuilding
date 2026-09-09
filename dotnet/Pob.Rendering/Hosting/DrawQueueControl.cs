using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Pob.Rendering.Skia;

namespace Pob.Rendering.Hosting;

/// <summary>Carries the queue a <see cref="DrawQueueControl"/> wants filled for one frame.</summary>
/// <param name="queue">The queue to record draw calls into. It is already reset for this frame.</param>
public sealed class DrawQueueEventArgs(DrawQueue queue) : EventArgs
{
    /// <summary>The queue to record draw calls into.</summary>
    public DrawQueue Queue { get; } = queue;
}

/// <summary>
/// The Avalonia host control for the draw queue: it hands out a <see cref="DrawQueue"/> reset to
/// the control's size, then flushes the sorted result onto the Skia canvas it leases from the
/// drawing context.
/// </summary>
/// <remarks>
/// This is deliberately thin — the surface PoB's custom-rendered controls sit on, not an
/// application shell. Subclass it and override <see cref="OnDraw"/>, or attach a handler to
/// <see cref="DrawQueueRequested"/>. Draw coordinates are the control's own logical pixels;
/// Avalonia's transform, including DPI scaling, is already on the leased canvas.
/// </remarks>
public class DrawQueueControl : Control
{
    private readonly DrawQueue _queue = new();

    /// <summary>Raised each frame so a listener can record the frame's draw calls.</summary>
    public event EventHandler<DrawQueueEventArgs>? DrawQueueRequested;

    /// <summary>Backend options used when the queue is flushed.</summary>
    public SkiaRenderOptions RenderOptions { get; set; } = SkiaRenderOptions.Default;

    /// <summary>The queue this control records into. Valid to touch only during a draw pass.</summary>
    protected DrawQueue Queue => _queue;

    /// <inheritdoc/>
    public sealed override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Size size = Bounds.Size;
        if (size.Width <= 0d || size.Height <= 0d)
        {
            return;
        }

        double scale = (this.GetVisualRoot()?.RenderScaling) ?? 1d;
        _queue.BeginFrame((float)size.Width, (float)size.Height, (float)scale);
        OnDraw(_queue);

        if (_queue.Count == 0)
        {
            return;
        }

        context.Custom(new DrawQueueDrawOperation(new Rect(size), _queue.ToSortedArray(), RenderOptions));
    }

    /// <summary>
    /// Records this frame's draw calls. The base implementation raises
    /// <see cref="DrawQueueRequested"/>.
    /// </summary>
    /// <param name="queue">The queue to record into, already reset for this frame.</param>
    protected virtual void OnDraw(DrawQueue queue) =>
        DrawQueueRequested?.Invoke(this, new DrawQueueEventArgs(queue));
}
