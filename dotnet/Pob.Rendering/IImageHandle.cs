namespace Pob.Rendering;

/// <summary>
/// How a backend samples texture coordinates that fall outside the 0..1 range. PoB relies on
/// repeat for tiled backgrounds — <c>src/Modules/Main.lua:1489</c> draws the tree background with
/// texture coordinates of <c>viewPort.width / 100</c> — while sprite-sheet handles loaded with the
/// Lua <c>"CLAMP"</c> flag must clamp so neighbouring sprites do not bleed in.
/// </summary>
public enum ImageTileMode
{
    /// <summary>Texture coordinates wrap; the SimpleGraphic default.</summary>
    Repeat,

    /// <summary>Texture coordinates are clamped to the edge; the Lua <c>"CLAMP"</c> load flag.</summary>
    Clamp,
}

/// <summary>
/// A backend-agnostic texture reference. It stands in for the Lua <c>ImageHandle</c> userdata, and
/// is opaque to <see cref="DrawQueue"/>: the queue only stores the reference, and the backend that
/// flushes the queue resolves it to a concrete texture.
/// </summary>
public interface IImageHandle
{
    /// <summary>Texture width in pixels; 0 when the handle is not loaded.</summary>
    int Width { get; }

    /// <summary>Texture height in pixels; 0 when the handle is not loaded.</summary>
    int Height { get; }

    /// <summary>Whether the handle currently references usable pixels (Lua <c>IsValid</c>).</summary>
    bool IsValid { get; }

    /// <summary>How texture coordinates outside 0..1 are sampled.</summary>
    ImageTileMode TileMode { get; }
}
