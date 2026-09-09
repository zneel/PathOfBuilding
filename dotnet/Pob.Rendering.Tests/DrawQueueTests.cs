using Xunit;

namespace Pob.Rendering.Tests;

/// <summary>Semantics of the buffered draw queue, independent of any backend.</summary>
public sealed class DrawQueueTests
{
    private static DrawQueue NewQueue() => new(800f, 600f);

    [Fact]
    public void CommandsSortByLayerThenSubLayerThenSequence()
    {
        DrawQueue queue = NewQueue();

        queue.SetDrawLayer(100);
        queue.DrawImage(null, 0, 0, 1, 1);      // seq 0
        queue.SetDrawLayer(-100);
        queue.DrawImage(null, 0, 0, 2, 2);      // seq 1
        queue.SetDrawLayer(0, 30);
        queue.DrawImage(null, 0, 0, 3, 3);      // seq 2
        queue.SetDrawLayer(0, 15);
        queue.DrawImage(null, 0, 0, 4, 4);      // seq 3
        queue.SetDrawLayer(0);
        queue.DrawImage(null, 0, 0, 5, 5);      // seq 4

        int[] order = [.. queue.ToSortedArray().Select(c => c.Sequence)];
        Assert.Equal([1, 4, 3, 2, 0], order);
    }

    [Fact]
    public void SortIsStableWithinALayer()
    {
        DrawQueue queue = NewQueue();
        queue.SetDrawLayer(5);

        for (int i = 0; i < 64; i++)
        {
            // Alternate sub-layers so the comparer has ties to break on sequence.
            queue.SetDrawLayer(null, i % 2 == 0 ? 1 : 0);
            queue.DrawImage(null, i, 0, 1, 1);
        }

        DrawCmd[] sorted = queue.ToSortedArray();
        int[] subZero = [.. sorted.Where(c => c.SubLayer == 0).Select(c => c.Sequence)];
        int[] subOne = [.. sorted.Where(c => c.SubLayer == 1).Select(c => c.Sequence)];

        Assert.Equal(subZero.OrderBy(s => s), subZero);
        Assert.Equal(subOne.OrderBy(s => s), subOne);
        Assert.All(sorted.Take(32), c => Assert.Equal(0, c.SubLayer));
        Assert.All(sorted.Skip(32), c => Assert.Equal(1, c.SubLayer));
    }

    [Fact]
    public void NullLayerKeepsTheCurrentLayer()
    {
        DrawQueue queue = NewQueue();

        queue.SetDrawLayer(10);
        Assert.Equal(10, queue.GetDrawLayer());
        Assert.Equal(0, queue.GetDrawSubLayer());

        queue.SetDrawLayer(null, 25);
        Assert.Equal(10, queue.GetDrawLayer());
        Assert.Equal(25, queue.GetDrawSubLayer());

        // The single-argument form resets the sub-layer.
        queue.SetDrawLayer(3);
        Assert.Equal(3, queue.GetDrawLayer());
        Assert.Equal(0, queue.GetDrawSubLayer());
    }

    [Fact]
    public void CommandsCaptureTheStateTheyWereRecordedWith()
    {
        DrawQueue queue = NewQueue();

        queue.SetDrawLayer(1);
        queue.SetDrawColor(1f, 0f, 0f);
        queue.DrawImage(null, 0, 0, 10, 10);

        queue.SetDrawLayer(-1);
        queue.SetDrawColor(0f, 0f, 1f, 0.25f);
        queue.DrawImage(null, 0, 0, 10, 10);

        DrawCmd[] sorted = queue.ToSortedArray();
        Assert.Equal(new DrawColor(0f, 0f, 1f, 0.25f), sorted[0].Color);
        Assert.Equal(new DrawColor(1f, 0f, 0f, 1f), sorted[1].Color);
    }

