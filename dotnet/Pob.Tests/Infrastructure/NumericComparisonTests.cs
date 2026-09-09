using Xunit;

namespace Pob.Tests.Infrastructure;

/// <summary>
/// The tolerance ladder, pinned down.
/// </summary>
/// <remarks>
/// This type is meant to be the one comparison path for the ModCache oracle (issue 6), the
/// mod-store dump (issue 7), the golden output corpus (issue 8) and the differential fuzzer, so
/// its behaviour has to be a written-down contract rather than whatever the first caller
/// happened to need. Each test below is one rung.
/// </remarks>
public sealed class NumericComparisonTests
{
    [Fact]
    public void DifferentLuaTypes_NeverMatch()
    {
        Difference? difference = NumericComparison.Compare(
            "k",
            ScalarValue.OfBoolean(true),
            ScalarValue.OfNumber(1.0));

        Assert.NotNull(difference);
        Assert.Contains("type changed", difference.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Booleans_CompareExactly()
    {
        Assert.Null(NumericComparison.Compare("k", ScalarValue.OfBoolean(true), ScalarValue.OfBoolean(true)));
        Assert.NotNull(NumericComparison.Compare("k", ScalarValue.OfBoolean(true), ScalarValue.OfBoolean(false)));
    }

    [Fact]
    public void Strings_CompareOrdinally()
    {
        Assert.Null(NumericComparison.Compare("k", ScalarValue.OfText("Fire"), ScalarValue.OfText("Fire")));
        Assert.NotNull(NumericComparison.Compare("k", ScalarValue.OfText("Fire"), ScalarValue.OfText("fire")));
    }

    [Fact]
    public void Integers_CompareExactly_UnderTheDefaultPolicy()
    {
        // A relative 1e-9 would swallow an off-by-one at this magnitude; the integer rung is
        // what stops that.
        Assert.NotNull(NumericComparison.Compare("Life", 4_000_000_000.0, 4_000_000_001.0));
        Assert.Null(NumericComparison.Compare("Life", 4_000_000_000.0, 4_000_000_000.0));
    }

    [Fact]
    public void Integers_CanBeRelaxed()
    {
        ComparisonPolicy relaxed = ComparisonPolicy.Default with { IntegralsExact = false };

        Assert.Null(NumericComparison.Compare("Life", 4_000_000_000.0, 4_000_000_001.0, relaxed));
    }

    [Fact]
    public void NonIntegers_UseTheRelativeEpsilon()
    {
        // 1e-9 relative of 1000 is 1e-6.
        Assert.Null(NumericComparison.Compare("DPS", 1000.5, 1000.5 + 1e-7));
        Assert.NotNull(NumericComparison.Compare("DPS", 1000.5, 1000.5 + 1e-4));
    }

    [Fact]
    public void NearZero_UsesTheAbsoluteFloor()
    {
        // A purely relative test here would demand agreement to within 1e-29, which two
        // correct-but-differently-ordered summations will not give.
        Assert.Null(NumericComparison.Compare("tiny", 1e-20, 5e-21));
        Assert.NotNull(NumericComparison.Compare("tiny", 1e-20, 1e-9));
    }

    [Fact]
    public void SignedZero_MattersOnlyUnderBitExact()
    {
        Assert.Null(NumericComparison.Compare("z", 0.0, -0.0));

        Difference? difference = NumericComparison.Compare("z", 0.0, -0.0, ComparisonPolicy.Exact);
        Assert.NotNull(difference);
        Assert.Contains("bit patterns differ", difference.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BitExact_ToleratesNothing()
    {
        Assert.Null(NumericComparison.Compare("x", 1.5, 1.5, ComparisonPolicy.Exact));
        Assert.NotNull(NumericComparison.Compare("x", 1.5, Math.BitIncrement(1.5), ComparisonPolicy.Exact));
    }

    [Fact]
    public void NaN_MatchesNaN_ButNothingElse()
    {
        Assert.Null(NumericComparison.Compare("n", double.NaN, double.NaN));
        Assert.NotNull(NumericComparison.Compare("n", double.NaN, 0.0));
        Assert.NotNull(NumericComparison.Compare("n", 0.0, double.NaN));
    }

    [Fact]
    public void Infinities_MatchOnlyTheSameInfinity()
    {
        Assert.Null(NumericComparison.Compare("i", double.PositiveInfinity, double.PositiveInfinity));
        Assert.NotNull(NumericComparison.Compare("i", double.PositiveInfinity, double.NegativeInfinity));
        Assert.NotNull(NumericComparison.Compare("i", double.PositiveInfinity, double.MaxValue));
    }

    [Fact]
    public void Diff_ReportsMissingAndUnexpectedKeysSeparately()
    {
        Dictionary<string, ScalarValue> expected = new(StringComparer.Ordinal)
        {
            ["A"] = ScalarValue.OfNumber(1.0),
            ["B"] = ScalarValue.OfNumber(2.0),
            ["C"] = ScalarValue.OfNumber(3.0),
        };
        Dictionary<string, ScalarValue> actual = new(StringComparer.Ordinal)
        {
            ["A"] = ScalarValue.OfNumber(1.0),
            ["B"] = ScalarValue.OfNumber(99.0),
            ["D"] = ScalarValue.OfNumber(4.0),
        };

        CorpusDiff diff = new CorpusDiff().CompareAll(expected, actual);

        Assert.False(diff.IsClean);
        Assert.Equal(2, diff.Compared);
        Assert.Single(diff.Differences);
        Assert.Equal("B", diff.Differences[0].Key);
        Assert.Equal(["C"], diff.MissingKeys);
        Assert.Equal(["D"], diff.UnexpectedKeys);

        string report = diff.Report("sample");
        Assert.Contains("1 of 2 values disagree", report, StringComparison.Ordinal);
        Assert.Contains("1 keys missing", report, StringComparison.Ordinal);
        Assert.Contains("1 keys unexpected", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_IsCleanWhenEverythingAgrees()
    {
        Dictionary<string, ScalarValue> values = new(StringComparer.Ordinal)
        {
            ["A"] = ScalarValue.OfNumber(1.0),
            ["B"] = ScalarValue.OfBoolean(true),
            ["C"] = ScalarValue.OfText("Fire"),
        };

        CorpusDiff diff = new CorpusDiff().CompareAll(values, values);

        Assert.True(diff.IsClean);
        Assert.Equal(3, diff.Compared);
        Assert.Equal(string.Empty, diff.Report("sample"));
    }

    [Fact]
    public void Diff_CapsTheReport()
    {
        Dictionary<string, ScalarValue> expected = new(StringComparer.Ordinal);
        Dictionary<string, ScalarValue> actual = new(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++)
        {
            string key = $"K{i:D3}";
            expected[key] = ScalarValue.OfNumber(i);
            actual[key] = ScalarValue.OfNumber(i + 1);
        }

        CorpusDiff diff = new CorpusDiff(maxReported: 5).CompareAll(expected, actual);
        string report = diff.Report("sample");

        Assert.Equal(200, diff.Differences.Count);
        Assert.Contains("and 195 more", report, StringComparison.Ordinal);
    }
}
