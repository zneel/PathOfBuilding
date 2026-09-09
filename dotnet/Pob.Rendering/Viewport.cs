namespace Pob.Rendering;

/// <summary>
/// An axis-aligned rectangle in absolute screen coordinates. Used both for the scissor rectangle
/// a <c>SetViewport</c> call establishes and for the origin it shifts drawing to.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width; may be zero or negative, in which case the viewport is empty.</param>
/// <param name="Height">Height; may be zero or negative, in which case the viewport is empty.</param>
public readonly record struct Viewport(float X, float Y, float Width, float Height)
{
    /// <summary>Right edge.</summary>
    public float Right => X + Width;

    /// <summary>Bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>True when the viewport has no area, so nothing clipped to it can be visible.</summary>
    public bool IsEmpty => Width <= 0f || Height <= 0f;

    /// <summary>Returns the intersection of this viewport with <paramref name="other"/>.</summary>
    public Viewport Intersect(Viewport other)
    {
        float left = MathF.Max(X, other.X);
        float top = MathF.Max(Y, other.Y);
        float right = MathF.Min(Right, other.Right);
        float bottom = MathF.Min(Bottom, other.Bottom);
        return new Viewport(left, top, MathF.Max(0f, right - left), MathF.Max(0f, bottom - top));
    }

    /// <summary>Returns this viewport offset by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public Viewport Offset(float dx, float dy) => this with { X = X + dx, Y = Y + dy };
}
