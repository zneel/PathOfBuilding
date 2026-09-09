namespace Pob.Tests;

/// <summary>
/// The tolerance ladder for comparing a ported calculation against the golden corpus
/// (migration ticket 07).
/// </summary>
/// <remarks>
/// <para>
/// Two rungs, and the ladder exists so the port can climb from the lower to the higher
/// without the corpus being regenerated:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <see cref="Ladder"/> - integers, booleans and strings must match exactly; other numbers
/// must agree to <c>1e-9</c> relative. This is the rung to stand on while
/// <c>Pob.Core.LuaCompat</c> is still being trusted: it catches every wrong formula while
/// tolerating a last-ulp difference in the order additions happen.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="Exact"/> - every value must match bit for bit, compared through
/// <see cref="BitConverter.DoubleToInt64Bits(double)"/> so that the sign of zero and the last
/// ulp are part of the contract, exactly as <c>LuaCompatOracleTests</c> already requires of
/// the arithmetic primitives.
/// </description>
/// </item>
/// </list>
/// <para>
/// Holding whole numbers to exact equality even on the lower rung is deliberate. Life,
/// Armour, Accuracy, resistances, charge counts and the rest are produced by the Lua engine's
/// <c>round</c>/<c>floor</c> helpers, so a port that reproduces those helpers correctly gets
/// them exactly right and a port that reaches for <see cref="Math.Round(double)"/> - banker's
/// rounding - gets them wrong by exactly one. A relative tolerance would hide that, and it is
/// the single most likely defect in the port.
/// </para>
/// <para>
/// Relative error is measured symmetrically, as
/// <c>|actual - expected| / max(|expected|, |actual|)</c>. The symmetry matters for the case
/// the ticket cares about most: an expected value of zero against any non-zero actual scores
/// an error of 1 and fails, rather than dividing by zero or silently passing.
/// </para>
/// </remarks>
public sealed class GoldenTolerance
{
    private GoldenTolerance(bool bitExact, double relative)
    {
        BitExact = bitExact;
        RelativeTolerance = relative;
    }

    /// <summary>
    /// Integers, booleans and strings exact; other numbers to <c>1e-9</c> relative.
    /// </summary>
    public static GoldenTolerance Ladder { get; } = new(bitExact: false, relative: 1e-9);

    /// <summary>
    /// Every value bit for bit. Switch to this once <c>LuaCompat</c> is trusted end to end;
    /// the corpus stores full <c>%.17g</c> precision precisely so this rung is reachable
    /// without regenerating anything.
    /// </summary>
    public static GoldenTolerance Exact { get; } = new(bitExact: true, relative: 0);

    /// <summary>A ladder with a custom relative tolerance, for bisecting a drift.</summary>
    public static GoldenTolerance Relative(double tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        return new GoldenTolerance(bitExact: false, relative: tolerance);
    }

    public bool BitExact { get; }

    public double RelativeTolerance { get; }

    public string Description => BitExact
        ? "bit-exact"
        : $"integers/booleans/strings exact, other numbers to {RelativeTolerance:G3} relative";

    /// <summary>
    /// Compares one value. Returns <see langword="true"/> when it is within tolerance, and
    /// always reports the errors so a caller can rank the failures it collects.
    /// </summary>
    public bool Matches(GoldenValue expected, GoldenValue actual, out double relativeError, out double absoluteError)
    {
        if (expected.Kind != actual.Kind)
        {
            // A number where the engine produced a boolean is not "off by a lot", it is a
            // different answer; infinite error sorts it above every numeric drift.
            relativeError = double.PositiveInfinity;
            absoluteError = double.PositiveInfinity;
            return false;
        }

        switch (expected.Kind)
        {
            case GoldenValueKind.Boolean:
                relativeError = expected.BooleanValue == actual.BooleanValue ? 0 : double.PositiveInfinity;
                absoluteError = relativeError;
                return expected.BooleanValue == actual.BooleanValue;

            case GoldenValueKind.Text:
                bool same = string.Equals(expected.StringValue, actual.StringValue, StringComparison.Ordinal);
                relativeError = same ? 0 : double.PositiveInfinity;
                absoluteError = relativeError;
                return same;

            default:
                return MatchesNumber(expected.NumberValue, actual.NumberValue, expected.IsIntegral,
                    out relativeError, out absoluteError);
        }
    }

    private bool MatchesNumber(double expected, double actual, bool expectedIsIntegral,
        out double relativeError, out double absoluteError)
    {
        absoluteError = ComputeAbsoluteError(expected, actual);
        relativeError = ComputeRelativeError(expected, actual, absoluteError);

        if (BitExact)
        {
            return BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual);
        }

        // NaN and the infinities have no neighbourhood; they match themselves and nothing else.
        if (!double.IsFinite(expected) || !double.IsFinite(actual))
        {
            return double.IsNaN(expected)
                ? double.IsNaN(actual)
                : expected.Equals(actual);
        }

        if (expectedIsIntegral)
        {
            // `==` rather than the bit pattern: -0.0 and 0.0 are the same integer, and the
            // engine produces both spellings of zero depending on which helper last touched
            // the value. Distinguishing them belongs to the Exact rung.
            return expected == actual;
        }

        return relativeError <= RelativeTolerance;
    }

    private static double ComputeAbsoluteError(double expected, double actual)
    {
        if (double.IsNaN(expected) || double.IsNaN(actual))
        {
            return double.IsNaN(expected) && double.IsNaN(actual) ? 0 : double.PositiveInfinity;
        }

        if (expected.Equals(actual))
        {
            return 0;
        }

        double difference = Math.Abs(expected - actual);
        return double.IsNaN(difference) ? double.PositiveInfinity : difference;
    }

    private static double ComputeRelativeError(double expected, double actual, double absoluteError)
    {
        if (absoluteError == 0)
        {
            return 0;
        }

        double scale = Math.Max(Math.Abs(expected), Math.Abs(actual));
        if (!double.IsFinite(scale) || scale == 0)
        {
            return double.PositiveInfinity;
        }

        return absoluteError / scale;
    }
}
