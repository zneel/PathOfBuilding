using System.Text.RegularExpressions;

namespace Pob.Tests.Infrastructure;

/// <summary>One <c>it("...")</c> block found in a busted spec file.</summary>
/// <param name="Name">The test name as written.</param>
/// <param name="Line">1-based line the <c>it(</c> appears on.</param>
/// <param name="OutputKeys">Engine output keys the block reads, sorted ordinally.</param>
/// <param name="ModQueries">Mod names it queries through a ModStore call, sorted ordinally.</param>
public sealed record SpecTest(string Name, int Line, IReadOnlyList<string> OutputKeys, IReadOnlyList<string> ModQueries);

/// <summary>The mechanically extractable facts about one busted spec file.</summary>
/// <param name="File">Repository-relative path.</param>
/// <param name="Loc">Line count.</param>
/// <param name="Describes">Every <c>describe("...")</c> label, in file order.</param>
/// <param name="Tests">Every <c>it("...")</c> block, in file order.</param>
/// <param name="OutputKeys">Union of the blocks' output keys.</param>
/// <param name="ModQueries">Union of the blocks' mod queries.</param>
/// <param name="Apis">Which engine surfaces the file touches, sorted ordinally.</param>
public sealed record SpecFacts(
    string File,
    int Loc,
    IReadOnlyList<string> Describes,
    IReadOnlyList<SpecTest> Tests,
    IReadOnlyList<string> OutputKeys,
    IReadOnlyList<string> ModQueries,
    IReadOnlyList<string> Apis);

/// <summary>
/// Extracts the mechanical half of the spec inventory straight from <c>spec/System</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Infrastructure/spec-inventory.json</c> (ticket 08) is half machine output -- test names,
/// line numbers, output keys, which engine surfaces a file touches -- and half human review:
/// which migration issue has to land before the file can be ported, and why. The human half
/// cannot be regenerated. The machine half can, and if it were not, the inventory would rot
/// the first time somebody added a test.
/// </para>
/// <para>
/// So this scanner IS the regeneration path, and <c>SpecInventoryTests</c> fails the build when
/// what it finds and what is committed disagree. That is deliberately stricter than a warning:
/// the inventory's whole job is to tell the engine tickets what they must satisfy, and a stale
/// one understates the work.
/// </para>
/// <para>
/// Regex over Lua source rather than a parser, for the same reason
/// <c>dotnet/tools/luacompat-oracle/dump.lua</c> extracts function bodies by text: the
/// alternative is a Lua parser in the test project to read four constructs. The patterns are
/// deliberately narrow, and the counts they produce are asserted against, so a pattern that
/// stopped matching shows up as a failure rather than as a quietly shrinking inventory.
/// </para>
/// </remarks>
public static partial class SpecScanner
{
    /// <summary>
    /// Every engine surface the inventory tracks, and how it is recognised in the source.
    /// Order matters only in that the emitted list is sorted ordinally afterwards.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] ApiPatterns =
    [
        ("buildXml", BuildXmlPattern()),
        ("calcs", CalcsPattern()),
        ("calcsTab", CalcsTabPattern()),
        ("commonLib", CommonLibPattern()),
        ("configTab", ConfigTabPattern()),
        ("frameLoop", FrameLoopPattern()),
        ("importTab", ImportTabPattern()),
        ("item", ItemPattern()),
        ("itemsTab", ItemsTabPattern()),
        ("modParser", ModParserPattern()),
        ("modStore", ModStorePattern()),
        ("passiveSpec", PassiveSpecPattern()),
        ("skillsTab", SkillsTabPattern()),
        ("tradeHttp", TradeHttpPattern()),
        ("treeTab", TreeTabPattern()),
        ("uiControls", UiControlsPattern()),
    ];

