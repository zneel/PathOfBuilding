using Xunit;

namespace Pob.Rendering.Tests;

/// <summary>The colour-escape parsing behind the string overload of <c>SetDrawColor</c>.</summary>
public sealed class ColorEscapeTests
{
    [Theory]
    [InlineData("^0", 0f, 0f, 0f)]
    [InlineData("^1", 1f, 0f, 0f)]
    [InlineData("^7", 1f, 1f, 1f)]
    [InlineData("^8", 0.7f, 0.7f, 0.7f)]
    [InlineData("^9", 0.4f, 0.4f, 0.4f)]
    public void IndexedEscapesUseThePalette(string escape, float r, float g, float b)
    {
        DrawColor color = ColorEscape.Parse(escape);
        Assert.Equal(new DrawColor(r, g, b, 1f), color);
    }

    [Theory]
    [InlineData("^xC8C8C8", 200, 200, 200)]  // colorCodes.NORMAL
    [InlineData("^x8888FF", 136, 136, 255)]  // colorCodes.MAGIC
    [InlineData("^xFFFF77", 255, 255, 119)]  // colorCodes.RARE
    [InlineData("^x000000", 0, 0, 0)]
    [InlineData("^xffffff", 255, 255, 255)]  // lower case hex
    public void HexEscapesParseAllThreeChannels(string escape, int r, int g, int b)
    {
        DrawColor color = ColorEscape.Parse(escape);
        Assert.Equal(new DrawColor(r / 255f, g / 255f, b / 255f, 1f), color);
    }

    [Fact]
    public void EscapesAreAlwaysOpaque()
    {
        DrawQueue queue = new(100f, 100f);
        queue.SetDrawColor(1f, 1f, 1f, 0.25f);
        queue.SetDrawColor("^xFF0000");

        Assert.Equal(1f, queue.GetDrawColor().A);
    }

    [Theory]
    [InlineData("")]
    [InlineData("^")]
    [InlineData("^a")]
    [InlineData("^x12345")]
    [InlineData("^xGGGGGG")]
    [InlineData("plain text")]
    [InlineData("^7 trailing")]
    public void MalformedEscapesAreRejected(string escape)
    {
        Assert.Throws<ArgumentException>(() => ColorEscape.Parse(escape));
    }

    [Theory]
    [InlineData("^7white", 2)]
    [InlineData("^xFF0000red", 8)]
    [InlineData("no escape", 0)]
    [InlineData("^x12", 0)]
    public void EscapeLengthMeasuresOnlyTheLeadingEscape(string text, int expected)
    {
        Assert.Equal(expected, ColorEscape.EscapeLength(text));
    }

    [Fact]
    public void TryParseReportsTheConsumedLength()
    {
        Assert.True(ColorEscape.TryParse("^x33FF77 rest", out DrawColor color, out int length));
        Assert.Equal(8, length);
        Assert.Equal(new DrawColor(0x33 / 255f, 1f, 0x77 / 255f, 1f), color);
    }

    [Fact]
    public void PaletteIndexIsRangeChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorEscape.GetPaletteColor(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorEscape.GetPaletteColor(-1));
    }
}
