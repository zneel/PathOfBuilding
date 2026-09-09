using System.Globalization;
using System.Text;

namespace Pob.Tests.Infrastructure;

/// <summary>
/// Compares two keyed sets of corpus values and reports every disagreement at once.
/// </summary>
/// <remarks>
/// <para>
/// An engine port fails a whole family of keys at a time -- get one conversion wrong and two
/// hundred damage outputs move together. A comparison that threw on the first mismatch would
/// hand back one arbitrary member of that family and hide the shape of the problem, so this
/// accumulates and then reports, capped so a wholesale failure does not produce a megabyte of
/// test output.
/// </para>
/// <para>
/// Keys present on one side only are reported too, and separately: a missing output key means
/// the port never computed something, which is a different bug from computing it wrongly.
/// </para>
/// </remarks>
public sealed class CorpusDiff
{
    private readonly List<Difference> _differences = [];
    private readonly List<string> _missing = [];
    private readonly List<string> _unexpected = [];

    /// <summary>Create a diff.</summary>
    /// <param name="policy">Comparison strictness; <see cref="ComparisonPolicy.Default"/> when null.</param>
    /// <param name="maxReported">How many differences to spell out in the failure message.</param>
    public CorpusDiff(ComparisonPolicy? policy = null, int maxReported = 25)
    {
        Policy = policy ?? ComparisonPolicy.Default;
        MaxReported = maxReported;
    }

    /// <summary>The strictness this diff applies.</summary>
    public ComparisonPolicy Policy { get; }

    /// <summary>How many differences the report spells out before truncating.</summary>
    public int MaxReported { get; }

    /// <summary>Keys compared, whether they matched or not.</summary>
    public int Compared { get; private set; }

    /// <summary>Values that disagreed.</summary>
    public IReadOnlyList<Difference> Differences => _differences;

    /// <summary>Keys the reference had and the port did not produce.</summary>
    public IReadOnlyList<string> MissingKeys => _missing;

    /// <summary>Keys the port produced and the reference does not have.</summary>
    public IReadOnlyList<string> UnexpectedKeys => _unexpected;

    /// <summary>Did everything agree?</summary>
    public bool IsClean => _differences.Count == 0 && _missing.Count == 0 && _unexpected.Count == 0;

    /// <summary>
    /// Compare two keyed sets. Both are walked, so key-set differences in either direction are
    /// found.
    /// </summary>
    /// <param name="expected">The reference set, normally from a Lua corpus.</param>
    /// <param name="actual">The set the port produced.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public CorpusDiff CompareAll(
        IReadOnlyDictionary<string, ScalarValue> expected,
        IReadOnlyDictionary<string, ScalarValue> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        // Ordinal ordering so the report reads the same on every machine and in every locale.
        foreach (string key in expected.Keys.Order(StringComparer.Ordinal))
        {
            if (!actual.TryGetValue(key, out ScalarValue actualValue))
            {
                _missing.Add(key);
                continue;
            }

            Compared++;
            Difference? difference = NumericComparison.Compare(key, expected[key], actualValue, Policy);
            if (difference is not null)
            {
                _differences.Add(difference);
            }
        }

        foreach (string key in actual.Keys.Order(StringComparer.Ordinal))
        {
            if (!expected.ContainsKey(key))
            {
                _unexpected.Add(key);
            }
        }

        return this;
    }

    /// <summary>
    /// Render the failure message. Empty when <see cref="IsClean"/>.
    /// </summary>
    /// <param name="context">What was being compared, e.g. a build name or a function name.</param>
    /// <returns>A multi-line report, or the empty string.</returns>
    public string Report(string context)
    {
        if (IsClean)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"{context}: {_differences.Count} of {Compared} values disagree");
        if (_missing.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $", {_missing.Count} keys missing");
        }

        if (_unexpected.Count > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $", {_unexpected.Count} keys unexpected");
        }

        builder.Append(CultureInfo.InvariantCulture, $" [{Policy.Describe()}]").AppendLine();

        Append(builder, "differences", _differences.Take(MaxReported).Select(static d => d.ToString()), _differences.Count);
        Append(builder, "missing keys", _missing.Take(MaxReported), _missing.Count);
        Append(builder, "unexpected keys", _unexpected.Take(MaxReported), _unexpected.Count);

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string heading, IEnumerable<string> lines, int total)
    {
        List<string> shown = [.. lines];
        if (shown.Count == 0)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"  {heading}:").AppendLine();
        foreach (string line in shown)
        {
            builder.Append("    ").AppendLine(line);
        }

        if (total > shown.Count)
        {
            builder.Append(CultureInfo.InvariantCulture, $"    ... and {total - shown.Count} more").AppendLine();
        }
    }
}
