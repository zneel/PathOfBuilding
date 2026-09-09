using System.Text.Json;

namespace Pob.Tests;

/// <summary>
/// One line of <c>src/Data/ModCache.lua</c>: the mod text PoB was asked to parse, the mods it
/// produced, and whatever text it could not consume.
/// </summary>
/// <param name="Line">The mod text, exactly as it appears as a key in the Lua cache.</param>
/// <param name="Mods">
/// The canonical <c>modLib.formatMod</c> string for each mod, in order. <see langword="null"/>
/// where the cache stored <c>nil</c>, which is a different outcome from an empty list and the
/// port has to reproduce the difference: <c>nil</c> means "no mod list at all", <c>{}</c> means
/// "a mod list that came out empty".
/// </param>
/// <param name="Remainder">
/// The unconsumed text, or <see langword="null"/> when PoB consumed the whole line. This is
/// <c>line:match("%S") and line</c> from <c>src/Modules/ModParser.lua:6992</c> — the input with
/// every matched fragment blanked out — so <see langword="null"/> is the definition of "PoB
/// parsed this line completely".
/// </param>
public sealed record ModCacheEntry(string Line, IReadOnlyList<string>? Mods, string? Remainder)
{
    /// <summary>
    /// True when PoB itself consumed the whole line. The 3,894 entries where this is false are
    /// lines PoB does not fully parse either; they are pinned so the port reproduces PoB
    /// exactly, but they must never be counted as C# failures.
    /// </summary>
    public bool LuaParsedWholeLine => Remainder is null;
}

/// <summary>
/// What a wired-in C# parser must hand back for one mod line: the same pair
/// <c>modLib.parseMod</c> returns, with each mod already rendered through the C# port of
/// <c>modLib.formatMod</c>.
/// </summary>
/// <param name="Mods">
/// Canonical mod strings in order, or <see langword="null"/> to mean Lua's <c>nil</c> mod list.
/// </param>
/// <param name="Remainder">Unconsumed text, or <see langword="null"/> if the line was consumed.</param>
public sealed record ModCacheParse(IReadOnlyList<string>? Mods, string? Remainder);

/// <summary>Parses one mod line into the oracle's canonical form.</summary>
/// <param name="line">The mod text.</param>
/// <returns>The parse, or <see langword="null"/> if the parser declines the line outright.</returns>
public delegate ModCacheParse? ModCacheParser(string line);

/// <summary>How one corpus entry came out when replayed through the C# parser.</summary>
public enum ModCacheOutcome
{
    /// <summary>No parser is wired yet. Counted as "not attempted", never as a failure.</summary>
    NotWired,

    /// <summary>Mods and remainder both reproduce the Lua cache exactly.</summary>
    Match,

    /// <summary>The parser answered, and disagreed with the Lua cache.</summary>
    Mismatch,

    /// <summary>The parser threw. Always a failure, whatever the score is.</summary>
    Threw,
}

/// <summary>
/// The <c>src/Data/ModCache.lua</c> answer key (migration ticket 05), loaded from
/// <c>oracles/modcache.json</c> and replayed against the C# ModParser port.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is produced by <c>dotnet/tools/modcache-oracle/dump.lua</c>, which loads the real
/// cache and renders every mod through the real <c>modLib.formatMod</c>
/// (<c>src/Modules/ModTools.lua:231-233</c>), so comparison is on canonical text rather than on
/// nested table structure. Regenerate it with <c>luajit dotnet/tools/modcache-oracle/dump.lua</c>;
/// see that directory's README for the canonical form and its measured limits.
/// </para>
/// </remarks>
public static class ModCacheOracle
{
    /// <summary>
    /// The one wiring point for the ModParser port (tickets 12-14, issues #13-#15).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is <see langword="null"/> today: <c>Pob.Parsing</c> is an empty assembly, so this
    /// suite measures the corpus as a baseline and reports a score of zero rather than
    /// asserting against a parser that does not exist.
    /// </para>
    /// <para>
    /// Wiring it in is this one line:
    /// </para>
    /// <code>
    /// public static ModCacheParser? Parser =&gt; line =&gt; ModParser.ParseForOracle(line);
    /// </code>
    /// <para>
    /// The delegate must return mods already rendered by the C# port of
    /// <c>modLib.formatMod</c>, and must preserve the <c>null</c>-versus-empty distinction in
    /// <see cref="ModCacheParse.Mods"/>. Nothing else in this file changes.
    /// </para>
    /// </remarks>
    public static ModCacheParser? Parser => null;

