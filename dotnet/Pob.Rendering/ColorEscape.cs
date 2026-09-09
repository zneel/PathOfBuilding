using System.Globalization;

namespace Pob.Rendering;

/// <summary>
/// Parsing for the SimpleGraphic colour-escape codes that the string overload of
/// <c>SetDrawColor</c> accepts: <c>^0</c>..<c>^9</c> for the ten palette entries and
/// <c>^xRRGGBB</c> for a literal 24-bit colour (see <c>src/Data/Global.lua</c>, whose whole
/// <c>colorCodes</c> table is written in the <c>^xRRGGBB</c> form).
/// </summary>
/// <remarks>
/// Only the parsing needed by <c>SetDrawColor(escapeStr)</c> lives here. Splitting a run of text
/// into coloured spans is part of the text pipeline (migration ticket 28) and is deliberately
/// not implemented in this type.
/// </remarks>
public static class ColorEscape
{
    /// <summary>The escape character that introduces a colour code.</summary>
    public const char EscapeChar = '^';

    /// <summary>
    /// The ten indexed palette entries selected by <c>^0</c> through <c>^9</c>.
    /// </summary>
    private static readonly DrawColor[] Palette =
    [
        new(0.00f, 0.00f, 0.00f), // ^0 black
        new(1.00f, 0.00f, 0.00f), // ^1 red
        new(0.00f, 1.00f, 0.00f), // ^2 green
        new(0.00f, 0.00f, 1.00f), // ^3 blue
        new(1.00f, 1.00f, 0.00f), // ^4 yellow
        new(1.00f, 0.00f, 1.00f), // ^5 magenta
        new(0.00f, 1.00f, 1.00f), // ^6 cyan
        new(1.00f, 1.00f, 1.00f), // ^7 white
        new(0.70f, 0.70f, 0.70f), // ^8 grey
        new(0.40f, 0.40f, 0.40f), // ^9 dark grey
    ];

    /// <summary>Gets the palette colour for an indexed escape <c>^0</c>..<c>^9</c>.</summary>
    /// <param name="index">Palette index, 0..9.</param>
    public static DrawColor GetPaletteColor(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Palette.Length);
        return Palette[index];
    }

    /// <summary>
    /// Returns the length in characters of the colour escape sequence at the start of
    /// <paramref name="text"/>, or 0 when the text does not begin with one.
    /// </summary>
    public static int EscapeLength(ReadOnlySpan<char> text)
    {
        if (text.Length < 2 || text[0] != EscapeChar)
        {
            return 0;
        }

        if (char.IsAsciiDigit(text[1]))
        {
            return 2;
        }

        if ((text[1] == 'x' || text[1] == 'X') && text.Length >= 8 && IsHex(text.Slice(2, 6)))
        {
            return 8;
        }

        return 0;
    }

    /// <summary>
    /// Attempts to parse the colour escape at the start of <paramref name="text"/>. The escape
    /// always produces an opaque colour: SimpleGraphic's string overload of <c>SetDrawColor</c>
    /// sets alpha to 1 regardless of the previous tint.
    /// </summary>
    /// <param name="text">Text that may begin with an escape sequence.</param>
    /// <param name="color">The parsed colour when the method returns <see langword="true"/>.</param>
    /// <param name="length">Number of characters consumed when the method returns <see langword="true"/>.</param>
    public static bool TryParse(ReadOnlySpan<char> text, out DrawColor color, out int length)
    {
        length = EscapeLength(text);
        switch (length)
        {
            case 2:
                color = Palette[text[1] - '0'];
                return true;
            case 8:
                color = FromHex(text.Slice(2, 6));
                return true;
            default:
                color = default;
                length = 0;
                return false;
        }
    }

    /// <summary>
    /// Parses a string that consists of exactly one colour escape, as passed to
    /// <c>SetDrawColor(escapeStr)</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The string is not a single well-formed colour escape. SimpleGraphic raises a Lua error in
    /// the same situation.
    /// </exception>
    public static DrawColor Parse(string escape)
    {
        ArgumentNullException.ThrowIfNull(escape);
        if (!TryParse(escape, out DrawColor color, out int length) || length != escape.Length)
        {
            throw new ArgumentException(
                $"'{escape}' is not a colour escape; expected ^0-^9 or ^xRRGGBB.",
                nameof(escape));
        }

        return color;
    }

    private static DrawColor FromHex(ReadOnlySpan<char> hex)
    {
        int r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new DrawColor(r / 255f, g / 255f, b / 255f);
    }

    private static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