    /// <summary>Scan every <c>*_spec.lua</c> under <c>spec/System</c>, ordered by file name.</summary>
    /// <returns>One record per spec file.</returns>
    public static IReadOnlyList<SpecFacts> ScanAll()
    {
        string root = RepoPaths.SpecSystem;
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"spec suite missing at {root}");
        }

        return
        [
            .. Directory.EnumerateFiles(root, "*_spec.lua")
                .Order(StringComparer.Ordinal)
                .Select(Scan)
        ];
    }

    /// <summary>Scan one spec file.</summary>
    /// <param name="path">Absolute path to the file.</param>
    /// <returns>Its extractable facts.</returns>
    public static SpecFacts Scan(string path)
    {
        // Normalise line endings before splitting so the line count and the line numbers are
        // the same on a CRLF checkout as on an LF one.
        string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string[] lines = text.Split('\n');

        List<string> describes = [.. DescribePattern().Matches(text).Select(static m => m.Groups[1].Value)];

        // Where each `it(` starts. A block runs until the next one, or to end of file; that
        // over-reads the tail of the last block in a nested describe, which is harmless here
        // because the keys collected are only ever a superset used for triage.
        List<(string Name, int Line)> marks = [];
        for (int i = 0; i < lines.Length; i++)
        {
            Match match = ItPattern().Match(lines[i]);
            if (match.Success)
            {
                marks.Add((match.Groups[1].Value, i));
            }
        }

        List<SpecTest> tests = [];
        SortedSet<string> fileKeys = new(StringComparer.Ordinal);
        SortedSet<string> fileMods = new(StringComparer.Ordinal);

        for (int i = 0; i < marks.Count; i++)
        {
            int start = marks[i].Line;
            int end = i + 1 < marks.Count ? marks[i + 1].Line : lines.Length;
            string body = string.Join('\n', lines[start..end]);

            SortedSet<string> keys = new(StringComparer.Ordinal);
            foreach (Match m in OutputAttrPattern().Matches(body))
            {
                keys.Add(m.Groups[1].Value);
            }

            foreach (Match m in OutputIndexPattern().Matches(body))
            {
                keys.Add(m.Groups[1].Value);
            }

            SortedSet<string> mods = new(StringComparer.Ordinal);
            foreach (Match m in ModQueryPattern().Matches(body))
            {
                mods.Add(m.Groups[1].Value);
            }

            fileKeys.UnionWith(keys);
            fileMods.UnionWith(mods);
            tests.Add(new SpecTest(marks[i].Name, start + 1, [.. keys], [.. mods]));
        }

        List<string> apis = [.. ApiPatterns.Where(p => p.Pattern.IsMatch(text)).Select(static p => p.Name).Order(StringComparer.Ordinal)];

        return new SpecFacts(
            $"spec/System/{Path.GetFileName(path)}",
            lines.Length,
            describes,
            tests,
            [.. fileKeys],
            [.. fileMods],
            apis);
    }

    [GeneratedRegex(@"describe\(\s*""([^""]*)""")]
    private static partial Regex DescribePattern();

    [GeneratedRegex(@"\bit\(\s*""([^""]*)""")]
    private static partial Regex ItPattern();

    // `build.calcsTab.mainOutput.X`, `luckyOutput.X`, `env.player.output.X`: any identifier
    // ending in "output"/"Output", then a field.
    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*?[Oo]utput\.([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex OutputAttrPattern();

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*?[Oo]utput\[\s*""([^""]+)""")]
    private static partial Regex OutputIndexPattern();

    // `modDB:Sum("BASE", nil, "Multiplier:PermanentMinion")` and its siblings; the third
    // argument is the mod name being asked for.
    [GeneratedRegex(@":(?:Sum|More|Flag|Override|GetMultiplier|GetStat)\(\s*(?:""[A-Z]+""|nil)\s*,\s*[^,]+,\s*""([^""]+)""")]
    private static partial Regex ModQueryPattern();

    [GeneratedRegex(@"build\.skillsTab[:.]")]
    private static partial Regex SkillsTabPattern();

    [GeneratedRegex(@"build\.itemsTab[:.]")]
    private static partial Regex ItemsTabPattern();

    [GeneratedRegex(@"build\.configTab[:.]")]
    private static partial Regex ConfigTabPattern();

    [GeneratedRegex(@"build\.treeTab[:.]")]
    private static partial Regex TreeTabPattern();

    [GeneratedRegex(@"build\.spec[:.]")]
    private static partial Regex PassiveSpecPattern();

    [GeneratedRegex(@"build\.calcsTab[:.]")]
    private static partial Regex CalcsTabPattern();

    [GeneratedRegex(@"build\.importTab[:.]")]
    private static partial Regex ImportTabPattern();

    [GeneratedRegex(@"modDB[:.]|:Sum\(|:More\(|:Flag\(|:Override\(")]
    private static partial Regex ModStorePattern();

    [GeneratedRegex(@"\bcalcs[.:]")]
    private static partial Regex CalcsPattern();

    [GeneratedRegex(@"\bmodLib[.:]")]
    private static partial Regex ModParserPattern();

    [GeneratedRegex(@"\bitemLib[.:]|new\(""Item""\)|CreateDisplayItemFromRaw")]
    private static partial Regex ItemPattern();

    [GeneratedRegex(@"loadBuildFromXML|loadBuildFromJSON")]
    private static partial Regex BuildXmlPattern();

    [GeneratedRegex(@"runCallback")]
    private static partial Regex FrameLoopPattern();

    [GeneratedRegex(@"\.controls\.")]
    private static partial Regex UiControlsPattern();

    [GeneratedRegex(@"TradeQuery|PoEAPI|SearchHost|DownloadPage|RateLimiter")]
    private static partial Regex TradeHttpPattern();

    [GeneratedRegex(@"\bcommon\.|\bround\(|\bfloor\(|naturalSortCompare|formatNumSep")]
    private static partial Regex CommonLibPattern();
}
