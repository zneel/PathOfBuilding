using Pob.Rendering.Skia;
using Xunit;

namespace Pob.Rendering.Tests;

/// <summary>
/// Renders known draw-call sequences to an offscreen surface and diffs them against committed
/// reference PNGs. These are the acceptance tests for migration ticket 27.
/// </summary>
public sealed class GoldenRenderTests : IDisposable
{
    private const int Width = 200;
    private const int Height = 150;

    private readonly SkiaImageHandle _texture = TestTextures.CreateCheckerboard();
    private readonly SkiaImageHandle _clampedTexture = TestTextures.CreateCheckerboard(ImageTileMode.Clamp);

    /// <inheritdoc/>
    public void Dispose()
    {
        _texture.Dispose();
        _clampedTexture.Dispose();
    }

    /// <summary>
    /// Draw calls are sorted by layer, then sub-layer, then emission order — not executed
    /// immediately. The scene emits its layers out of order on purpose.
    /// </summary>
    [Fact]
    public void LayerSortOrdering() => GoldenImage.Verify("layer-sort", Width, Height, static queue =>
    {
        // Emitted first, drawn last: layer 10 is the modal-popup layer.
        queue.SetDrawLayer(10);
        queue.SetDrawColor(0.9f, 0.2f, 0.2f);
        queue.DrawImage(null, 40, 45, 120, 60);

        // Emitted second, drawn first: layer -100 is the tree background.
        queue.SetDrawLayer(-100);
        queue.SetDrawColor(0.15f, 0.15f, 0.35f);
        queue.DrawImage(null, 0, 0, Width, Height);

        queue.SetDrawLayer(0);
        queue.SetDrawColor(0.2f, 0.7f, 0.3f);
        queue.DrawImage(null, 20, 20, 110, 70);

        // Two overlapping fills on the same layer and sub-layer: the later emission must win in
        // the overlap, which is what makes the sort stable on sequence.
        queue.SetDrawLayer(5);
        queue.SetDrawColor(0.95f, 0.85f, 0.1f);
        queue.DrawImage(null, 10, 100, 90, 40);
        queue.SetDrawColor(0.1f, 0.8f, 0.85f);
        queue.DrawImage(null, 50, 112, 90, 30);

        // Sub-layer ordering inside layer 5: nil layer keeps the layer, as most of PoB calls it.
        queue.SetDrawLayer(null, 20);
        queue.SetDrawColor(0.8f, 0.2f, 0.8f, 0.75f);
        queue.DrawImage(null, 150, 10, 40, Height - 20);
    });

    /// <summary>
    /// A null image handle is a solid rectangle in the current tint. This is how PoB draws every
    /// solid fill, including through the <c>^xRRGGBB</c> string overload of <c>SetDrawColor</c>.
    /// </summary>
    [Fact]
    public void SolidRectFillsThroughNullHandle() => GoldenImage.Verify("solid-rect", Width, Height, static queue =>
    {
        queue.SetDrawColor(1f, 0.5f, 0f);
        queue.DrawImage(null, 10, 10, 80, 60);

        // Alpha blends against what is already there.
        queue.SetDrawColor(0f, 0f, 0f, 0.5f);
        queue.DrawImage(null, 50, 40, 100, 60);

        queue.SetDrawColor("^x33FF77");
        queue.DrawImage(null, 120, 95, 60, 40);

        queue.SetDrawColor("^8");
        queue.DrawImage(null, 5, 120, 60, 20);

        queue.SetDrawColor("^1");
        queue.DrawImage(null, 70, 120, 40, 20);
    });

    /// <summary>
    /// <c>SetViewport</c> both scissors and moves the origin, and the no-argument form resets it
    /// to the full screen.
    /// </summary>
    [Fact]
    public void ViewportClipsAndShiftsOrigin() => GoldenImage.Verify("viewport-clip", Width, Height, static queue =>
    {
        queue.SetDrawColor(0.25f, 0.25f, 0.3f);
        queue.DrawImage(null, 0, 0, Width, Height);

        queue.SetViewport(30, 20, 80, 60);

        // Far larger than the viewport in every direction: only the viewport survives.
        queue.SetDrawColor(0.9f, 0.3f, 0.3f);
        queue.DrawImage(null, -100, -100, 400, 400);

        // Drawn at the viewport's own origin, so it lands at absolute (30, 20).
        queue.SetDrawColor(0.95f, 0.95f, 0.2f);
        queue.DrawImage(null, 0, 0, 20, 20);

        queue.SetViewport();

        // Back to full-screen coordinates.
        queue.SetDrawColor(1f, 1f, 1f);
        queue.DrawImage(null, 0, 135, Width, 8);
    });

