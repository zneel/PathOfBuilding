using System.Globalization;
using System.Text;

namespace Pob.Tests;

/// <summary>
/// Condenses a build's mod stores to the handful of distributions that actually move when
/// setup goes wrong: how many mods each store holds, where they came from, what types they
/// are, what shapes their values take and which tags gate them.
/// </summary>
/// <remarks>
/// This is what the committed <c>.verified.txt</c> snapshots hold. A full per-mod listing
/// would just be the JSON dump again in another spelling; the summary is small enough to
/// read in a pull request, and a mod that moved between stores, lost its source or lost a
/// tag changes it. The per-mod check is <c>ModStoreDumpTests.Print_RoundTripsThroughTheModel</c>,
/// which compares every single line against what Lua emitted.
/// </remarks>
public static class ModStoreSummary
{
    /// <summary>Renders the snapshot text for one build.</summary>
    public static string Render(ModStoreDump dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        StringBuilder text = new();

        Field(text, "build", dump.Build);
        Field(text, "buildFile", dump.BuildFile);
        Field(text, "mainSkill", dump.MainSkill);
        Field(text, "usedModCache", dump.UsedModCache ? "true" : "false");
        Field(text, "stores", Count(dump.Stores.Count));

        foreach (ModStoreDumpStore store in dump.Stores)
        {
            text.Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"== {store.Path} ({store.Class}) ==\n");
            text.Append(CultureInfo.InvariantCulture,
                $"mods {store.Mods.Count} | conditions {store.Conditions.Count} "
                + $"| multipliers {store.Multipliers.Count} | parent {(store.HasParent ? "yes" : "no")}\n");

            Distribution(text, "mod type", store.Mods.Select(static m => m.Type));
            Distribution(text, "source", store.Mods.Select(static m => m.SourcePrefix));
            Distribution(text, "value kind", store.Mods.Select(static m => ValueKind(m.Value)));
            Distribution(text, "tag type", store.Mods.SelectMany(static m => m.Tags).Select(static t => t.Type ?? "(untyped)"));
            Distribution(text, "extra field", store.Mods.SelectMany(static m => m.Extra).Select(static e => e.Key));

            Field(text, "  tagged mods", Count(store.Mods.Count(static m => m.Tags.Count > 0)));
            Field(text, "  flagged mods", Count(store.Mods.Count(static m => m.Flags != 0)));
            Field(text, "  keyword-flagged mods", Count(store.Mods.Count(static m => m.KeywordFlags != 0)));
            Field(text, "  unsourced mods", Count(store.Mods.Count(static m => m.Source is null)));
            Field(text, "  distinct mod names", Count(store.Mods.Select(static m => m.Name).Distinct(StringComparer.Ordinal).Count()));
        }

        // The one store small enough to pin line by line, and the most diagnostic: if the
        // main skill's own mod list is wrong, every damage number downstream is wrong.
        ModStoreDumpStore? skillModList = dump.Stores
            .FirstOrDefault(static s => string.Equals(s.Name, "skillModList", StringComparison.Ordinal));

        if (skillModList is not null)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"\n== {skillModList.Path} — full ModDB:Print() listing ==\n");
            text.Append(ModStoreTextFormat.PrintStore(skillModList));
        }

        return text.ToString();
    }

    /// <summary>The name this snapshot's <c>.verified.txt</c> is filed under.</summary>
    public static string SnapshotName(string slug) => slug + ".summary";

    private static void Field(StringBuilder text, string name, string value) =>
        text.Append(CultureInfo.InvariantCulture, $"{name,-24}{value}\n");

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A "name count" histogram, ordered by name so the line is stable whatever order the
    /// mods came in.
    /// </summary>
    private static void Distribution(StringBuilder text, string label, IEnumerable<string> values)
    {
        List<string> parts = values
            .GroupBy(static v => v, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => FormattableString.Invariant($"{g.Key} {g.Count()}"))
            .ToList();

        Field(text, "  " + label, parts.Count == 0 ? "(none)" : string.Join(", ", parts));
    }

    private static string ValueKind(ModStoreValue value) => value switch
    {
        ModStoreNumberValue => "number",
        ModStoreStringValue => "string",
        ModStoreBooleanValue => "boolean",
        ModStoreNilValue => "nil",
        ModStoreFunctionValue => "function",
        ModStoreModValue => "mod",
        ModStoreListValue => "list",
        ModStoreMapValue map => map.Entries.Any(static e => string.Equals(e.Key, "mod", StringComparison.Ordinal))
            ? "record{mod}"
            : "record",
        _ => "?",
    };
}
