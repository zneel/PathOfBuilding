using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Pob.Rendering.Skia;

namespace Pob.Rendering.Hosting;

/// <summary>
/// The custom draw operation that leases Avalonia's Skia canvas and flushes one frame's worth of
/// already-sorted commands onto it.
/// </summary>
/// <remarks>
/// It holds a snapshot array rather than the live <see cref="DrawQueue"/>: Avalonia may run the
/// operation on the render thread while the UI thread is already recording the next frame.
/// </remarks>
internal sealed class DrawQueueDrawOperation : ICustomDrawOperation
{
    private readonly DrawCmd[] _commands;
    private readonly SkiaRenderOptions _options;

    public DrawQueueDrawOperation(Rect bounds, DrawCmd[] commands, SkiaRenderOptions options)
    {
        Bounds = bounds;
        _commands = commands;
        _options = options;
    }

    public Rect Bounds { get; }

    public void Dispose()
    {
    }

    /// <summary>Hit testing is the control's business; the operation itself claims nothing.</summary>
    public bool HitTest(Point p) => false;

    /// <summary>Every frame is a new operation, so no two are ever equal.</summary>
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Render(ImmediateDrawingContext context)
    {
        ISkiaSharpApiLeaseFeature? feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null)
        {
            // A non-Skia backend (e.g. the headless renderer in unit tests) — nothing to draw to.
            return;
        }

        using ISkiaSharpApiLease lease = feature.Lease();
        SkiaDrawQueueRenderer.Render(_commands.AsSpan(), lease.SkCanvas, _options);
    }
}
