using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Pob.Tests;

/// <summary>
/// Compares a calculated result against a golden build and reports the differences ordered by
/// magnitude (migration ticket 07).
/// </summary>
/// <remarks>
/// <para>
/// The ordering is the entire point. A build carries roughly 1,800 keys; when a port change
/// breaks 400 of them, an alphabetical list opens on <c>AccuracyHitChance</c> and buries the
/// one number that actually moved. Sorted by magnitude, the first line is the largest
/// deviation, which in practice is the value closest to the broken formula - everything below
/// it is usually downstream of it.
/// </para>
/// <para>
/// "Magnitude" is <em>relative</em> error, not absolute. Absolute error ranks by how big the
/// stat happens to be: a 0.01% drift in a 4,000,000 DPS number would outrank a doubled crit
/// multiplier forever. Relative error is scale-free, so a systematic rounding difference lands
/// every affected key at the same rank (a strong signal in itself: a whole screen of identical
/// relative errors says "one rounding helper", not "four hundred bugs"), and a genuinely wrong
/// value rises above them all. Absolute error breaks ties and is printed alongside, because
/// once the relative errors are equal the biggest number is the one worth looking at first.
/// </para>
/// <para>
/// Structural differences - a key the port never produced, or produced with the wrong type -
/// carry infinite error and therefore sort above every numeric drift. They are not "a large
/// difference", they are a missing implementation, and they should be read first.
/// </para>
/// </remarks>
public static class GoldenDiffReporter
{
    /// <summary>
    /// Compares every key of every section of <paramref name="expected"/> against
    /// <paramref name="actual"/>, returning the differences sorted by descending magnitude.
    /// </summary>
    /// <param name="expected">The golden build.</param>
    /// <param name="actual">
    /// Sections the engine under test produced, keyed the same way (<c>"MAIN/player"</c>).
    /// </param>
    /// <param name="tolerance">Which rung of the ladder to hold the comparison to.</param>
    public static ImmutableArray<GoldenDiff> Compare(
        GoldenBuild expected,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, GoldenValue>> actual,
        GoldenTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(tolerance);

        List<GoldenDiff> diffs = [];

        foreach ((string sectionName, GoldenSection section) in expected.Sections.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!actual.TryGetValue(sectionName, out IReadOnlyDictionary<string, GoldenValue>? actualValues))
            {
                diffs.Add(new GoldenDiff(sectionName, "*", GoldenDiffKind.SectionMissing,
                    GoldenValue.Text($"{section.Values.Count} keys"), GoldenValue.Text("<no section>"),
                    double.PositiveInfinity, double.PositiveInfinity));
                continue;
            }

            foreach ((string key, GoldenValue expectedValue) in section.Values)
            {
                if (!actualValues.TryGetValue(key, out GoldenValue actualValue))
                {
                    diffs.Add(new GoldenDiff(sectionName, key, GoldenDiffKind.Missing,
                        expectedValue, GoldenValue.Text("<missing>"),
                        double.PositiveInfinity, double.PositiveInfinity));
                    continue;
                }

                if (!tolerance.Matches(expectedValue, actualValue, out double relative, out double absolute))
                {
                    GoldenDiffKind kind = expectedValue.Kind == actualValue.Kind
                        ? GoldenDiffKind.ValueMismatch
                        : GoldenDiffKind.KindMismatch;
                    diffs.Add(new GoldenDiff(sectionName, key, kind, expectedValue, actualValue, relative, absolute));
                }
            }

            foreach (string key in actualValues.Keys)
            {
                if (!section.Values.ContainsKey(key))
                {
                    diffs.Add(new GoldenDiff(sectionName, key, GoldenDiffKind.Unexpected,
                        GoldenValue.Text("<absent>"), actualValues[key],
                        double.PositiveInfinity, double.PositiveInfinity));
                }
            }
        }

        return [.. Sort(diffs)];
    }

    /// <summary>
    /// Orders differences worst-first: relative error descending, then absolute error
    /// descending, then section and key so the order is total and the report is reproducible.
    /// </summary>
    public static IEnumerable<GoldenDiff> Sort(IEnumerable<GoldenDiff> diffs)
    {
        ArgumentNullException.ThrowIfNull(diffs);
        return diffs
            .OrderByDescending(static d => d.RelativeError)
            .ThenByDescending(static d => d.AbsoluteError)
            .ThenBy(static d => d.Section, StringComparer.Ordinal)
            .ThenBy(static d => d.Key, StringComparer.Ordinal);
    }

    /// <summary>
    /// Renders a human-readable report. The header carries the counts, then at most
    /// <paramref name="limit"/> lines worst-first, then a note about what was elided.
    /// </summary>
    public static string Report(
        string buildName,
        IReadOnlyCollection<GoldenDiff> diffs,
        GoldenTolerance tolerance,
        int limit = 25)
    {
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(tolerance);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        if (diffs.Count == 0)
        {
            return $"{buildName}: matches the golden corpus ({tolerance.Description}).";
        }

        StringBuilder report = new();
        report.Append(CultureInfo.InvariantCulture, $"{buildName}: {diffs.Count} difference(s) at {tolerance.Description}");

        int structural = diffs.Count(static d => d.Kind is GoldenDiffKind.Missing or GoldenDiffKind.Unexpected
            or GoldenDiffKind.KindMismatch or GoldenDiffKind.SectionMissing);
        if (structural > 0)
        {
            report.Append(CultureInfo.InvariantCulture, $" ({structural} structural, {diffs.Count - structural} numeric)");
        }

        report.AppendLine(". Worst first:");

        int shown = 0;
        foreach (GoldenDiff diff in Sort(diffs))
        {
            if (shown == limit)
            {
                break;
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"  {diff.Describe()}");
            shown++;
        }

        if (shown < diffs.Count)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  ... and {diffs.Count - shown} more");
        }

        return report.ToString();
    }
}

/// <summary>One key that did not match, and by how much.</summary>
public sealed record GoldenDiff(
    string Section,
    string Key,
    GoldenDiffKind Kind,
    GoldenValue Expected,
    GoldenValue Actual,
    double RelativeError,
    double AbsoluteError)
{
    /// <summary>One line of the report.</summary>
    public string Describe()
    {
        string magnitude = double.IsFinite(RelativeError)
            ? string.Create(CultureInfo.InvariantCulture, $"rel {RelativeError:E3} abs {AbsoluteError:E3}")
            : Kind.ToString();

        return string.Create(CultureInfo.InvariantCulture,
            $"{magnitude,-26} {Section}/{Key}: expected {Expected}, got {Actual}");
    }
}

/// <summary>Why a key is in the report.</summary>
public enum GoldenDiffKind
{
    /// <summary>Both sides produced the same kind of value; the numbers disagree.</summary>
    ValueMismatch,

    /// <summary>The golden corpus has this key and the engine under test did not produce it.</summary>
    Missing,

    /// <summary>The engine under test produced a key the golden corpus does not have.</summary>
    Unexpected,

    /// <summary>A number where the corpus has a boolean, or similar.</summary>
    KindMismatch,

    /// <summary>A whole actor/mode section is absent.</summary>
    SectionMissing,
}
