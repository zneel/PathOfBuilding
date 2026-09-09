using SkiaSharp;

namespace Pob.Rendering.Skia;

/// <summary>Backend options for <see cref="SkiaDrawQueueRenderer"/>.</summary>
public sealed class SkiaRenderOptions
{
    /// <summary>The defaults used when no options are supplied.</summary>
    public static SkiaRenderOptions Default { get; } = new();

    /// <summary>
    /// Whether edges are antialiased. Skia does not antialias <c>DrawVertices</c>, so this only
    /// affects rectangles.
    /// </summary>
    public bool Antialias { get; init; } = true;

    /// <summary>Sampling quality used when a texture is scaled.</summary>
    public SKFilterQuality FilterQuality { get; init; } = SKFilterQuality.Medium;
}

/// <summary>
/// Flushes a sorted <see cref="DrawQueue"/> to an <see cref="SKCanvas"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>A solid fill (null image handle) is an <see cref="SKCanvas.DrawRect(SKRect, SKPaint)"/> in the tint colour.</description></item>
/// <item><description>A textured rectangle is a <c>DrawRect</c> with an image shader whose local matrix maps the destination rectangle onto the requested texture coordinates, so coordinates outside 0..1 tile or clamp per the handle's <see cref="IImageHandle.TileMode"/>.</description></item>
/// <item><description>A quad is <see cref="SKCanvas.DrawVertices(SKVertexMode, SKPoint[], SKPoint[], SKColor[], SKPaint)"/> with two triangles, which is the primitive that maps a texture coordinate per corner without any hand-rolled transform.</description></item>
/// <item><description>The tint is applied as a per-channel multiply through an <see cref="SKColorFilter"/>, matching SimpleGraphic's modulation of the texture by the current draw colour. An opaque white tint skips the filter.</description></item>
/// <item><description>A viewport is an <see cref="SKCanvas.ClipRect(SKRect, SKClipOperation, bool)"/>; the clip is re-applied only when it changes between consecutive commands, so same-viewport runs batch.</description></item>
/// </list>
/// <para>
/// A command whose handle is not a loaded <see cref="SkiaImageHandle"/> draws nothing. What
/// SimpleGraphic does with a handle that is still loading asynchronously is not determinable from
/// the Lua sources, and drawing nothing at least avoids untextured blocks flashing while sprite
/// sheets stream in.
/// </para>
/// </remarks>
public static class SkiaDrawQueueRenderer
{
    /// <summary>Sorts the queue and draws it.</summary>
    /// <param name="queue">The queue to flush.</param>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="options">Backend options, or <see langword="null"/> for the defaults.</param>
    public static void Render(DrawQueue queue, SKCanvas canvas, SkiaRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(queue);
        Render(queue.GetSortedCommands(), canvas, options);
    }

    /// <summary>Draws commands that are already in sorted order.</summary>
    /// <param name="commands">Commands in draw order.</param>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="options">Backend options, or <see langword="null"/> for the defaults.</param>
    public static void Render(ReadOnlySpan<DrawCmd> commands, SKCanvas canvas, SkiaRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        options ??= SkiaRenderOptions.Default;

        var tints = new Dictionary<DrawColor, SKColorFilter>();
        var quadShaders = new Dictionary<SKImage, SKShader>();
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = options.Antialias,
            FilterQuality = options.FilterQuality,
        };

        int restoreCount = canvas.Save();
        Viewport lastClip = default;
        bool clipApplied = false;