    /// <summary>
    /// The completeness ratchet. <see cref="ModCacheOracleTests.Score_MeetsTheCommittedFloor"/>
    /// fails when the number of lines reproduced drops below this. Raise it as tickets 12-14
    /// land; never lower it without saying why in the commit message.
    /// </summary>
    public const int MatchedLineFloor = 0;

    /// <summary>Number of shards the corpus is split into for <c>[Theory]</c> reporting.</summary>
    public const int ShardCount = 24;

    private static readonly Lazy<ModCacheCorpus> LoadedCorpus = new(Load, isThreadSafe: true);

    /// <summary>The loaded corpus. Parsed once per test assembly.</summary>
    public static ModCacheCorpus Corpus => LoadedCorpus.Value;

    /// <summary>Replays one entry through <see cref="Parser"/>.</summary>
    public static ModCacheOutcome Evaluate(ModCacheEntry entry, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(entry);
        detail = null;

        ModCacheParser? parser = Parser;
        if (parser is null)
        {
            return ModCacheOutcome.NotWired;
        }

        ModCacheParse? actual;

        try
        {
            actual = parser(entry.Line);
        }
        catch (Exception ex)
        {
            detail = $"{ex.GetType().Name}: {ex.Message}";
            return ModCacheOutcome.Threw;
        }

        if (actual is null)
        {
            detail = "parser declined the line";
            return ModCacheOutcome.Mismatch;
        }

        if (!ModsEqual(entry.Mods, actual.Mods))
        {
            detail = $"mods: expected {Show(entry.Mods)}, got {Show(actual.Mods)}";
            return ModCacheOutcome.Mismatch;
        }

        if (!string.Equals(entry.Remainder, actual.Remainder, StringComparison.Ordinal))
        {
            detail = $"remainder: expected {Quote(entry.Remainder)}, got {Quote(actual.Remainder)}";
            return ModCacheOutcome.Mismatch;
        }

        return ModCacheOutcome.Match;
    }

    private static bool ModsEqual(IReadOnlyList<string>? expected, IReadOnlyList<string>? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Show(IReadOnlyList<string>? mods) =>
        mods is null ? "nil" : $"[{string.Join(" ;; ", mods)}]";

    private static string Quote(string? text) => text is null ? "nil" : $"\"{text}\"";

    /// <summary>
    /// Locates the repository's <c>dotnet/</c> directory by walking up from the test assembly,
    /// the same way <see cref="LuaCompatOracleTests"/> does. The corpus is read out of the source
    /// tree rather than copied to the output directory, so a stale build never replays a stale
    /// answer key.
    /// </summary>
    public static string SolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PathOfBuilding.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"no PathOfBuilding.sln above {AppContext.BaseDirectory}")
            : directory.FullName;
    }

    /// <summary>Path of the committed corpus.</summary>
    public static string CorpusPath() =>
        Path.Combine(SolutionDirectory(), "Pob.Tests", "oracles", "modcache.json");

    private static ModCacheCorpus Load()
    {
        string path = CorpusPath();

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"oracle corpus missing at {path}; run luajit dotnet/tools/modcache-oracle/dump.lua",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        int format = root.GetProperty("format").GetInt32();
        if (format != 1)
        {
            throw new InvalidOperationException($"corpus format {format} is not supported by this test");
        }

        List<ModCacheEntry> entries = [];

        foreach (JsonElement element in root.GetProperty("entries").EnumerateArray())
        {
            string line = element[0].GetString()
                ?? throw new InvalidOperationException("corpus entry has a null line");

            IReadOnlyList<string>? mods = null;
            if (element[1].ValueKind != JsonValueKind.Null)
            {
                List<string> list = [];
                foreach (JsonElement mod in element[1].EnumerateArray())
                {
                    list.Add(mod.GetString()
                        ?? throw new InvalidOperationException($"corpus entry '{line}' has a null mod"));
                }

                mods = list;
            }

            string? remainder = element[2].ValueKind == JsonValueKind.Null ? null : element[2].GetString();

            entries.Add(new ModCacheEntry(line, mods, remainder));
        }

        List<ModCacheAmbiguity> ambiguities = [];
        foreach (JsonElement element in root.GetProperty("ambiguousCanonical").EnumerateArray())
        {
            ambiguities.Add(new ModCacheAmbiguity(
                element.GetProperty("canonical").GetString()!,
                element.GetProperty("line").GetString()!,
                element.GetProperty("shadowedTag").GetBoolean()));
        }

        ModCacheCorpusHeader header = new(
            SourceLineCount: root.GetProperty("sourceLineCount").GetInt32(),
            SourceChunkCount: root.GetProperty("sourceChunkCount").GetInt32(),
            EntryCount: root.GetProperty("entryCount").GetInt32(),
            ModCount: root.GetProperty("modCount").GetInt32(),
            DistinctModCount: root.GetProperty("distinctModCount").GetInt32(),
            LuaFullyParsedCount: root.GetProperty("luaFullyParsedCount").GetInt32(),
            LuaRemainderCount: root.GetProperty("luaRemainderCount").GetInt32(),
            LuaWithModsCount: root.GetProperty("luaWithModsCount").GetInt32(),
            LuaEmptyModListCount: root.GetProperty("luaEmptyModListCount").GetInt32(),
            LuaNilModListCount: root.GetProperty("luaNilModListCount").GetInt32(),
            LuaNilModsWithRemainderCount: root.GetProperty("luaNilModsWithRemainderCount").GetInt32(),
            LuaNilModsNoRemainderCount: root.GetProperty("luaNilModsNoRemainderCount").GetInt32(),
            ShadowedTagCount: root.GetProperty("shadowedTagCount").GetInt32(),
            AmbiguousCanonicalCount: root.GetProperty("ambiguousCanonicalCount").GetInt32(),
            CanonicalForm: root.GetProperty("canonicalForm").GetString()!,
            Source: root.GetProperty("source").GetString()!);

        return new ModCacheCorpus(header, entries, ambiguities);
    }
}

