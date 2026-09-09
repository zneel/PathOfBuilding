using System.Globalization;

namespace Pob.Tests.Infrastructure;

/// <summary>One disagreement between an expected and an actual corpus value.</summary>
/// <param name="Key">What disagreed: an output key, a mod name, a function call.</param>
/// <param name="Expected">The reference value, normally from Lua.</param>
/// <param name="Actual">The value the port produced.</param>
/// <param name="Reason">Which rung of the ladder rejected the pair.</param>
public sealed record Difference(string Key, ScalarValue Expected, ScalarValue Actual, string Reason)
{
    /// <summary>Format for a test failure message.</summary>
    /// <returns>One line naming the key, both values and the reason.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Key}: expected {Expected}, got {Actual} ({Reason})");
}

/// <summary>
/// The single comparison path shared by every conformance corpus in this project.
/// </summary>
/// <remarks>
/// <para>
/// Tickets 05, 06, 07 and 08 each produce a corpus of (key, value) pairs dumped from the Lua
/// engine, and each will eventually be replayed against the C# port. If each grew its own
/// notion of "close enough", the migration's headline number -- "we match the reference engine
/// on N keys" -- would mean three different things depending on which suite reported it. This
/// type is that notion, once.
/// </para>
/// <para>
/// The ladder, in order:
/// </para>
/// <list type="number">
/// <item><description>
/// Different Lua types never match. A corpus <c>false</c> against a port <c>0</c> is a bug in
/// the port, not a rounding question.
/// </description></item>
/// <item><description>
/// Booleans and strings compare exactly. Strings compare ordinally: engine output keys and mod
/// names are identifiers, and a culture-aware comparison would make <c>"i"</c> and <c>"I"</c>
/// equal under a Turkish locale.
/// </description></item>
/// <item><description>
/// Identical bit patterns match, always and first. This is what preserves the sign of zero and
/// makes NaN-to-NaN agreement meaningful.
/// </description></item>
/// <item><description>
/// Under <see cref="ComparisonPolicy.BitExact"/>, nothing else matches.
/// </description></item>
/// <item><description>
/// NaN matches NaN (bit patterns may differ); an infinity matches only the same infinity.
/// </description></item>
/// <item><description>
/// Under <see cref="ComparisonPolicy.IntegralsExact"/>, two whole numbers must be equal.
/// </description></item>
/// <item><description>
/// Otherwise the difference must be within
/// <c>max(RelativeEpsilon * max(|expected|, |actual|), AbsoluteFloor)</c>.
/// </description></item>
/// </list>
/// </remarks>
public static class NumericComparison
{
    /// <summary>
    /// Compare one pair.
    /// </summary>
    /// <param name="key">The name this pair is filed under, used in the failure message.</param>
    /// <param name="expected">The reference value.</param>
    /// <param name="actual">The value under test.</param>
    /// <param name="policy">How strict to be; <see cref="ComparisonPolicy.Default"/> when null.</param>
    /// <returns>A <see cref="Difference"/> when they disagree, or <see langword="null"/>.</returns>
    public static Difference? Compare(string key, ScalarValue expected, ScalarValue actual, ComparisonPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        policy ??= ComparisonPolicy.Default;

        if (expected.Kind != actual.Kind)
        {
            return new Difference(key, expected, actual, $"type changed from {expected.Kind} to {actual.Kind}");
        }

        switch (expected.Kind)
        {
            case ScalarKind.Boolean:
                return expected.Boolean == actual.Boolean
                    ? null
                    : new Difference(key, expected, actual, "boolean differs");

            case ScalarKind.Text:
                return string.Equals(expected.Text, actual.Text, StringComparison.Ordinal)
                    ? null
                    : new Difference(key, expected, actual, "string differs");

            case ScalarKind.Number:
                return CompareNumbers(key, expected, actual, policy);

            default:
                return new Difference(key, expected, actual, $"unsupported kind {expected.Kind}");
        }
    }

    /// <summary>
    /// Convenience overload for two doubles.
    /// </summary>
    /// <param name="key">The name this pair is filed under.</param>
    /// <param name="expected">The reference value.</param>
    /// <param name="actual">The value under test.</param>
    /// <param name="policy">How strict to be; <see cref="ComparisonPolicy.Default"/> when null.</param>
    /// <returns>A <see cref="Difference"/> when they disagree, or <see langword="null"/>.</returns>
    public static Difference? Compare(string key, double expected, double actual, ComparisonPolicy? policy = null) =>
        Compare(key, ScalarValue.OfNumber(expected), ScalarValue.OfNumber(actual), policy);

    private static Difference? CompareNumbers(string key, ScalarValue expected, ScalarValue actual, ComparisonPolicy policy)
    {
        double e = expected.Number;
        double a = actual.Number;

        // First and unconditionally: identical bits are identical values. `==` cannot be used
        // for this -- it says -0.0 equals 0.0 and that NaN equals nothing, both of which are
        // exactly the cases a conformance corpus exists to pin down.
        if (BitConverter.DoubleToInt64Bits(e) == BitConverter.DoubleToInt64Bits(a))
        {
            return null;
        }

        if (policy.BitExact)
        {
            return new Difference(key, expected, actual, "bit patterns differ");
        }

        if (double.IsNaN(e) || double.IsNaN(a))
        {
            // Two NaNs with different payloads still both mean "not a number"; anything else
            // paired with a NaN is a real disagreement.
            return double.IsNaN(e) && double.IsNaN(a)
                ? null
                : new Difference(key, expected, actual, "one side is NaN");
        }

        if (double.IsInfinity(e) || double.IsInfinity(a))
        {
            // Bit equality above already accepted matching infinities, so reaching here means
            // they differ in sign or only one side is infinite. No epsilon can bridge that.
            return new Difference(key, expected, actual, "infinity mismatch");
        }

        if (policy.IntegralsExact && LuaNumber.IsIntegral(e) && LuaNumber.IsIntegral(a))
        {
            // No epsilon between two whole numbers: a count, charge, level or resistance is
            // right or it is off by one. `==` rather than bit equality here on purpose -- it
            // lets -0.0 and 0.0 pass, which is correct outside bit-exact mode, where the sign
            // of zero is not part of the claim being checked.
            return e == a
                ? null
                : new Difference(key, expected, actual, "integers differ");
        }

        double difference = Math.Abs(e - a);
        double allowed = Math.Max(policy.RelativeEpsilon * Math.Max(Math.Abs(e), Math.Abs(a)), policy.AbsoluteFloor);

        if (difference <= allowed)
        {
            return null;
        }

        return new Difference(
            key,
            expected,
            actual,
            string.Create(CultureInfo.InvariantCulture, $"differs by {difference:G6}, allowed {allowed:G6} under {policy.Describe()}"));
    }
}