        try
        {
            foreach (ref readonly DrawCmd cmd in commands)
            {
                if (cmd.Clip.IsEmpty)
                {
                    continue;
                }

                if (!clipApplied || cmd.Clip != lastClip)
                {
                    canvas.RestoreToCount(restoreCount);
                    canvas.Save();
                    canvas.ClipRect(ToRect(cmd.Clip), SKClipOperation.Intersect, antialias: false);
                    lastClip = cmd.Clip;
                    clipApplied = true;
                }

                switch (cmd.Kind)
                {
                    case DrawCmdKind.Rect:
                        DrawSolidRect(canvas, in cmd, paint);
                        break;
                    case DrawCmdKind.Image:
                        DrawTexturedRect(canvas, in cmd, paint, tints);
                        break;
                    case DrawCmdKind.Quad:
                        DrawQuad(canvas, in cmd, paint, tints, quadShaders);
                        break;
                    default:
                        break;
                }
            }
        }
        finally
        {
            paint.Shader = null;
            paint.ColorFilter = null;
            canvas.RestoreToCount(restoreCount);
            foreach (SKColorFilter filter in tints.Values)
            {
                filter.Dispose();
            }

            foreach (SKShader shader in quadShaders.Values)
            {
                shader.Dispose();
            }
        }
    }

    private static void DrawSolidRect(SKCanvas canvas, ref readonly DrawCmd cmd, SKPaint paint)
    {
        paint.Shader = null;
        paint.ColorFilter = null;
        paint.Color = ToColor(cmd.Color);
        canvas.DrawRect(ToRect(in cmd), paint);
    }

    private static void DrawTexturedRect(
        SKCanvas canvas,
        ref readonly DrawCmd cmd,
        SKPaint paint,
        Dictionary<DrawColor, SKColorFilter> tints)
    {
        SKImage? image = Resolve(cmd.Image);
        if (image is null)
        {
            return;
        }

        SKRect rect = ToRect(in cmd);
        if (rect.Width == 0f || rect.Height == 0f)
        {
            return;
        }

        // The shader's local matrix maps its own space — texture pixels — onto the canvas, so it
        // is the mapping that puts texture coordinate s1 at the destination's left edge and s3 at
        // its right edge. Skia inverts it to sample.
        float spanX = (cmd.S3 - cmd.S1) * image.Width;
        float spanY = (cmd.T3 - cmd.T1) * image.Height;
        if (spanX == 0f || spanY == 0f)
        {
            return;
        }

        float scaleX = cmd.Width / spanX;
        float scaleY = cmd.Height / spanY;
        SKMatrix local = SKMatrix.CreateScaleTranslation(
            scaleX,
            scaleY,
            cmd.Left - (cmd.S1 * image.Width * scaleX),
            cmd.Top - (cmd.T1 * image.Height * scaleY));

        SKShaderTileMode tile = ToTileMode(cmd.Image!.TileMode);
        using SKShader shader = SKShader.CreateImage(image, tile, tile, local);
        paint.Color = SKColors.White;
        paint.Shader = shader;
        paint.ColorFilter = GetTint(tints, cmd.Color);
        canvas.DrawRect(rect, paint);
        paint.Shader = null;
        paint.ColorFilter = null;
    }

    private static void DrawQuad(
        SKCanvas canvas,
        ref readonly DrawCmd cmd,
        SKPaint paint,
        Dictionary<DrawColor, SKColorFilter> tints,
        Dictionary<SKImage, SKShader> shaders)
    {
        // Two triangles over the four corners: (1, 2, 3) and (1, 3, 4).
        SKPoint[] positions =
        [
            new(cmd.X1, cmd.Y1), new(cmd.X2, cmd.Y2), new(cmd.X3, cmd.Y3),
            new(cmd.X1, cmd.Y1), new(cmd.X3, cmd.Y3), new(cmd.X4, cmd.Y4),
        ];

        SKImage? image = Resolve(cmd.Image);
        if (image is null)
        {
            if (cmd.Image is not null)
            {
                return;
            }

            // Untextured quad: the tint goes in as vertex colours.
            SKColor solid = ToColor(cmd.Color);
            SKColor[] solidColors = [solid, solid, solid, solid, solid, solid];
            paint.Shader = null;
            paint.ColorFilter = null;
            paint.Color = solid;
            canvas.DrawVertices(SKVertexMode.Triangles, positions, solidColors, paint);
            return;
        }

        // DrawVertices samples the paint's shader, whose coordinate space is texture pixels.
        SKPoint[] texCoords =
        [
            new(cmd.S1 * image.Width, cmd.T1 * image.Height),
            new(cmd.S2 * image.Width, cmd.T2 * image.Height),
            new(cmd.S3 * image.Width, cmd.T3 * image.Height),
            new(cmd.S1 * image.Width, cmd.T1 * image.Height),
            new(cmd.S3 * image.Width, cmd.T3 * image.Height),
            new(cmd.S4 * image.Width, cmd.T4 * image.Height),
        ];

        // Vertex colours stay white; the tint is a colour filter, exactly as for textured rects,
        // so a quad and a rectangle drawn with the same tint come out identical.
        SKColor[] colors = [SKColors.White, SKColors.White, SKColors.White, SKColors.White, SKColors.White, SKColors.White];

        // The quad's shader always has an identity local matrix — texture coordinates are passed
        // per vertex — so one shader per texture is reusable across the whole frame. The passive
        // tree draws thousands of connector quads from a handful of atlases.
        if (!shaders.TryGetValue(image, out SKShader? shader))
        {
            SKShaderTileMode tile = ToTileMode(cmd.Image!.TileMode);
            shader = SKShader.CreateImage(image, tile, tile);
            shaders[image] = shader;
        }

        paint.Color = SKColors.White;
        paint.Shader = shader;
        paint.ColorFilter = GetTint(tints, cmd.Color);
        canvas.DrawVertices(SKVertexMode.Triangles, positions, texCoords, colors, paint);
        paint.Shader = null;
        paint.ColorFilter = null;
    }

    private static SKImage? Resolve(IImageHandle? handle) =>
        handle is SkiaImageHandle { IsValid: true } skia ? skia.Image : null;

    private static SKColorFilter? GetTint(Dictionary<DrawColor, SKColorFilter> tints, DrawColor color)
    {
        DrawColor clamped = color.Clamped();
        if (clamped == DrawColor.White)
        {
            return null;
        }

        if (!tints.TryGetValue(clamped, out SKColorFilter? filter))
        {
            // Per-channel multiply, including alpha. Skia's colour matrix operates on
            // unpremultiplied colours, so a diagonal matrix is exactly texture * tint.
            filter = SKColorFilter.CreateColorMatrix(
            [
                clamped.R, 0f, 0f, 0f, 0f,
                0f, clamped.G, 0f, 0f, 0f,
                0f, 0f, clamped.B, 0f, 0f,
                0f, 0f, 0f, clamped.A, 0f,
            ]);
            tints[clamped] = filter;
        }

        return filter;
    }

    private static SKShaderTileMode ToTileMode(ImageTileMode mode) =>
        mode == ImageTileMode.Clamp ? SKShaderTileMode.Clamp : SKShaderTileMode.Repeat;

    private static SKColor ToColor(DrawColor color)
    {
        DrawColor c = color.Clamped();
        return new SKColor(
            (byte)MathF.Round(c.R * 255f),
            (byte)MathF.Round(c.G * 255f),
            (byte)MathF.Round(c.B * 255f),
            (byte)MathF.Round(c.A * 255f));
    }

    private static SKRect ToRect(Viewport viewport) =>
        SKRect.Create(viewport.X, viewport.Y, viewport.Width, viewport.Height);

    private static SKRect ToRect(ref readonly DrawCmd cmd) =>
        new SKRect(cmd.Left, cmd.Top, cmd.Right, cmd.Bottom).Standardized;
}
