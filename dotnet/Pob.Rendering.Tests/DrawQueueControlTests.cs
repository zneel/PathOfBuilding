using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Pob.Rendering.Hosting;
using SkiaSharp;
using Xunit;

namespace Pob.Rendering.Tests;

/// <summary>
/// End-to-end check of the Avalonia host control: it must lease the Skia canvas through
/// <c>ISkiaSharpApiLeaseFeature</c> and get the queue onto the window's real framebuffer. This
/// runs on Avalonia's headless platform with the Skia renderer, not the headless stub renderer.
/// </summary>
public sealed class DrawQueueControlTests
{
    [Fact]
    public async Task ControlRendersItsQueueThroughTheSkiaLease()
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        SKColor[] samples = await session.Dispatch(
            () =>
            {
                var control = new DrawQueueControl();
                control.DrawQueueRequested += (_, e) =>
                {
                    // Layer 10 is emitted first but must land on top of layer 0.
                    e.Queue.SetDrawLayer(10);
                    e.Queue.SetDrawColor(0f, 0f, 1f);
                    e.Queue.DrawImage(null, 20, 20, 40, 40);

                    e.Queue.SetDrawLayer(0);
                    e.Queue.SetDrawColor(1f, 0f, 0f);
                    e.Queue.DrawImage(null, 0, 0, 100, 100);
                };

                var window = new Window
                {
                    SystemDecorations = SystemDecorations.None,
                    Width = 100,
                    Height = 100,
                    Content = control,
                };

                window.Show();
                using WriteableBitmap frame = window.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("The headless window rendered no frame.");

                return Sample(frame, [(5, 5), (40, 40)]);
            },
            CancellationToken.None);

        Assert.Equal(new SKColor(255, 0, 0, 255), samples[0]);
        Assert.Equal(new SKColor(0, 0, 255, 255), samples[1]);
    }

    private static SKColor[] Sample(WriteableBitmap bitmap, (int X, int Y)[] points)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        using SKBitmap decoded = SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException("Could not decode the captured frame.");

        var result = new SKColor[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            result[i] = decoded.GetPixel(points[i].X, points[i].Y);
        }

        return result;
    }

    /// <summary>The minimal application the headless session hosts.</summary>
    private sealed class TestApp : Application
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<TestApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