    /// <summary>
    /// The nesting that <c>src/Modules/ItemSlotHelper.lua:10-37</c> performs: a whole second
    /// renderer inside a slot. Inside the scope the nested content is clipped to the slot, its own
    /// <c>SetViewport()</c> reset falls back to the slot rather than the screen, and on exit the
    /// caller's viewport, layer and tint are all intact.
    /// </summary>
    [Fact]
    public void NestedViewportSaveRestore() => GoldenImage.Verify("nested-viewport", Width, Height, static queue =>
    {
        queue.SetDrawColor(0.2f, 0.2f, 0.25f);
        queue.DrawImage(null, 0, 0, Width, Height);

        // The outer panel.
        queue.SetViewport(20, 20, 160, 110);
        queue.SetDrawLayer(1);
        queue.SetDrawColor(0.45f, 0.45f, 0.5f);
        queue.DrawImage(null, 0, 0, 160, 110);

        using (queue.PushViewport(10, 10, 60, 60))
        {
            // The nested renderer clobbers layer, tint and viewport exactly as the Lua one does.
            queue.SetDrawLayer(15);
            queue.SetDrawColor(0.9f, 0.25f, 0.25f);
            queue.DrawImage(null, -50, -50, 400, 400);

            queue.SetViewport();
            queue.SetDrawColor(0.3f, 0.4f, 1f, 0.7f);
            queue.DrawImage(null, 0, 0, 200, 200);
        }

        // Restored: layer 1 again, so this bar draws *under* the nested content above, and the
        // outer viewport's origin is back, so it starts at absolute (20, 20).
        queue.SetDrawColor(0.95f, 0.9f, 0.2f);
        queue.DrawImage(null, 0, 0, 160, 22);

        queue.SetViewport();
        queue.SetDrawColor(1f, 1f, 1f);
        queue.DrawImage(null, 190, 0, 10, Height);
    });

    /// <summary>
    /// <c>DrawImageQuad</c> with a rotated quad and per-corner texture coordinates from an atlas
    /// region, next to the axis-aligned <c>DrawImage</c> forms for comparison.
    /// </summary>
    [Fact]
    public void RotatedQuadWithAtlasTexCoords()
    {
        SkiaImageHandle texture = _texture;
        SkiaImageHandle clamped = _clampedTexture;

        GoldenImage.Verify("quad-uv", Width, Height, queue =>
        {
            queue.SetDrawColor(0.15f, 0.15f, 0.15f);
            queue.DrawImage(null, 0, 0, Width, Height);

            // The whole texture, untinted.
            queue.SetDrawColor(1f, 1f, 1f);
            queue.DrawImage(texture, 8, 8, 56, 56);

            // A sub-rectangle of the texture, tinted — the atlas case.
            queue.SetDrawColor(1f, 0.6f, 0.6f);
            queue.DrawImage(clamped, 72, 8, 56, 56, 0.25f, 0.25f, 0.75f, 0.75f);

            // Texture coordinates beyond 1 tile, the way the tree background does.
            queue.SetDrawColor(0.8f, 0.8f, 1f);
            queue.DrawImage(texture, 136, 8, 56, 56, 0f, 0f, 2f, 2f);

            // A quad rotated 27 degrees about its centre, with per-corner texture coordinates
            // taken from an off-centre, non-square atlas region.
            const float Cx = 60f;
            const float Cy = 105f;
            const float Half = 34f;
            (float X, float Y)[] corners = Rotate(Cx, Cy, Half, 27f);
            queue.SetDrawColor(1f, 1f, 1f);
            queue.DrawImageQuad(
                clamped,
                corners[0].X, corners[0].Y,
                corners[1].X, corners[1].Y,
                corners[2].X, corners[2].Y,
                corners[3].X, corners[3].Y,
                0.10f, 0.20f,
                0.85f, 0.05f,
                0.95f, 0.70f,
                0.20f, 0.90f);

            // The same quad shape, tinted and untextured — the null-handle path through
            // DrawVertices.
            (float X, float Y)[] plain = Rotate(150f, 105f, 30f, -15f);
            queue.SetDrawColor(0.3f, 0.9f, 0.5f, 0.85f);
            queue.DrawImageQuad(
                null,
                plain[0].X, plain[0].Y,
                plain[1].X, plain[1].Y,
                plain[2].X, plain[2].Y,
                plain[3].X, plain[3].Y);
        });
    }

    /// <summary>Corners of a square rotated about its centre, clockwise from the top-left.</summary>
    private static (float X, float Y)[] Rotate(float cx, float cy, float half, float degrees)
    {
        float rad = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        (float X, float Y)[] unit = [(-half, -half), (half, -half), (half, half), (-half, half)];
        var result = new (float X, float Y)[4];
        for (int i = 0; i < 4; i++)
        {
            result[i] = (
                cx + (unit[i].X * cos) - (unit[i].Y * sin),
                cy + (unit[i].X * sin) + (unit[i].Y * cos));
        }

        return result;
    }
}
