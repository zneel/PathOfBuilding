using Pob.Rendering;
using Pob.Rendering.Skia;
using SkiaSharp;

namespace Pob.Rendering.Tests;

/// <summary>
/// Procedurally generated textures, so the golden tests need no binary asset beyond the reference
/// images themselves and are byte-for-byte reproducible.
/// </summary>
internal static class TestTextures
{
    /// <summary>
    /// A 64x64 texture whose four quadrants are distinctly coloured and which carries an 8-pixel
    /// checker pattern. Quadrant colour makes the orientation of a UV mapping obvious; the checker
    /// makes scaling and tiling obvious.
    /// </summary>
    public static SkiaImageHandle CreateCheckerboard(ImageTileMode tileMode = ImageTileMode.Repeat)
    {
        const int Size = 64;
        var bitmap = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                SKColor baseColor = (x < Size / 2, y < Size / 2) switch
                {
                    (true, true) => new SKColor(220, 40, 40),
                    (false, true) => new SKColor(40, 200, 60),
                    (true, false) => new SKColor(50, 90, 230),
                    (false, false) => new SKColor(230, 210, 40),
                };

                bool dark = ((x / 8) + (y / 8)) % 2 == 0;
                if (dark)
                {
                    baseColor = new SKColor(
                        (byte)(baseColor.Red / 3),
                        (byte)(baseColor.Green / 3),
                        (byte)(baseColor.Blue / 3));
                }

                bitmap.SetPixel(x, y, baseColor);
            }
        }

        bitmap.SetImmutable();
        return new SkiaImageHandle(SKImage.FromBitmap(bitmap), tileMode);
    }
}
