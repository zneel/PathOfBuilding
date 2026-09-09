using System.Globalization;
using System.Text.Json;

namespace Pob.Tests;

/// <summary>
/// One build's worth of mod stores, as dumped out of the Lua engine by
/// <c>dotnet/tools/modstore-dump/dump.lua</c> (migration ticket 06).
/// </summary>
/// <remarks>
/// <para>
/// These records are the *shape of a mod store*, not the shape of the JSON: when
/// <c>Pob.Core</c>'s <c>ModDb</c> / <c>ModList</c> arrive (tickets 10-11) the wiring is one
/// adapter that projects a real store into <see cref="ModStoreDumpStore"/>. Everything
/// downstream — <see cref="ModStoreTextFormat"/>, the invariant checks, the snapshots —
/// keeps working unchanged, which is the whole reason the dump is parsed into a model
/// rather than compared as text.
/// </para>
/// </remarks>
public sealed record ModStoreDump(
    string Build,
    string BuildFile,
    string MainSkill,
    bool UsedModCache,
    IReadOnlyList<ModStoreDumpStore> Stores);

/// <summary>A single mod store: <c>env.modDB</c>, <c>env.enemyDB</c>, and so on.</summary>
public sealed record ModStoreDumpStore(
    string Name,
    string Path,
    string Class,
    bool HasParent,
    int DeclaredModCount,
    int DeclaredConditionCount,
    int DeclaredMultiplierCount,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<ModStoreDumpMultiplier> Multipliers,
    IReadOnlyList<ModStoreDumpMod> Mods);

/// <summary>An entry of <c>ModStore.multipliers</c>.</summary>
public sealed record ModStoreDumpMultiplier(string Name, double Value);

/// <summary>
/// One modifier, as built by <c>modLib.createMod</c> (<c>src/Modules/ModTools.lua:32-66</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>Source</c> is null when the Lua mod carried no <c>source</c> at all. That is not the
/// same as an empty string: <c>ModStore</c>'s source filtering matches on
/// <c>mod.source:match("[^:]+")</c>, and <c>createMod</c> only assigns a source when its
/// fourth positional argument happens to be a string — a number there becomes <c>flags</c>
/// instead. Mis-attributed sources are the failure mode this whole harness exists to catch,
/// so the distinction is preserved.
/// </para>
/// <para>
/// <c>Extra</c> holds fields bolted on after construction (<c>sourceSlot</c>,
/// <c>replaced</c>, ...), so the model is lossless without the schema having to enumerate
/// them.
/// </para>
/// <para>
/// <c>Print</c> is the line <c>ModDB:Print()</c> would emit for this mod
/// (<c>src/Classes/ModDB.lua:336-370</c>). <see cref="ModStoreTextFormat"/> reconstructs it
/// from the other fields and the test suite asserts the two agree — which is what proves the
/// model captured everything the Lua formatter looked at.
/// </para>
/// </remarks>
public sealed record ModStoreDumpMod(
    string Name,
    string Type,
    ModStoreValue Value,
    int Flags,
    string FlagNames,
    int KeywordFlags,
    string KeywordFlagNames,
    string? Source,
    IReadOnlyList<ModStoreDumpTag> Tags,
    IReadOnlyList<KeyValuePair<string, ModStoreValue>> Extra,
    string Print)
{
    /// <summary>
    /// The part of <see cref="Source"/> before the first colon — <c>"Item"</c>, <c>"Tree"</c>,
    /// <c>"Config"</c>, <c>"Skill"</c>, <c>"Base"</c>. This is the granularity every
    /// source-filtered query in <c>ModStore</c> works at.
    /// </summary>
    public string SourcePrefix
    {
        get
        {
            if (Source is null)
            {
                return "(none)";
            }

            // An empty source is a third thing again: CalcSetup.lua:424 passes
            // `modSource or groupCfg.slotName or ""`, so a socket group with no slot name
            // sources its mods to the empty string, which no prefix filter will ever match.
            if (Source.Length == 0)
            {
                return "(empty)";
            }

            int colon = Source.IndexOf(':', StringComparison.Ordinal);
            return colon < 0 ? Source : Source[..colon];
        }
    }
}

/// <summary>
/// A tag off a mod's array part. Tags decide whether a mod applies at all
/// (<c>ModStore:EvalMod</c>, <c>src/Classes/ModStore.lua:363-971</c>), so a dump without them
/// cannot catch the most common class of setup bug.
/// </summary>
/// <remarks>
/// <c>Entries</c> holds the tag's parameters in the order the dump emitted them, which is the
/// Lua sort order (ordinal).
/// </remarks>
public sealed record ModStoreDumpTag(IReadOnlyList<KeyValuePair<string, ModStoreValue>> Entries)
{
    /// <summary>The tag's <c>type</c> discriminator, or null for the untyped tags EvalMod skips.</summary>
    public string? Type => Entries
        .Where(static e => string.Equals(e.Key, "type", StringComparison.Ordinal))
        .Select(static e => (e.Value as ModStoreStringValue)?.Value)
        .FirstOrDefault();
}

