using System.Runtime.CompilerServices;
using Pob.Rendering;
using Pob.Rendering.Skia;
using SkiaSharp;

namespace Pob.Rendering.Tests;

/// <summary>
/// Renders a draw queue to an offscreen <see cref="SKSurface"/> and compares the result with a
/// committed reference PNG.
/// </summary>
/// <remarks>
/// Set the environment variable <c>POB_UPDATE_GOLDENS=1</c> to (re)write the reference images
/// under <c>References/</c> in the test project's source directory instead of asserting. Review
/// and commit the result; without the variable a missing reference is a failure, so a forgotten
/// image cannot pass silently in CI.
/// </remarks>
internal static class GoldenImage
{
    /// <summary>Maximum tolerated absolute difference in any single channel.</summary>
    private const int MaxChannelDelta = 8;

    /// <summary>Maximum tolerated fraction of pixels differing by more than one unit in a channel.</summary>
    private const double MaxDifferingFraction = 0.005d;

    /// <summary>Renders <paramref name="draw"/> and asserts it matches <c>References/{name}.png</c>.</summary>
    /// <param name="name">Reference image name, without extension.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <param name="draw">Records the scene's draw calls.</param>
    /// <param name="options">Backend options, or <see langword="null"/> for the defaults.</param>
    public static void Verify(
        string name,
        int width,
        int height,
        Action<DrawQueue> draw,
        SkiaRenderOptions? options = null)
    {
        using SKBitmap actual = Render(width, height, draw, options);

        if (ShouldUpdate)
        {
            Write(ReferencePath(name), actual);
            return;
        }

        string path = ReferencePath(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Reference image '{name}.png' is missing. Re-run with POB_UPDATE_GOLDENS=1 to generate it, then review and commit it.",
                path);
        }

        using SKBitmap expected = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Could not decode reference image '{path}'.");

        Compare(name, expected, actual);
    }

    /// <summary>Renders a scene to an offscreen surface and returns the pixels.</summary>
    public static SKBitmap Render(
        int width,
        int height,
        Action<DrawQueue> draw,
        SkiaRenderOptions? options = null)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create an offscreen SKSurface.");

        // Opaque mid-grey ground: alpha blending and clipping are both visible against it.
        surface.Canvas.Clear(new SKColor(64, 64, 64));

        var queue = new DrawQueue(width, height);
        draw(queue);
        SkiaDrawQueueRenderer.Render(queue, surface.Canvas, options);
        surface.Canvas.Flush();

        var bitmap = new SKBitmap(info);
        if (!surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Could not read back the rendered surface.");
        }

        return bitmap;
    }

    private static bool ShouldUpdate =>
        Environment.GetEnvironmentVariable("POB_UPDATE_GOLDENS") is "1" or "true";

    private static string ReferencePath(string name, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "References", name + ".png");

    private static void Write(string path, SKBitmap bitmap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    private static void Compare(string name, SKBitmap expected, SKBitmap actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new GoldenImageMismatchException(
                $"'{name}': reference is {expected.Width}x{expected.Height} but the render is {actual.Width}x{actual.Height}.");
        }

        int differing = 0;
        int worst = 0;
        int worstX = -1;
        int worstY = -1;

        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                SKColor e = expected.GetPixel(x, y);
                SKColor a = actual.GetPixel(x, y);
                int delta = Math.Max(
                    Math.Max(Math.Abs(e.Red - a.Red), Math.Abs(e.Green - a.Green)),
                    Math.Max(Math.Abs(e.Blue - a.Blue), Math.Abs(e.Alpha - a.Alpha)));

                if (delta > 1)
                {
                    differing++;
                }

                if (delta > worst)
                {
                    worst = delta;
                    worstX = x;
                    worstY = y;
                }
            }
        }

        double fraction = (double)differing / (expected.Width * expected.Height);
        if (worst <= MaxChannelDelta && fraction <= MaxDifferingFraction)
        {
            return;
        }

        string artifact = Path.Combine(Path.GetTempPath(), "pob-rendering-golden", name + ".actual.png");
        Write(artifact, actual);
        throw new GoldenImageMismatchException(
            $"'{name}' does not match its reference: worst channel delta {worst} at ({worstX},{worstY}), " +
            $"{differing} of {expected.Width * expected.Height} pixels differ ({fraction:P2}). " +
            $"Rendered image written to '{artifact}'.");
    }
}

/// <summary>Thrown when a rendered scene does not match its reference image.</summary>
internal sealed class GoldenImageMismatchException(string message) : Exception(message);
