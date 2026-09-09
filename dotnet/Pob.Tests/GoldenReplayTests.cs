using System.Collections.Immutable;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Exercises the replay path end to end against the real corpus (migration ticket 07).
/// </summary>
/// <remarks>
/// <para>
/// The engine these tests are ultimately for does not exist: <c>Pob.Calc</c> is tickets
/// 21-27. What does exist is everything around it - reading a golden file, matching sections,
/// applying the tolerance ladder, ranking the differences - and that is what runs here,
/// driven by <see cref="GoldenReplay.PlaybackEngine"/> over real corpus builds rather than
/// hand-written fixtures.
/// </para>
/// <para>
/// This is not a tautology dressed up as a test. Playing the corpus back unchanged proves the
/// comparison path is clean on ~1,800 real keys per build, including the ones that are
/// booleans, strings, infinities and negative zeroes - the values most likely to make a naive
/// comparator report a spurious difference. Playing it back with a known perturbation proves
/// the same path actually detects and ranks a deviation. When the real engine arrives, a
/// failure will be the engine's.
/// </para>
/// </remarks>
public sealed class GoldenReplayTests
{
    /// <summary>A handful of real builds, spread across the corpus's groups.</summary>
    public static TheoryData<string> SampleBuilds
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string name in GoldenCorpus.Names
                .GroupBy(static n => n.Split('-')[0], StringComparer.Ordinal)
                .SelectMany(static g => g.Take(2))
                .OrderBy(static n => n, StringComparer.Ordinal))
            {
                data.Add(name);
            }

            return data;
        }
    }

    /// <summary>
    /// The corpus compares clean against itself at the strictest rung. If this fails, the
    /// comparison path has a bug of its own - a value kind it mishandles, a NaN it treats as
    /// unequal to itself - and no engine result could be trusted through it.
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleBuilds))]
    public void Replay_OfTheCorpusAgainstItself_IsClean(string name)
    {
        GoldenBuild build = GoldenCorpus.Load(name);
        GoldenReplay.PlaybackEngine engine = new(build);

        ImmutableArray<GoldenDiff> diffs = GoldenReplay.Replay(build, engine, GoldenTolerance.Exact);

        Assert.True(diffs.IsEmpty, GoldenDiffReporter.Report(build.Name, diffs, GoldenTolerance.Exact));
    }

    /// <summary>
    /// One key moved by 5% in a real build, and the report has to open on it - not on the
    /// 1,799 keys that are fine, and not on whichever key happens to sort first alphabetically.
    /// </summary>
    [Fact]
    public void Replay_WithOneInjectedDeviation_NamesThatKeyFirst()
    {
        GoldenBuild build = FirstBuildWith("MAIN/player", "Life");

        GoldenReplay.PlaybackEngine engine = new(build, (section, key, value) =>
            section == "MAIN/player" && key == "Life"
                ? GoldenValue.Number(value.NumberValue * 1.05)
                : value);

        ImmutableArray<GoldenDiff> diffs = GoldenReplay.Replay(build, engine, GoldenTolerance.Ladder);

        GoldenDiff diff = Assert.Single(diffs);
        Assert.Equal("MAIN/player", diff.Section);
        Assert.Equal("Life", diff.Key);

        string report = GoldenDiffReporter.Report(build.Name, diffs, GoldenTolerance.Ladder);
        Assert.Contains("MAIN/player/Life", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// A systematic rounding error - every fractional value off by one part in a million -
    /// is what a mis-ported <c>round</c> helper actually looks like. The ladder has to catch
    /// it on hundreds of keys at once, and the worst offender has to come first.
    /// </summary>
    [Fact]
    public void Replay_WithASystematicRoundingDrift_ReportsItWorstFirst()
    {
        GoldenBuild build = FirstBuildWith("MAIN/player", "Life");

        GoldenReplay.PlaybackEngine engine = new(build, static (_, _, value) =>
            value.Kind == GoldenValueKind.Number && double.IsFinite(value.NumberValue) && !value.IsIntegral
                ? GoldenValue.Number(value.NumberValue * (1 + 1e-6))
                : value);

        ImmutableArray<GoldenDiff> diffs = GoldenReplay.Replay(build, engine, GoldenTolerance.Ladder);

        Assert.True(diffs.Length > 50,
            $"only {diffs.Length} keys moved; the perturbation should have reached most fractional values");
        Assert.All(diffs, static d => Assert.Equal(GoldenDiffKind.ValueMismatch, d.Kind));

        // Sorted, not merely collected.
        for (int i = 1; i < diffs.Length; i++)
        {
            Assert.True(diffs[i - 1].RelativeError >= diffs[i].RelativeError,
                $"diff {i} is ranked above a larger deviation");
        }

        // ... and integral values, held to exact equality, were left alone by construction and
        // therefore must not appear.
        Assert.DoesNotContain(diffs, static d => d.Expected.IsIntegral);
    }

    /// <summary>
    /// The same drift at one part in ten billion sits under the ladder's <c>1e-9</c> rung and
    /// over the exact rung. Both answers are correct; the difference between them is the whole
    /// reason the ladder exists.
    /// </summary>
    [Fact]
    public void Replay_WithADriftBelowTheLadder_PassesTheLadderAndFailsExact()
    {
        GoldenBuild build = FirstBuildWith("MAIN/player", "Life");

        GoldenReplay.PlaybackEngine engine = new(build, static (_, _, value) =>
            value.Kind == GoldenValueKind.Number && double.IsFinite(value.NumberValue) && !value.IsIntegral
                ? GoldenValue.Number(value.NumberValue * (1 + 1e-10))
                : value);

        Assert.Empty(GoldenReplay.Replay(build, engine, GoldenTolerance.Ladder));
        Assert.NotEmpty(GoldenReplay.Replay(build, engine, GoldenTolerance.Exact));
    }

    /// <summary>
    /// An engine that has not implemented a whole actor yet reports as a structural failure,
    /// not as several hundred numeric ones. This is the state the port will be in for most of
    /// tickets 21-27, so the harness has to say something useful about it.
    /// </summary>
    [Fact]
    public void Replay_WithAnEngineThatProducesNothing_ReportsMissingSections()
    {
        GoldenBuild build = FirstBuildWith("MAIN/player", "Life");
        EmptyEngine engine = new();

        ImmutableArray<GoldenDiff> diffs = GoldenReplay.Replay(build, engine, GoldenTolerance.Ladder);

        Assert.Equal(build.Sections.Count, diffs.Length);
        Assert.All(diffs, static d => Assert.Equal(GoldenDiffKind.SectionMissing, d.Kind));

        string report = GoldenDiffReporter.Report(build.Name, diffs, GoldenTolerance.Ladder);
        Assert.Contains("structural", report, StringComparison.Ordinal);
    }

    private static GoldenBuild FirstBuildWith(string section, string key)
    {
        foreach (string name in GoldenCorpus.Names)
        {
            GoldenBuild build = GoldenCorpus.Load(name);
            if (build.Sections.TryGetValue(section, out GoldenSection? found) && found.Values.ContainsKey(key))
            {
                return build;
            }
        }

        throw new InvalidOperationException($"no golden build has {section}/{key}");
    }

    private sealed class EmptyEngine : IGoldenEngine
    {
        public string Description => "engine with nothing implemented";

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, GoldenValue>> Calculate(string inputXml) =>
            new Dictionary<string, IReadOnlyDictionary<string, GoldenValue>>(StringComparer.Ordinal);
    }
}