    [Fact]
    public void GetDrawColorRoundTripsForSaveRestore()
    {
        // src/Classes/Tooltip.lua:613 saves the tint before drawing a background image and puts it
        // back afterwards.
        DrawQueue queue = NewQueue();
        queue.SetDrawColor(0.25f, 0.5f, 0.75f, 0.5f);

        DrawColor saved = queue.GetDrawColor();
        queue.SetDrawColor(1f, 1f, 1f);
        queue.SetDrawColor(saved);

        Assert.Equal(new DrawColor(0.25f, 0.5f, 0.75f, 0.5f), queue.GetDrawColor());
    }

    [Fact]
    public void ViewportShiftsTheOriginAndClips()
    {
        DrawQueue queue = NewQueue();
        queue.SetViewport(100, 50, 200, 150);

        queue.DrawImage(null, 10, 10, 20, 20);

        DrawCmd cmd = queue.ToSortedArray()[0];
        Assert.Equal(110f, cmd.Left);
        Assert.Equal(60f, cmd.Top);
        Assert.Equal(new Viewport(100, 50, 200, 150), cmd.Clip);
    }

    [Fact]
    public void ConsecutiveViewportsReplaceRatherThanCompose()
    {
        // The Lua original's viewport is absolute; two calls in a row do not nest. See
        // src/Classes/CompareTab.lua:3200-3210.
        DrawQueue queue = NewQueue();
        queue.SetViewport(100, 50, 200, 150);
        queue.SetViewport(10, 20, 30, 40);

        Assert.Equal(new Viewport(10, 20, 30, 40), queue.CurrentViewport);
        Assert.Equal(new Viewport(10, 20, 30, 40), queue.CurrentClip);
    }

    [Fact]
    public void NoArgViewportResetsToTheFullScreen()
    {
        DrawQueue queue = NewQueue();
        queue.SetViewport(100, 50, 200, 150);
        queue.SetViewport();

        Assert.Equal(new Viewport(0, 0, 800, 600), queue.CurrentViewport);
        Assert.Equal((0f, 0f), queue.Origin);
    }

    [Fact]
    public void NestedViewportComposesOriginAndClip()
    {
        DrawQueue queue = NewQueue();
        queue.SetViewport(100, 100, 200, 200);

        using (queue.PushViewport(50, 50, 400, 400))
        {
            // Origin composes; the clip is intersected with the parent's, so the nested render
            // cannot draw outside the slot it was given.
            Assert.Equal(new Viewport(150, 150, 400, 400), queue.CurrentViewport);
            Assert.Equal(new Viewport(150, 150, 150, 150), queue.CurrentClip);

            queue.DrawImage(null, 0, 0, 10, 10);
        }

        DrawCmd cmd = queue.ToSortedArray()[0];
        Assert.Equal(150f, cmd.Left);
        Assert.Equal(new Viewport(150, 150, 150, 150), cmd.Clip);
    }

    [Fact]
    public void NestedViewportResetFallsBackToTheEnclosingViewport()
    {
        // The gotcha from src/Modules/ItemSlotHelper.lua: the nested renderer ends with a bare
        // SetViewport(), which in the Lua original escapes to the full screen and clobbers the
        // caller. Inside a pushed scope it must fall back to the scope's own viewport instead.
        DrawQueue queue = NewQueue();
        queue.SetViewport(100, 100, 200, 200);

        using (queue.PushViewport(20, 20, 60, 60))
        {
            queue.SetViewport(5, 5, 10, 10);
            queue.SetViewport();

            Assert.Equal(new Viewport(120, 120, 60, 60), queue.CurrentViewport);
            Assert.Equal(new Viewport(120, 120, 60, 60), queue.CurrentClip);
        }

        Assert.Equal(new Viewport(100, 100, 200, 200), queue.CurrentViewport);
    }

    [Fact]
    public void PushStateRestoresColorLayerAndViewport()
    {
        DrawQueue queue = NewQueue();
        queue.SetDrawLayer(1, 5);
        queue.SetDrawColor(0.5f, 0.5f, 0.5f, 0.5f);
        queue.SetViewport(10, 10, 100, 100);

        using (queue.PushState())
        {
            queue.SetDrawLayer(15, 30);
            queue.SetDrawColor(1f, 0f, 0f);
            queue.SetViewport(1, 1, 5, 5);
        }

        Assert.Equal(1, queue.GetDrawLayer());
        Assert.Equal(5, queue.GetDrawSubLayer());
        Assert.Equal(new DrawColor(0.5f, 0.5f, 0.5f, 0.5f), queue.GetDrawColor());
        Assert.Equal(new Viewport(10, 10, 100, 100), queue.CurrentViewport);
        Assert.Equal(0, queue.StateDepth);
    }

