using System.Globalization;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Replays <c>oracles/modcache.json</c> — the 23,207 pre-parsed mod lines of
/// <c>src/Data/ModCache.lua</c> — against the C# ModParser port, and reports how many of them
/// the port reproduces.
/// </summary>
/// <remarks>
/// <para>
/// <c>Pob.Parsing</c> is empty today (tickets 12-14 / issues #13-#15), so
/// <see cref="ModCacheOracle.Parser"/> is <see langword="null"/> and the suite measures the
/// corpus as a baseline instead of asserting against a parser that does not exist. Everything
/// here that can be checked without a parser <em>is</em> checked, so the day the parser is wired
/// in the corpus is already known to be sound.
/// </para>
/// <para>
/// The headline number is written to <c>dotnet/artifacts/modcache-score.json</c> on every run —
/// see <see cref="ModCacheScorecard"/>. That file, not this test's output, is the daily metric.
/// </para>
/// </remarks>
public sealed class ModCacheOracleTests
{
    public static TheoryData<string> Shards
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string name in ModCacheOracle.Corpus.ShardNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    /// <summary>
    /// One shard of the corpus, replayed. Sharded rather than one case per line because 23,207
    /// xUnit test cases costs more in discovery than it buys in signal, and a failing shard
    /// already names a contiguous alphabetical range plus every offending line in its message.
    /// </summary>
    /// <remarks>
    /// A line the port does not yet handle is not a failure — that is what the score is for.
    /// This test fails on the two things that are always wrong: the parser throwing, and the
    /// number of reproduced lines going backwards is caught by
    /// <see cref="Score_MeetsTheCommittedFloor"/>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Shards))]
    public void Shard_ReplaysAgainstTheParser(string shard)
    {
        IReadOnlyList<ModCacheEntry> entries = ModCacheOracle.Corpus.Shard(shard);
        Assert.NotEmpty(entries);

        ModCacheShardScore score = ModCacheScorecard.MeasureShard(entries, shard);

        Assert.Equal(entries.Count, score.Matched + score.Mismatched + score.Threw + score.NotAttempted);

        Assert.True(
            score.Threw == 0,
            $"{shard}: the parser threw on {score.Threw} of {score.Total} lines.{Environment.NewLine}"
            + string.Join(Environment.NewLine, score.Failures));

        if (ModCacheOracle.Parser is null)
        {
            // Non-vacuity: with no parser wired, every line must be reported as not attempted.
            // A seam that silently counted un-attempted lines as matches would make the whole
            // metric a lie, so it is checked rather than assumed.
            Assert.Equal(entries.Count, score.NotAttempted);
            Assert.Equal(0, score.Matched);
        }
    }

    /// <summary>
    /// The deliverable of ticket 05: <c>N / 23,207</c>, written somewhere a machine can read it.
    /// </summary>
    [Fact]
    public void Score_MeetsTheCommittedFloor()
    {
        ModCacheScorecard score = ModCacheScorecard.Measure(ModCacheOracle.Corpus);
        string path = score.Write();

        // Deliberately on stdout as well as in the file: a CI log that scrolls past is not the
        // metric, but a one-line grep target next to it costs nothing.
        Console.Out.WriteLine($"##pob-modcache-score {score.Headline} (scorecard: {path})");

        Assert.True(File.Exists(path), $"scorecard was not written to {path}");
        Assert.Equal(
            ModCacheOracle.Corpus.Entries.Count,
            score.LinesMatched + score.LinesMismatched + score.LinesThrew + score.LinesNotAttempted);

        Assert.True(
            score.LinesMatched >= ModCacheOracle.MatchedLineFloor,
            $"{score.Headline}, below the committed floor of {ModCacheOracle.MatchedLineFloor}. "
            + $"See {path}.");
    }

