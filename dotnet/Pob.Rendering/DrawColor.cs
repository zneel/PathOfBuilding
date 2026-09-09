namespace Pob.Rendering;

/// <summary>
/// The current draw tint, in linear 0..1 float components exactly as the Lua host API uses them
/// (<c>SetDrawColor(r, g, b, a)</c>). Components are not clamped on construction; clamping happens
/// when a backend converts the colour to a concrete 8-bit value.
/// </summary>
/// <param name="R">Red component, nominally 0..1.</param>
/// <param name="G">Green component, nominally 0..1.</param>
/// <param name="B">Blue component, nominally 0..1.</param>
/// <param name="A">Alpha component, nominally 0..1.</param>
public readonly record struct DrawColor(float R, float G, float B, float A)
{
    /// <summary>Opaque white — the initial tint of a fresh frame, matching SimpleGraphic.</summary>
    public static DrawColor White => new(1f, 1f, 1f, 1f);

    /// <summary>Creates an opaque colour.</summary>
    public DrawColor(float r, float g, float b) : this(r, g, b, 1f)
    {
    }

    /// <summary>Returns this colour with every component clamped to 0..1.</summary>
    public DrawColor Clamped() => new(Clamp(R), Clamp(G), Clamp(B), Clamp(A));

    private static float Clamp(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
