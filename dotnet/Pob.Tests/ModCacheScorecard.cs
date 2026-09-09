using System.Globalization;
using System.Text.Json;

namespace Pob.Tests;

/// <summary>
/// The headline number for the ModParser port: <c>N / 23,207 mod lines reproduce ModCache.lua</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the deliverable of migration ticket 05, and it is written to a file rather than left
/// in a log: every <c>dotnet test</c> run drops <c>modcache-score.json</c> at
/// <see cref="OutputPath"/> (<c>dotnet/artifacts/</c> by default, which <c>dotnet/.gitignore</c>
/// already ignores; override with the <c>POB_MODCACHE_SCORE</c> environment variable). A CI job
/// or a daily standup script reads that file; nobody has to grep test output.
/// </para>
/// <para>
/// Two denominators are reported, and they mean different things:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>linesTotal</c> (23,207) — every mod line in the cache. Scoring against this asks the port
/// to reproduce PoB exactly, including the 3,894 lines PoB itself leaves a remainder on.
/// </description></item>
/// <item><description>
/// <c>luaFullyParsedTotal</c> (19,313) — only the lines PoB consumes whole. This is the honest
/// "is the parser done" number: the other 3,894 are lines PoB does not fully parse either, and
/// they must not be counted against the port.
/// </description></item>
/// </list>
/// </remarks>
public sealed record ModCacheScorecard(
    bool ParserWired,
    int LinesMatched,
    int LinesMismatched,
    int LinesThrew,
    int LinesNotAttempted,
    int LinesTotal,
    int FullyParsedMatched,
    int FullyParsedTotal,
    int RemainderLinesMatched,
    int RemainderLinesTotal,
    int ModsMatched,
    int ModsTotal,
    IReadOnlyList<ModCacheShardScore> Shards)
{
    /// <summary>The one-line summary, in the shape the migration README quotes it.</summary>
    public string Headline =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"we parse {LinesMatched} / {LinesTotal} mod lines correctly ({Percent(LinesMatched, LinesTotal)}); "
            + $"{FullyParsedMatched} / {FullyParsedTotal} of the lines PoB itself parses whole "
            + $"({Percent(FullyParsedMatched, FullyParsedTotal)})");

    /// <summary>Where the scorecard is written.</summary>
    public static string OutputPath() =>
        Environment.GetEnvironmentVariable("POB_MODCACHE_SCORE") is { Length: > 0 } custom
            ? custom
            : Path.Combine(ModCacheOracle.SolutionDirectory(), "artifacts", "modcache-score.json");

    /// <summary>Replays the whole corpus and tallies it.</summary>
    public static ModCacheScorecard Measure(ModCacheCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        int matched = 0, mismatched = 0, threw = 0, notAttempted = 0;
        int fullMatched = 0, remainderMatched = 0, modsMatched = 0, modsTotal = 0;
        List<ModCacheShardScore> shards = [];

        foreach (string name in corpus.ShardNames)
        {
            ModCacheShardScore score = MeasureShard(corpus.Shard(name), name);
            shards.Add(score);

            matched += score.Matched;
            mismatched += score.Mismatched;
            threw += score.Threw;
            notAttempted += score.NotAttempted;
            fullMatched += score.FullyParsedMatched;
            remainderMatched += score.RemainderLinesMatched;
            modsMatched += score.ModsMatched;
            modsTotal += score.ModsTotal;
        }

        return new ModCacheScorecard(
            ParserWired: ModCacheOracle.Parser is not null,
            LinesMatched: matched,
            LinesMismatched: mismatched,
            LinesThrew: threw,
            LinesNotAttempted: notAttempted,
            LinesTotal: corpus.Entries.Count,
            FullyParsedMatched: fullMatched,
            FullyParsedTotal: corpus.Header.LuaFullyParsedCount,
            RemainderLinesMatched: remainderMatched,
            RemainderLinesTotal: corpus.Header.LuaRemainderCount,
            ModsMatched: modsMatched,
            ModsTotal: modsTotal,
            Shards: shards);
    }

    /// <summary>Replays one shard and tallies it.</summary>
    public static ModCacheShardScore MeasureShard(IReadOnlyList<ModCacheEntry> entries, string name)
    {
        ArgumentNullException.ThrowIfNull(entries);

        int matched = 0, mismatched = 0, threw = 0, notAttempted = 0;
        int fullMatched = 0, remainderMatched = 0, modsMatched = 0, modsTotal = 0;
        List<string> failures = [];

        foreach (ModCacheEntry entry in entries)
        {
            modsTotal += entry.Mods?.Count ?? 0;

            switch (ModCacheOracle.Evaluate(entry, out string? detail))
            {
                case ModCacheOutcome.Match:
                    matched++;
                    modsMatched += entry.Mods?.Count ?? 0;
                    if (entry.LuaParsedWholeLine)
                    {
                        fullMatched++;
                    }
                    else
                    {
                        remainderMatched++;
                    }

                    break;

                case ModCacheOutcome.Mismatch:
                    mismatched++;
                    Record(failures, entry, detail);
                    break;

                case ModCacheOutcome.Threw:
                    threw++;
                    Record(failures, entry, detail);
                    break;

                case ModCacheOutcome.NotWired:
                default:
                    notAttempted++;
                    break;
            }
        }

        return new ModCacheShardScore(
            name, matched, mismatched, threw, notAttempted, entries.Count,
            fullMatched, remainderMatched, modsMatched, modsTotal, failures);
    }

    /// <summary>Writes the scorecard as JSON and returns the path it went to.</summary>
    public string Write()
    {
        string path = OutputPath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        JsonWriterOptions options = new() { Indented = true };

        using (FileStream stream = File.Create(path))
        using (Utf8JsonWriter writer = new(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", 1);
            writer.WriteString("metric", "modcache-lines-reproduced");
            writer.WriteString("corpus", "dotnet/Pob.Tests/oracles/modcache.json");
            writer.WriteBoolean("parserWired", ParserWired);
            writer.WriteString("headline", Headline);

            writer.WriteStartObject("score");
            writer.WriteNumber("linesMatched", LinesMatched);
            writer.WriteNumber("linesTotal", LinesTotal);
            writer.WriteNumber("linesMismatched", LinesMismatched);
            writer.WriteNumber("linesThrew", LinesThrew);
            writer.WriteNumber("linesNotAttempted", LinesNotAttempted);
            writer.WriteNumber("luaFullyParsedMatched", FullyParsedMatched);
            writer.WriteNumber("luaFullyParsedTotal", FullyParsedTotal);
            writer.WriteNumber("luaRemainderMatched", RemainderLinesMatched);
            writer.WriteNumber("luaRemainderTotal", RemainderLinesTotal);
            writer.WriteNumber("modsMatched", ModsMatched);
            writer.WriteNumber("modsTotal", ModsTotal);
            writer.WriteNumber("floor", ModCacheOracle.MatchedLineFloor);
            writer.WriteEndObject();

            writer.WriteStartArray("shards");
            foreach (ModCacheShardScore shard in Shards)
            {
                writer.WriteStartObject();
                writer.WriteString("name", shard.Name);
                writer.WriteNumber("matched", shard.Matched);
                writer.WriteNumber("total", shard.Total);
                writer.WriteNumber("mismatched", shard.Mismatched);
                writer.WriteNumber("threw", shard.Threw);
                writer.WriteNumber("notAttempted", shard.NotAttempted);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return path;
    }

    private static void Record(List<string> failures, ModCacheEntry entry, string? detail)
    {
        const int Cap = 20;

        if (failures.Count < Cap)
        {
            failures.Add($"\"{entry.Line}\" -> {detail}");
        }
        else if (failures.Count == Cap)
        {
            failures.Add("... (further failures suppressed)");
        }
    }

    private static string Percent(int part, int whole) =>
        whole == 0
            ? "n/a"
            : ((double)part * 100.0 / whole).ToString("F2", CultureInfo.InvariantCulture) + "%";
}

/// <summary>One shard's tally.</summary>
public sealed record ModCacheShardScore(
    string Name,
    int Matched,
    int Mismatched,
    int Threw,
    int NotAttempted,
    int Total,
    int FullyParsedMatched,
    int RemainderLinesMatched,
    int ModsMatched,
    int ModsTotal,
    IReadOnlyList<string> Failures);