    /// <summary>
    /// Guards the corpus itself: every count in the header is recomputed from the entries. A
    /// generator that silently dropped half the cache would otherwise make the score meaningless
    /// and every shard above vacuous.
    /// </summary>
    [Fact]
    public void Corpus_HeaderAgreesWithItsEntries()
    {
        ModCacheCorpus corpus = ModCacheOracle.Corpus;
        ModCacheCorpusHeader header = corpus.Header;

        Assert.Equal("src/Data/ModCache.lua", header.Source);
        Assert.Equal("modLib.formatMod", header.CanonicalForm);

        // 23,215 raw lines = 23,207 statements + `local c = {}` + `return c` + five IIFE
        // openers/closers written as 6 lines (ModCache.lua:2, 5004, 10006, 15008, 20010, and
        // the trailing `end)();`).
        Assert.Equal(23_215, header.SourceLineCount);
        Assert.Equal(5, header.SourceChunkCount);
        Assert.Equal(header.SourceLineCount - 8, header.EntryCount);

        Assert.Equal(header.EntryCount, corpus.Entries.Count);
        Assert.Equal(header.ModCount, corpus.Entries.Sum(static e => e.Mods?.Count ?? 0));
        Assert.Equal(
            header.DistinctModCount,
            corpus.Entries.SelectMany(static e => e.Mods ?? []).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(header.LuaFullyParsedCount, corpus.Entries.Count(static e => e.Remainder is null));
        Assert.Equal(header.LuaRemainderCount, corpus.Entries.Count(static e => e.Remainder is not null));
        Assert.Equal(header.EntryCount, header.LuaFullyParsedCount + header.LuaRemainderCount);

        Assert.Equal(header.LuaNilModListCount, corpus.Entries.Count(static e => e.Mods is null));
        Assert.Equal(header.LuaEmptyModListCount, corpus.Entries.Count(static e => e.Mods is { Count: 0 }));
        Assert.Equal(header.LuaWithModsCount, corpus.Entries.Count(static e => e.Mods is { Count: > 0 }));
        Assert.Equal(
            header.EntryCount,
            header.LuaNilModListCount + header.LuaEmptyModListCount + header.LuaWithModsCount);

        Assert.Equal(
            header.LuaNilModsWithRemainderCount,
            corpus.Entries.Count(static e => e.Mods is null && e.Remainder is not null));
        Assert.Equal(
            header.LuaNilModsNoRemainderCount,
            corpus.Entries.Count(static e => e.Mods is null && e.Remainder is null));

        // Every shard together is exactly the corpus, in order.
        Assert.Equal(
            corpus.Entries,
            corpus.ShardNames.SelectMany(corpus.Shard).ToList());
    }

    /// <summary>
    /// The numbers the migration plan quotes. Pinned so that a regenerated cache
    /// (<c>REGENERATE_MOD_CACHE=1</c>) is a deliberate, reviewed change of the answer key rather
    /// than a silent shift of the denominator that makes yesterday's score incomparable.
    /// </summary>
    [Fact]
    public void Corpus_IsTheSizeTheMigrationPlanClaims()
    {
        ModCacheCorpusHeader header = ModCacheOracle.Corpus.Header;

        Assert.Equal(23_207, header.EntryCount);
        Assert.Equal(23_324, header.ModCount);

        // Lines PoB itself does not finish. These are NOT failures of the C# port.
        Assert.Equal(3_894, header.LuaRemainderCount);
        Assert.Equal(19_313, header.LuaFullyParsedCount);
    }

    /// <summary>
    /// The corpus covers the shapes that make the port hard, and the entries are the sorted,
    /// deduplicated set the generator claims.
    /// </summary>
    [Fact]
    public void Corpus_CoversTheShapesThatMatter()
    {
        IReadOnlyList<ModCacheEntry> entries = ModCacheOracle.Corpus.Entries;

        // Sorted by byte order and unique: the generator's determinism guarantee, checked here
        // rather than trusted.
        for (int i = 1; i < entries.Count; i++)
        {
            Assert.True(
                string.CompareOrdinal(entries[i - 1].Line, entries[i].Line) < 0,
                $"entries {i - 1} and {i} are out of order or duplicated: "
                + $"\"{entries[i - 1].Line}\" then \"{entries[i].Line}\"");
        }

        Dictionary<string, ModCacheEntry> byLine =
            entries.ToDictionary(static e => e.Line, StringComparer.Ordinal);

        // A plain numeric mod, consumed whole, one mod out.
        ModCacheEntry simple = byLine["+0.1 metres to Melee Strike Range"];
        Assert.Null(simple.Remainder);
        Assert.Equal(
            [
                "0.1 = MeleeWeaponRangeMetre|BASE|-|-|-",
                "0.1 = UnarmedRangeMetre|BASE|-|-|-",
            ],
            simple.Mods);

        // ModFlag rendering: "with Swords" sets Sword|Hit (0x400004).
        Assert.Contains(
            "0.1 = MeleeWeaponRangeMetre|BASE|Hit,Sword|-|-",
            byLine["+0.1 metres to Melee Strike Range with Swords"].Mods!);

        // A partial parse: mods AND a remainder on the same line. The normal case, not an error.
        ModCacheEntry partial = byLine["(2-4)% chance to deal Double Damage"];
        Assert.Equal("(2-4)% chance to deal   ", partial.Remainder);
        Assert.Equal(2, partial.Mods!.Count);
        Assert.Contains("type=Multiplier/", partial.Mods[0], StringComparison.Ordinal);

        // nil mod list is distinct from an empty one, and both occur.
        Assert.Null(byLine[" Wolf Alpha Talisman"].Mods);
        Assert.Contains(entries, static e => e.Mods is { Count: 0 });

        // Tags, nested mods, boolean and string values, multi-mod lines: all present.
        Assert.Contains(entries, static e => e.Mods?.Any(static m => m.Contains("mod=[", StringComparison.Ordinal)) == true);
        Assert.Contains(entries, static e => e.Mods?.Any(static m => m.Contains("|FLAG|", StringComparison.Ordinal)) == true);
        Assert.Contains(entries, static e => e.Mods?.Any(static m => m.Contains("|LIST|", StringComparison.Ordinal)) == true);
        Assert.Contains(entries, static e => e.Mods?.Any(static m => m.Contains("type=Condition/", StringComparison.Ordinal)) == true);
        Assert.Contains(entries, static e => e.Mods is { Count: > 3 });

        // Every canonical string is the five-field formatMod shape.
        foreach (ModCacheEntry entry in entries)
        {
            foreach (string mod in entry.Mods ?? [])
            {
                Assert.Contains(" = ", mod, StringComparison.Ordinal);
                Assert.True(
                    mod.Count(static c => c == '|') >= 4,
                    $"\"{entry.Line}\" produced a canonical string that is not formatMod-shaped: {mod}");
            }
        }
    }

    /// <summary>
    /// <c>modLib.formatMod</c> is a rendering, not a serialisation, so it is not injective in
    /// general — see <c>dotnet/tools/modcache-oracle/README.md</c>. This pins how far that
    /// actually bites on this corpus: exactly twice, and both times because a tag sits behind a
    /// nil hole in the mod's array part, where <c>ipairs</c> — and therefore
    /// <c>ModStore:EvalMod</c>, the only consumer — never reaches it. Such a tag is dead in the
    /// engine, so the canonical form still says exactly what PoB evaluates.
    /// </summary>
    /// <remarks>
    /// If a regenerated cache ever produces a collision that is <em>not</em> a shadowed tag, the
    /// canonical form has stopped distinguishing two mods the engine does distinguish, and it
    /// must be extended rather than quietly weakened. That is what this test is here to catch.
    /// </remarks>
    [Fact]
    public void CanonicalForm_IsAmbiguousOnlyWhereTheEngineIsToo()
    {
        ModCacheCorpus corpus = ModCacheOracle.Corpus;

        Assert.Equal(corpus.Header.AmbiguousCanonicalCount, corpus.Ambiguities.Count);
        Assert.Equal(2, corpus.Ambiguities.Count);

        foreach (ModCacheAmbiguity ambiguity in corpus.Ambiguities)
        {
            Assert.True(
                ambiguity.ShadowedTag,
                $"canonical string \"{ambiguity.Canonical}\" (from \"{ambiguity.Line}\") stands for two "
                + "different mod tables and it is NOT the known ipairs-hole case. The canonical form "
                + "no longer distinguishes two mods the engine does; extend it.");

            Assert.Contains("Sacrificial Zeal", ambiguity.Line, StringComparison.Ordinal);
        }

        // 23,324 mods, 21,121 distinct canonical strings: the 2,203-string gap is duplicates
        // across different lines, which is expected and harmless -- each line carries its own
        // expected value.
        Assert.True(corpus.Header.DistinctModCount < corpus.Header.ModCount);
    }

    /// <summary>
    /// The scorecard is the machine-readable deliverable, so its shape is a test, not a hope.
    /// </summary>
    [Fact]
    public void Scorecard_IsMachineReadable()
    {
        ModCacheScorecard score = ModCacheScorecard.Measure(ModCacheOracle.Corpus);
        string path = score.Write();

        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        System.Text.Json.JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal("modcache-lines-reproduced", root.GetProperty("metric").GetString());
        Assert.Equal(ModCacheOracle.Parser is not null, root.GetProperty("parserWired").GetBoolean());

        System.Text.Json.JsonElement scores = root.GetProperty("score");
        Assert.Equal(score.LinesMatched, scores.GetProperty("linesMatched").GetInt32());
        Assert.Equal(23_207, scores.GetProperty("linesTotal").GetInt32());
        Assert.Equal(19_313, scores.GetProperty("luaFullyParsedTotal").GetInt32());
        Assert.Equal(3_894, scores.GetProperty("luaRemainderTotal").GetInt32());
        Assert.Equal(ModCacheOracle.MatchedLineFloor, scores.GetProperty("floor").GetInt32());

        Assert.Equal(
            ModCacheOracle.ShardCount,
            root.GetProperty("shards").EnumerateArray().Count());
        Assert.Equal(
            23_207,
            root.GetProperty("shards").EnumerateArray().Sum(static s => s.GetProperty("total").GetInt32()));

        Assert.Contains(
            23_207.ToString(CultureInfo.InvariantCulture),
            root.GetProperty("headline").GetString()!,
            StringComparison.Ordinal);
    }
}
