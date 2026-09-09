using System.Globalization;

namespace Pob.Tests.Infrastructure;

/// <summary>
/// The wire spelling every Lua-produced corpus in this repository uses for a number.
/// </summary>
/// <remarks>
/// <para>
/// The dump scripts write numbers as JSON <em>strings</em>, never as JSON numbers, so that no
/// reader anywhere in the chain gets a chance to re-round a double it was handed. The spelling
/// is: integers bare; <c>inf</c>, <c>-inf</c> and <c>nan</c> for the non-finite values;
/// <c>-0</c> for negative zero; otherwise the shortest <c>%.Ng</c> form that survives a Lua
/// <c>tonumber</c> round-trip.
/// </para>
/// <para>
/// Negative zero has its own spelling because <c>-0 == 0</c> is true in both languages while
/// the sign bit still propagates through multiplication, so a corpus that lost it would be
/// silently weaker exactly where <c>LuaCompat.Modf</c> and friends are most delicate. Parsing
/// therefore goes through <see cref="double.Parse(string, NumberStyles, IFormatProvider)"/>,
/// which does preserve the sign of zero, and never through a path that normalises it away.
/// </para>
/// <para>
/// Producers: <c>dotnet/tools/luacompat-oracle/dump.lua</c> and
/// <c>dotnet/tools/fuzz/json.lua</c>. Any future dump script should use the same spelling and
/// read back through this type.
/// </para>
/// </remarks>
public static class LuaNumber
{
    /// <summary>Parse one corpus number spelling.</summary>
    /// <param name="text">The spelling, e.g. <c>"1.5"</c>, <c>"-0"</c>, <c>"inf"</c>, <c>"nan"</c>.</param>
    /// <returns>The value it denotes.</returns>
    /// <exception cref="FormatException">The text is not a spelling this convention produces.</exception>
    public static double Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        switch (text)
        {
            case "inf":
                return double.PositiveInfinity;
            case "-inf":
                return double.NegativeInfinity;
            case "nan":
                return double.NaN;
            case "-0":
                return -0.0;
            default:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    ? value
                    : throw new FormatException($"'{text}' is not a Lua corpus number spelling");
        }
    }

    /// <summary>
    /// Spell a value the way the dump scripts do. The inverse of <see cref="Parse"/> for every
    /// value those scripts emit, which makes it the right formatter for a failure message: a
    /// diff that prints <c>0</c> on both sides while the bits differ is worse than no message.
    /// </summary>
    /// <param name="value">The value to spell.</param>
    /// <returns>Its corpus spelling.</returns>
    public static string Format(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        if (value == 0.0)
        {
            return double.IsNegative(value) ? "-0" : "0";
        }

        if (IsIntegral(value))
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        // "R" is the shortest round-tripping form in .NET Core 3.0 and later, which is the same
        // guarantee the Lua side's precision search gives. The two do not always pick the same
        // spelling for the same double, so this is for display only -- comparison is on the
        // parsed value, never on the text.
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Is this value a whole number that a double represents exactly? Above 2^53 consecutive
    /// integers are no longer distinguishable, so "integral" stops being a meaningful claim
    /// about the value the engine intended.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when it is an exactly-representable whole number.</returns>
    public static bool IsIntegral(double value) =>
        double.IsFinite(value)
        && Math.Abs(value) <= 9007199254740992.0
        && value == Math.Truncate(value);

    /// <summary>Render a value with its bit pattern, for a failure message.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>Spelling plus the raw IEEE-754 bits.</returns>
    public static string Describe(double value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Format(value)} (0x{BitConverter.DoubleToInt64Bits(value):X16})");
}