/// <summary>A canonical string that stands for two structurally different mod tables.</summary>
/// <param name="Canonical">The canonical string.</param>
/// <param name="Line">The mod line whose mod collided with an earlier one.</param>
/// <param name="ShadowedTag">
/// True when the collision is the known <c>ipairs</c>-hole case: a tag sitting at array index 2
/// with index 1 <c>nil</c>, which <c>formatTags</c> and <c>ModStore:EvalMod</c> both skip. Such a
/// tag is dead in the engine, so the canonical form is still faithful to what PoB evaluates.
/// </param>
public sealed record ModCacheAmbiguity(string Canonical, string Line, bool ShadowedTag);

/// <summary>The corpus header, as counted by the generator against the Lua cache.</summary>
public sealed record ModCacheCorpusHeader(
    int SourceLineCount,
    int SourceChunkCount,
    int EntryCount,
    int ModCount,
    int DistinctModCount,
    int LuaFullyParsedCount,
    int LuaRemainderCount,
    int LuaWithModsCount,
    int LuaEmptyModListCount,
    int LuaNilModListCount,
    int LuaNilModsWithRemainderCount,
    int LuaNilModsNoRemainderCount,
    int ShadowedTagCount,
    int AmbiguousCanonicalCount,
    string CanonicalForm,
    string Source);

/// <summary>The loaded corpus, sharded for <c>[Theory]</c> reporting.</summary>
public sealed class ModCacheCorpus
{
    private readonly Dictionary<string, List<ModCacheEntry>> _shards = new(StringComparer.Ordinal);

    internal ModCacheCorpus(
        ModCacheCorpusHeader header,
        IReadOnlyList<ModCacheEntry> entries,
        IReadOnlyList<ModCacheAmbiguity> ambiguities)
    {
        Header = header;
        Entries = entries;
        Ambiguities = ambiguities;

        // Contiguous slices of the (line-sorted) corpus. Deterministic, evenly sized, and a
        // failing shard names a contiguous alphabetical range of mod text rather than a hash
        // bucket nobody can reason about.
        int size = (entries.Count + ModCacheOracle.ShardCount - 1) / ModCacheOracle.ShardCount;
        for (int shard = 0; shard < ModCacheOracle.ShardCount; shard++)
        {
            int start = shard * size;
            if (start >= entries.Count)
            {
                break;
            }

            int end = Math.Min(start + size, entries.Count);
            List<ModCacheEntry> bucket = [];
            for (int i = start; i < end; i++)
            {
                bucket.Add(entries[i]);
            }

            _shards[ShardName(shard)] = bucket;
        }
    }

    /// <summary>Counts the generator recorded while reading the Lua cache.</summary>
    public ModCacheCorpusHeader Header { get; }

    /// <summary>Every mod line, sorted by byte order of the line text.</summary>
    public IReadOnlyList<ModCacheEntry> Entries { get; }

    /// <summary>Canonical strings that stand for more than one mod table. See the tool README.</summary>
    public IReadOnlyList<ModCacheAmbiguity> Ambiguities { get; }

    /// <summary>Shard names, in order.</summary>
    public IEnumerable<string> ShardNames => _shards.Keys;

    /// <summary>The entries in one shard.</summary>
    public IReadOnlyList<ModCacheEntry> Shard(string name) => _shards[name];

    private static string ShardName(int index) =>
        $"shard {(index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture)}"
        + $"/{ModCacheOracle.ShardCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