/// <summary>
/// A Lua value out of a mod store. Mod values are not always numbers: they nest whole
/// modifiers, key/value records, string lists and — for jewel radius mods — closures.
/// </summary>
public abstract record ModStoreValue;

/// <summary>A Lua number.</summary>
public sealed record ModStoreNumberValue(double Value) : ModStoreValue;

/// <summary>A Lua string.</summary>
public sealed record ModStoreStringValue(string Value) : ModStoreValue;

/// <summary>A Lua boolean, which is what every <c>FLAG</c> mod carries.</summary>
public sealed record ModStoreBooleanValue(bool Value) : ModStoreValue;

/// <summary>Lua <c>nil</c>.</summary>
public sealed record ModStoreNilValue : ModStoreValue
{
    /// <summary>The single instance; <c>nil</c> has no state.</summary>
    public static ModStoreNilValue Instance { get; } = new();
}

/// <summary>
/// A Lua function, identified by where it was defined rather than by its address. Radius-jewel
/// mods carry one under <c>value.func</c>; porting those bodies is ticket 04's behaviour-key
/// contract, so all this dump can pin is that the mod points at the same definition site.
/// </summary>
public sealed record ModStoreFunctionValue(string Definition) : ModStoreValue;

/// <summary>A nested modifier, as in <c>{ mod = &lt;inner mod&gt; }</c>.</summary>
public sealed record ModStoreModValue(ModStoreDumpMod Mod) : ModStoreValue;

/// <summary>A Lua table with only array entries, such as a tag's <c>varList</c>.</summary>
public sealed record ModStoreListValue(IReadOnlyList<ModStoreValue> Items) : ModStoreValue;

/// <summary>A Lua table with named entries, in ordinal key order.</summary>
public sealed record ModStoreMapValue(IReadOnlyList<KeyValuePair<string, ModStoreValue>> Entries) : ModStoreValue;

/// <summary>
/// Reads the committed dumps under <c>Pob.Tests/oracles/modstore/</c>.
/// </summary>
public static class ModStoreDumpReader
{
    /// <summary>The dump format this reader understands; bumped by the Lua generator in lockstep.</summary>
    public const int SupportedFormat = 1;

    /// <summary>Build slugs, in the order the generator emits them. One file each.</summary>
    public static IReadOnlyList<string> BuildSlugs { get; } =
    [
        "DualSavior",
        "DualWieldCosprisCoC",
        "GeneralsPerforateZerker",
        "MirageArcherToxicRain",
        "OccVortex",
    ];

    /// <summary>Directory holding the committed dumps.</summary>
    public static string CorpusDirectory =>
        System.IO.Path.Combine(SolutionDirectory(), "Pob.Tests", "oracles", "modstore");

