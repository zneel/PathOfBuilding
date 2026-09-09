using System.Collections.Immutable;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Unit tests for the tolerance ladder and the magnitude-sorted diff reporter
/// (migration ticket 07).
/// </summary>
/// <remarks>
/// Every case here is synthetic. The reporter's job is to make a 1,800-key mismatch
/// debuggable, and the only way to know it does that is to hand it differences whose correct
/// ordering is known in advance - which the real corpus, by construction, never produces
/// (it matches itself).
/// </remarks>
public sealed class GoldenDiffReporterTests
{
    // ------------------------------------------------------------------------------------
    // Tolerance ladder
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Ladder_HoldsWholeNumbersToExactEquality()
    {
        // Life, Armour, Accuracy and the charge counts are all integral, and the most likely
        // defect in the port - Math.Round's banker's rounding where Lua rounds half up - moves
        // them by exactly one. A relative tolerance would let that through on any large pool.
        Assert.False(Match(GoldenTolerance.Ladder, 6728, 6729));
        Assert.True(Match(GoldenTolerance.Ladder, 6728, 6728));

        // ... and one part in 1e12 of a big integer is still not that integer.
        Assert.False(Match(GoldenTolerance.Ladder, 6728, 6728.000000001));
    }

    [Fact]
    public void Ladder_AllowsOneInABillionOnFractionalNumbers()
    {
        const double Expected = 566925.51596343;

        Assert.True(Match(GoldenTolerance.Ladder, Expected, Expected * (1 + 5e-10)));
        Assert.False(Match(GoldenTolerance.Ladder, Expected, Expected * (1 + 5e-9)));
    }

    [Fact]
    public void Ladder_TreatsSignedZeroAsEqualAndExactDoesNot()
    {
        Assert.True(Match(GoldenTolerance.Ladder, 0.0, -0.0));
        Assert.False(Match(GoldenTolerance.Exact, 0.0, -0.0));
    }

    [Fact]
    public void Exact_RequiresTheLastUlp()
    {
        // Fractional, because the ladder holds whole numbers to exact equality and would
        // reject a one-ulp difference on 1.0 for that reason instead of this one.
        const double Expected = 1.5;
        double oneUlpAway = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(Expected) + 1);

        Assert.True(Match(GoldenTolerance.Ladder, Expected, oneUlpAway));
        Assert.False(Match(GoldenTolerance.Exact, Expected, oneUlpAway));
        Assert.True(Match(GoldenTolerance.Exact, Expected, Expected));