    [Fact]
    public void SaveAndRestoreStateWithoutAScope()
    {
        DrawQueue queue = NewQueue();
        queue.SetDrawLayer(20, 3);
        queue.SetDrawColor("^x8888FF");
        queue.SetViewport(4, 5, 6, 7);
        DrawState saved = queue.SaveState();

        queue.SetDrawLayer(0);
        queue.SetDrawColor(1f, 1f, 1f);
        queue.SetViewport();
        queue.RestoreState(saved);

        Assert.Equal(20, queue.GetDrawLayer());
        Assert.Equal(3, queue.GetDrawSubLayer());
        Assert.Equal(ColorEscape.Parse("^x8888FF"), queue.GetDrawColor());
        Assert.Equal(new Viewport(4, 5, 6, 7), queue.CurrentViewport);
    }

    [Fact]
    public void PopStateWithoutPushThrows()
    {
        DrawQueue queue = NewQueue();
        Assert.Throws<InvalidOperationException>(queue.PopState);
    }

    [Fact]
    public void BeginFrameClearsCommandsAndState()
    {
        DrawQueue queue = NewQueue();
        queue.SetDrawLayer(9, 9);
        queue.SetDrawColor(1f, 0f, 0f);
        queue.SetViewport(1, 2, 3, 4);
        queue.DrawImage(null, 0, 0, 1, 1);

        queue.BeginFrame(640, 480, 1.5f);

        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.GetDrawLayer());
        Assert.Equal(0, queue.GetDrawSubLayer());
        Assert.Equal(DrawColor.White, queue.GetDrawColor());
        Assert.Equal(new Viewport(0, 0, 640, 480), queue.CurrentViewport);
        Assert.Equal(1.5f, queue.ScreenScale);
    }

    [Fact]
    public void DrawImageRecordsTextureCoordinatesPerCorner()
    {
        DrawQueue queue = NewQueue();
        using var texture = TestTextures.CreateCheckerboard();

        queue.DrawImage(texture, 10, 20, 30, 40, 0.25f, 0.5f, 0.75f, 1f);

        DrawCmd cmd = queue.ToSortedArray()[0];
        Assert.Equal(DrawCmdKind.Image, cmd.Kind);
        Assert.Equal(30f, cmd.Width);
        Assert.Equal(40f, cmd.Height);
        Assert.Equal((0.25f, 0.5f), (cmd.S1, cmd.T1));
        Assert.Equal((0.75f, 0.5f), (cmd.S2, cmd.T2));
        Assert.Equal((0.75f, 1f), (cmd.S3, cmd.T3));
        Assert.Equal((0.25f, 1f), (cmd.S4, cmd.T4));
    }

    [Fact]
    public void NullHandleProducesARectCommand()
    {
        DrawQueue queue = NewQueue();
        queue.DrawImage(null, 0, 0, 1, 1);
        Assert.Equal(DrawCmdKind.Rect, queue.ToSortedArray()[0].Kind);
    }

    [Fact]
    public void QuadCornersAreOffsetByTheViewportOrigin()
    {
        DrawQueue queue = NewQueue();
        queue.SetViewport(100, 200, 50, 50);
        queue.DrawImageQuad(null, 0, 0, 10, 1, 11, 12, 1, 13);

        DrawCmd cmd = queue.ToSortedArray()[0];
        Assert.Equal(DrawCmdKind.Quad, cmd.Kind);
        Assert.Equal((100f, 200f), (cmd.X1, cmd.Y1));
        Assert.Equal((110f, 201f), (cmd.X2, cmd.Y2));
        Assert.Equal((111f, 212f), (cmd.X3, cmd.Y3));
        Assert.Equal((101f, 213f), (cmd.X4, cmd.Y4));
    }
}