    /// <summary>Reads and parses one build's dump.</summary>
    /// <param name="slug">A value from <see cref="BuildSlugs"/>.</param>
    public static ModStoreDump Load(string slug)
    {
        string path = System.IO.Path.Combine(CorpusDirectory, slug + ".json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"mod-store dump missing at {path}; regenerate with "
                + "`cd src && luajit ../dotnet/tools/modstore-dump/dump.lua`",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        int format = root.GetProperty("format").GetInt32();
        if (format != SupportedFormat)
        {
            throw new InvalidDataException(
                $"{path} is dump format {format}, this reader understands {SupportedFormat}");
        }

        List<ModStoreDumpStore> stores = [];
        foreach (JsonElement store in root.GetProperty("stores").EnumerateArray())
        {
            stores.Add(ReadStore(store));
        }

        return new ModStoreDump(
            root.GetProperty("build").GetString()!,
            root.GetProperty("buildFile").GetString()!,
            root.GetProperty("mainSkill").GetString()!,
            root.GetProperty("usedModCache").GetBoolean(),
            stores);
    }

    /// <summary>Reads and parses every build's dump, in <see cref="BuildSlugs"/> order.</summary>
    public static IReadOnlyList<ModStoreDump> LoadAll() => BuildSlugs.Select(Load).ToArray();

    private static ModStoreDumpStore ReadStore(JsonElement store)
    {
        List<string> conditions = [];
        foreach (JsonElement condition in store.GetProperty("conditions").EnumerateArray())
        {
            conditions.Add(condition.GetString()!);
        }

        List<ModStoreDumpMultiplier> multipliers = [];
        foreach (JsonElement multiplier in store.GetProperty("multipliers").EnumerateArray())
        {
            multipliers.Add(new ModStoreDumpMultiplier(
                multiplier.GetProperty("name").GetString()!,
                ReadNumber(multiplier.GetProperty("value"))));
        }

        List<ModStoreDumpMod> mods = [];
        foreach (JsonElement mod in store.GetProperty("mods").EnumerateArray())
        {
            mods.Add(ReadMod(mod));
        }

        return new ModStoreDumpStore(
            store.GetProperty("name").GetString()!,
            store.GetProperty("path").GetString()!,
            store.GetProperty("class").GetString()!,
            store.GetProperty("hasParent").GetBoolean(),
            store.GetProperty("modCount").GetInt32(),
            store.GetProperty("conditionCount").GetInt32(),
            store.GetProperty("multiplierCount").GetInt32(),
            conditions,
            multipliers,
            mods);
    }

    private static ModStoreDumpMod ReadMod(JsonElement mod)
    {
        List<ModStoreDumpTag> tags = [];
        foreach (JsonElement tag in mod.GetProperty("tags").EnumerateArray())
        {
            tags.Add(new ModStoreDumpTag(ReadEntries(tag)));
        }

        IReadOnlyList<KeyValuePair<string, ModStoreValue>> extra =
            mod.TryGetProperty("extra", out JsonElement extraElement) ? ReadEntries(extraElement) : [];

        return new ModStoreDumpMod(
            mod.GetProperty("name").GetString()!,
            mod.GetProperty("type").GetString()!,
            ReadValue(mod.GetProperty("value")),
            mod.GetProperty("flags").GetInt32(),
            mod.GetProperty("flagNames").GetString()!,
            mod.GetProperty("keywordFlags").GetInt32(),
            mod.GetProperty("keywordFlagNames").GetString()!,
            mod.GetProperty("source").ValueKind == JsonValueKind.Null
                ? null
                : mod.GetProperty("source").GetString(),
            tags,
            extra,
            mod.GetProperty("print").GetString()!);
    }

    private static List<KeyValuePair<string, ModStoreValue>> ReadEntries(JsonElement element)
    {
        List<KeyValuePair<string, ModStoreValue>> entries = [];
        foreach (JsonProperty property in element.EnumerateObject())
        {
            entries.Add(new KeyValuePair<string, ModStoreValue>(property.Name, ReadValue(property.Value)));
        }

        return entries;
    }

    /// <summary>
    /// Parses the number's own text rather than calling <c>GetDouble()</c>.
    /// <c>JsonElement.GetDouble()</c> folds the JSON literal <c>-0</c> to positive zero, and
    /// the sign of zero is exactly what this corpus has to carry: the engine stores
    /// <c>-0 INC ActionSpeed</c> for an inactive Chill and Lua prints it as <c>-0</c>.
    /// .NET's parser is correctly rounded, so the text maps back to the identical double.
    /// </summary>
    private static double ReadNumber(JsonElement element) =>
        double.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static ModStoreValue ReadValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return ModStoreNilValue.Instance;

            case JsonValueKind.True:
                return new ModStoreBooleanValue(true);

            case JsonValueKind.False:
                return new ModStoreBooleanValue(false);

            case JsonValueKind.Number:
                return new ModStoreNumberValue(ReadNumber(element));

            case JsonValueKind.String:
                return new ModStoreStringValue(element.GetString()!);

            case JsonValueKind.Array:
                return new ModStoreListValue(element.EnumerateArray().Select(ReadValue).ToArray());

            case JsonValueKind.Object:
                // A function marker, a nested mod, or a plain record — in that order of
                // specificity. The generator writes __lua for the first and the full mod
                // schema for the second.
                if (element.TryGetProperty("__lua", out JsonElement lua)
                    && string.Equals(lua.GetString(), "function", StringComparison.Ordinal))
                {
                    return new ModStoreFunctionValue(element.GetProperty("def").GetString()!);
                }

                if (element.TryGetProperty("print", out _) && element.TryGetProperty("tags", out _))
                {
                    return new ModStoreModValue(ReadMod(element));
                }

                return new ModStoreMapValue(ReadEntries(element));

            default:
                throw new InvalidDataException(
                    FormattableString.Invariant($"unexpected JSON value kind {element.ValueKind} in a mod store dump"));
        }
    }

    private static string SolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(System.IO.Path.Combine(directory.FullName, "PathOfBuilding.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                FormattableString.Invariant($"no PathOfBuilding.sln above {AppContext.BaseDirectory}"));
    }
}

/// <summary>Shared number formatting; Lua's <c>tostring</c> on a double is <c>%.14g</c>.</summary>
internal static class ModStoreLua
{
    /// <summary>
    /// Formats a double the way Lua's <c>tostring</c> does. LUAI_NUMFFORMAT is <c>"%.14g"</c>
    /// in Lua 5.1 and LuaJIT alike, which is why the engine prints <c>18.016666666667</c> and
    /// not <c>18.016666666666666</c>. .NET's <c>G14</c> picks the same fixed/scientific
    /// crossover; only the exponent's spelling differs, and that is normalised here.
    /// </summary>
    public static string Number(double value)
    {
        string text = value.ToString("G14", CultureInfo.InvariantCulture);

        int exponent = text.IndexOf('E', StringComparison.Ordinal);
        if (exponent < 0)
        {
            return text;
        }

        string mantissa = text[..exponent];
        string rest = text[(exponent + 1)..];
        char sign = rest[0];
        string digits = rest[1..].TrimStart('0');

        if (digits.Length < 2)
        {
            digits = digits.PadLeft(2, '0');
        }

        return mantissa + "e" + sign + digits;
    }

    /// <summary>Formats a boolean the way Lua's <c>tostring</c> does.</summary>
    public static string Boolean(bool value) => value ? "true" : "false";
}
