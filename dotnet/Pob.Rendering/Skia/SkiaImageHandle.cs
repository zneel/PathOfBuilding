using SkiaSharp;

namespace Pob.Rendering.Skia;

/// <summary>
/// An <see cref="IImageHandle"/> backed by an <see cref="SKImage"/>. It stands in for the Lua
/// <c>ImageHandle</c> userdata; the asynchronous loading, sprite-sheet slicing and arc-band
/// generation that the Lua handle also offers belong to the passive-tree asset work
/// (migration tickets 16 and 40) and are not part of this type.
/// </summary>
public sealed class SkiaImageHandle : IImageHandle, IDisposable
{
    private readonly bool _ownsImage;
    private SKImage? _image;

    /// <summary>Wraps an existing <see cref="SKImage"/>.</summary>
    /// <param name="image">The texture.</param>
    /// <param name="tileMode">How texture coordinates outside 0..1 are sampled.</param>
    /// <param name="ownsImage">Whether disposing this handle should also dispose the image.</param>
    public SkiaImageHandle(SKImage image, ImageTileMode tileMode = ImageTileMode.Repeat, bool ownsImage = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = image;
        _ownsImage = ownsImage;
        TileMode = tileMode;
    }

    /// <summary>The underlying texture, or <see langword="null"/> once the handle is disposed.</summary>
    public SKImage? Image => _image;

    /// <inheritdoc/>
    public int Width => _image?.Width ?? 0;

    /// <inheritdoc/>
    public int Height => _image?.Height ?? 0;

    /// <inheritdoc/>
    public bool IsValid => _image is not null;

    /// <inheritdoc/>
    public ImageTileMode TileMode { get; }

    /// <summary>Loads a texture from an encoded image file (Lua <c>imgHandle:Load</c>).</summary>
    /// <param name="path">Path to a PNG, JPEG or other Skia-decodable file.</param>
    /// <param name="tileMode">How texture coordinates outside 0..1 are sampled.</param>
    /// <exception cref="InvalidOperationException">The file could not be decoded.</exception>
    public static SkiaImageHandle Load(string path, ImageTileMode tileMode = ImageTileMode.Repeat)
    {
        SKImage image = SKImage.FromEncodedData(path)
            ?? throw new InvalidOperationException($"Could not decode image '{path}'.");
        return new SkiaImageHandle(image, tileMode);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsImage)
        {
            _image?.Dispose();
        }

        _image = null;
    }
}