        // The same one-ulp difference on a whole number is a mismatch on both rungs: a value
        // the engine produced by rounding is either that integer or it is wrong.
        double integralUlp = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1.0) + 1);
        Assert.False(Match(GoldenTolerance.Ladder, 1.0, integralUlp));
    }

    [Fact]
    public void Ladder_ComparesNonFiniteValuesToThemselvesOnly()
    {
        Assert.True(Match(GoldenTolerance.Ladder, double.NaN, double.NaN));
        Assert.True(Match(GoldenTolerance.Ladder, double.PositiveInfinity, double.PositiveInfinity));
        Assert.False(Match(GoldenTolerance.Ladder, double.PositiveInfinity, double.NegativeInfinity));
        Assert.False(Match(GoldenTolerance.Ladder, double.NaN, 0));
        Assert.False(Match(GoldenTolerance.Ladder, double.PositiveInfinity, double.MaxValue));
    }

    [Fact]
    public void Ladder_FailsAnyNonZeroValueAgainstAnExpectedZero()
    {
        // Symmetric relative error: dividing by the expected value would divide by zero, and
        // an absolute epsilon would quietly accept "0 became 1e-10" on a number whose true
        // scale nobody knows.
        Assert.False(Match(GoldenTolerance.Ladder, 0.0, 1e-30));
        Assert.True(Match(GoldenTolerance.Ladder, 0.0, 0.0));
    }

    [Fact]
    public void Ladder_ComparesBooleansAndStringsExactly()
    {
        GoldenTolerance ladder = GoldenTolerance.Ladder;

        Assert.True(ladder.Matches(GoldenValue.Boolean(true), GoldenValue.Boolean(true), out _, out _));
        Assert.False(ladder.Matches(GoldenValue.Boolean(true), GoldenValue.Boolean(false), out _, out _));
        Assert.True(ladder.Matches(GoldenValue.Text("Cleave"), GoldenValue.Text("Cleave"), out _, out _));
        Assert.False(ladder.Matches(GoldenValue.Text("Cleave"), GoldenValue.Text("cleave"), out _, out _));

        // A boolean where the corpus has a number is a different answer, not a near miss.
        Assert.False(ladder.Matches(GoldenValue.Number(1), GoldenValue.Boolean(true), out double relative, out _));
        Assert.Equal(double.PositiveInfinity, relative);
    }

    // ------------------------------------------------------------------------------------
    // Magnitude ordering
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Compare_OrdersDifferencesByDescendingRelativeError()
    {
        GoldenBuild build = SyntheticBuild(new Dictionary<string, GoldenValue>(StringComparer.Ordinal)
        {
            // key                    expected      actual        relative error
            ["TinyDrift"] = GoldenValue.Number(100.5),          // -> 100.5000001   ~1e-9  (within tolerance)
            ["SmallDrift"] = GoldenValue.Number(100.5),         // -> 100.6         ~1e-3
            ["HugeNumberSmallDrift"] = GoldenValue.Number(4_000_000.5), // -> +0.5%  5e-3
            ["Doubled"] = GoldenValue.Number(2.5),              // -> 5.0           5e-1
        });

        Dictionary<string, GoldenValue> actual = new(StringComparer.Ordinal)
        {
            ["TinyDrift"] = GoldenValue.Number(100.5 * (1 + 1e-10)),
            ["SmallDrift"] = GoldenValue.Number(100.6),
            ["HugeNumberSmallDrift"] = GoldenValue.Number(4_000_000.5 * 1.005),
            ["Doubled"] = GoldenValue.Number(5.0),
        };

        ImmutableArray<GoldenDiff> diffs = GoldenDiffReporter.Compare(build, Sections(actual), GoldenTolerance.Ladder);

        Assert.Equal(["Doubled", "HugeNumberSmallDrift", "SmallDrift"], diffs.Select(static d => d.Key));

        // The point of ranking by relative rather than absolute error: the biggest absolute
        // deviation by far is on the four-million stat, and it is not the interesting one.
        Assert.True(diffs[0].AbsoluteError < diffs[1].AbsoluteError);
    }

    [Fact]
    public void Compare_RanksStructuralDifferencesAboveEveryNumericOne()
    {
        GoldenBuild build = SyntheticBuild(new Dictionary<string, GoldenValue>(StringComparer.Ordinal)
        {
            ["Doubled"] = GoldenValue.Number(2.5),
            ["NeverImplemented"] = GoldenValue.Number(42.5),
            ["WrongType"] = GoldenValue.Number(1.5),
        });

        Dictionary<string, GoldenValue> actual = new(StringComparer.Ordinal)
        {
            ["Doubled"] = GoldenValue.Number(5.0),
            ["WrongType"] = GoldenValue.Boolean(true),
            ["Invented"] = GoldenValue.Number(7),
        };

        ImmutableArray<GoldenDiff> diffs = GoldenDiffReporter.Compare(build, Sections(actual), GoldenTolerance.Ladder);

        Assert.Equal(4, diffs.Length);
        Assert.All(diffs.Take(3), static d => Assert.NotEqual(GoldenDiffKind.ValueMismatch, d.Kind));
        Assert.Equal(GoldenDiffKind.ValueMismatch, diffs[^1].Kind);
        Assert.Contains(diffs, static d => d.Kind == GoldenDiffKind.Missing && d.Key == "NeverImplemented");
        Assert.Contains(diffs, static d => d.Kind == GoldenDiffKind.Unexpected && d.Key == "Invented");
        Assert.Contains(diffs, static d => d.Kind == GoldenDiffKind.KindMismatch && d.Key == "WrongType");
    }

    [Fact]
    public void Compare_ReportsAnEntirelyMissingSectionOnce()
    {
        GoldenBuild build = SyntheticBuild(new Dictionary<string, GoldenValue>(StringComparer.Ordinal)
        {
            ["Life"] = GoldenValue.Number(6728),
            ["Mana"] = GoldenValue.Number(1391),
        });

        ImmutableArray<GoldenDiff> diffs = GoldenDiffReporter.Compare(
            build,
            new Dictionary<string, IReadOnlyDictionary<string, GoldenValue>>(StringComparer.Ordinal),
            GoldenTolerance.Ladder);

        GoldenDiff diff = Assert.Single(diffs);
        Assert.Equal(GoldenDiffKind.SectionMissing, diff.Kind);
        Assert.Equal("MAIN/player", diff.Section);
    }

    [Fact]
    public void Sort_IsTotalSoTheReportIsReproducible()
    {
        // Two differences with identical error have to come out in a fixed order, or a report
        // taken twice from the same failure reads differently and cannot be diffed.
        GoldenDiff[] diffs =
        [
            new("MAIN/player", "Zeta", GoldenDiffKind.ValueMismatch, GoldenValue.Number(1.5), GoldenValue.Number(3.0), 0.5, 1.5),
            new("MAIN/player", "Alpha", GoldenDiffKind.ValueMismatch, GoldenValue.Number(1.5), GoldenValue.Number(3.0), 0.5, 1.5),
            new("CALCS/player", "Alpha", GoldenDiffKind.ValueMismatch, GoldenValue.Number(1.5), GoldenValue.Number(3.0), 0.5, 1.5),
        ];

        Assert.Equal(
            ["CALCS/player/Alpha", "MAIN/player/Alpha", "MAIN/player/Zeta"],
            GoldenDiffReporter.Sort(diffs).Select(static d => $"{d.Section}/{d.Key}"));
        Assert.Equal(
            GoldenDiffReporter.Sort(diffs).Select(static d => d.Key),
            GoldenDiffReporter.Sort(diffs.Reverse()).Select(static d => d.Key));
    }

    // ------------------------------------------------------------------------------------
    // Report rendering
    // ------------------------------------------------------------------------------------

    [Fact]
    public void Report_LeadsWithTheWorstDifferenceAndSaysHowManyItElided()
    {
        List<GoldenDiff> diffs = [];
        for (int i = 1; i <= 40; i++)
        {
            diffs.Add(new GoldenDiff("MAIN/player", $"Key{i:D2}", GoldenDiffKind.ValueMismatch,
                GoldenValue.Number(100), GoldenValue.Number(100 + i), i / 100.0, i));
        }

        string report = GoldenDiffReporter.Report("synthetic-build", diffs, GoldenTolerance.Ladder, limit: 5);

        Assert.Contains("40 difference(s)", report, StringComparison.Ordinal);
        Assert.Contains("MAIN/player/Key40", report, StringComparison.Ordinal);
        Assert.Contains("... and 35 more", report, StringComparison.Ordinal);
        Assert.DoesNotContain("MAIN/player/Key01", report, StringComparison.Ordinal);

        // Worst first, on the first line after the header.
        string firstDiffLine = report.Split('\n')[1];
        Assert.Contains("Key40", firstDiffLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_SaysNothingIsWrongWhenNothingIsWrong()
    {
        string report = GoldenDiffReporter.Report("synthetic-build", [], GoldenTolerance.Exact);

        Assert.Contains("matches the golden corpus", report, StringComparison.Ordinal);
        Assert.Contains("bit-exact", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_SeparatesStructuralFromNumericCounts()
    {
        GoldenDiff[] diffs =
        [
            new("MAIN/player", "Gone", GoldenDiffKind.Missing, GoldenValue.Number(1), GoldenValue.Text("<missing>"),
                double.PositiveInfinity, double.PositiveInfinity),
            new("MAIN/player", "Drifted", GoldenDiffKind.ValueMismatch, GoldenValue.Number(1.5), GoldenValue.Number(1.6), 0.0625, 0.1),
        ];

        string report = GoldenDiffReporter.Report("synthetic-build", diffs, GoldenTolerance.Ladder);

        Assert.Contains("(1 structural, 1 numeric)", report, StringComparison.Ordinal);
        Assert.Contains("Missing", report, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------

    private static bool Match(GoldenTolerance tolerance, double expected, double actual) =>
        tolerance.Matches(GoldenValue.Number(expected), GoldenValue.Number(actual), out _, out _);

    private static GoldenBuild SyntheticBuild(Dictionary<string, GoldenValue> values) =>
        new(
            Name: "synthetic-build",
            Group: "synthetic",
            InputXml: "<?xml version=\"1.0\"?><PathOfBuilding/>",
            TreeVersion: "3_29",
            KeyCount: values.Count,
            Sections: new Dictionary<string, GoldenSection>(StringComparer.Ordinal)
            {
                ["MAIN/player"] = new GoldenSection("MAIN/player", values),
            });

    private static Dictionary<string, IReadOnlyDictionary<string, GoldenValue>> Sections(
        Dictionary<string, GoldenValue> values) =>
        new(StringComparer.Ordinal) { ["MAIN/player"] = values };
}
