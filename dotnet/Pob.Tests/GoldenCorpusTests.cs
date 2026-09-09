using System.Globalization;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Validates the golden corpus itself: that it is there, that it is big enough to be worth
/// trusting, and that every file has the shape the replay harness expects
/// (migration ticket 07).
/// </summary>
/// <remarks>
/// These are not engine tests - <c>Pob.Calc</c> is tickets 21-27 and does not exist yet.
/// They are the guard that stops the corpus rotting between now and then: a truncated
/// regeneration, a schema bump, or a build that silently stopped producing minion output
/// fails here rather than three months later in the middle of porting CalcOffence.
/// </remarks>
public sealed class GoldenCorpusTests
{
    private static readonly Lazy<GoldenManifest> Manifest = new(GoldenCorpus.LoadManifest, isThreadSafe: true);

    public static TheoryData<string> BuildFiles
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string path in GoldenCorpus.Files)
            {
                data.Add(path);
            }

            return data;
        }
    }

    /// <summary>
    /// The ticket asks for at least 200 builds. Five - what the Lua project has had since
    /// 3.13 - is not a corpus for a 43k-line engine, and the number is the whole point of
    /// the expansion, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void Corpus_HasAtLeastTwoHundredBuilds()
    {
        Assert.True(GoldenCorpus.Files.Length >= 200,
            $"golden corpus has {GoldenCorpus.Files.Length} builds under {GoldenCorpus.Directory}; " +
            "regenerate with `luajit ../dotnet/tools/golden-corpus/generate.lua` from src/");
    }

    /// <summary>The manifest and the directory listing have to describe the same corpus.</summary>
    [Fact]
    public void Manifest_AgreesWithTheFilesOnDisk()
    {
        GoldenManifest manifest = Manifest.Value;

        Assert.Equal(1, manifest.Schema);
        Assert.Equal(manifest.BuildCount, manifest.Builds.Length);
        Assert.Equal(manifest.BuildCount, GoldenCorpus.Files.Length);

        HashSet<string> onDisk = [.. GoldenCorpus.Names];
        List<string> missing = [.. manifest.Builds.Select(static b => b.Name).Where(name => !onDisk.Contains(name))];
        Assert.True(missing.Count == 0, $"manifest lists builds with no file: {string.Join(", ", missing.Take(10))}");

        Assert.Equal(manifest.BuildCount, manifest.Groups.Values.Sum());
    }

    /// <summary>
    /// A build the generator could not calculate is recorded, not dropped - so an empty
    /// failure list is a real statement about the run, and a non-empty one has to be read.
    /// </summary>
    [Fact]
    public void Manifest_RecordsNoFailedBuilds()
    {
        Assert.Empty(Manifest.Value.Failures);
    }

    /// <summary>
    /// A corpus that only exercises one class or one skill would pass every other test here
    /// and be worthless. The groups are what make it a matrix.
    /// </summary>
    [Fact]
    public void Corpus_CoversEveryPlannedDimension()
    {
        GoldenManifest manifest = Manifest.Value;

        foreach (string group in new[] { "class-ascendancy", "keystone", "skill", "weapon", "defence", "config", "mechanic", "legacy" })
        {
            Assert.True(manifest.Groups.TryGetValue(group, out int count) && count > 0,
                $"corpus has no builds in the '{group}' group; groups present: " +
                string.Join(", ", manifest.Groups.Select(p => $"{p.Key}={p.Value}")));
        }

        // Every class/ascendancy pair on the tree: seven classes, three ascendancies each.
        Assert.Equal(21, manifest.Groups["class-ascendancy"]);

        // Every keystone on the tree gets its own build; the tree has had 40+ for years, so a
        // sudden collapse to a handful means the enumeration broke, not that GGG deleted them.
        Assert.True(manifest.Groups["keystone"] >= 40,
            $"only {manifest.Groups["keystone"]} keystone builds");
    }

    /// <summary>
    /// Mod text that the engine fails to parse would apply nothing, and the affected build's
    /// golden numbers would be quietly wrong rather than obviously wrong. The generator counts
    /// them; the count has to stay zero.
    /// </summary>
    [Fact]
    public void Corpus_HasNoUnparsedModLines()
    {
        Assert.Equal(0, Manifest.Value.UnparsedModLineCount);
    }

    /// <summary>
    /// Every file: schema, a real build XML, the sections the ticket names, and a key count
    /// that agrees with the manifest. Sharded per build so a regression names the build.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuildFiles))]
    public void Build_HasTheExpectedShape(string path)
    {
        GoldenBuild build = GoldenCorpus.LoadFile(path);

        Assert.Equal(GoldenCorpus.BuildNameOf(path), build.Name);
        Assert.False(string.IsNullOrWhiteSpace(build.Group));
        Assert.StartsWith("<?xml", build.InputXml, StringComparison.Ordinal);
        Assert.Contains("<PathOfBuilding>", build.InputXml, StringComparison.Ordinal);

        // The ticket asks for both modes and for the player and enemy actors. `minion` is
        // present only when the build's main skill summons something, so it is checked
        // corpus-wide rather than per build.
        foreach (string section in new[] { "MAIN/player", "MAIN/enemy", "CALCS/player", "CALCS/enemy" })
        {
            Assert.True(build.Sections.ContainsKey(section),
                $"{build.Name} has no '{section}' section; it has: {string.Join(", ", build.Sections.Keys.Order(StringComparer.Ordinal))}");
        }

        int keyCount = build.Sections.Values.Sum(static s => s.Values.Count);
        Assert.Equal(build.KeyCount, keyCount);

        // The Lua project's five goldens carry ~474 keys of player output each. Anything that
        // drops to a couple of hundred keys means the capture path broke, not that the build
        // got simpler.
        Assert.True(build.Sections["MAIN/player"].Values.Count >= 400,
            $"{build.Name} MAIN/player has only {build.Sections["MAIN/player"].Values.Count} keys");

        GoldenManifestEntry? entry = Manifest.Value.Builds.FirstOrDefault(e => string.Equals(e.Name, build.Name, StringComparison.Ordinal));
        Assert.NotNull(entry);
        Assert.Equal(entry.KeyCount, keyCount);
    }

    /// <summary>
    /// The minion actor is a whole calculation path of its own (<c>CalcSetup</c> builds a
    /// second actor with its own mod database), so the corpus has to contain builds that
    /// produce one at all.
    /// </summary>
    [Fact]
    public void Corpus_ContainsBuildsWithMinionOutput()
    {
        int withMinions = GoldenCorpus.Files.Count(static path =>
            GoldenCorpus.LoadFile(path).Sections.ContainsKey("MAIN/minion"));

        Assert.True(withMinions >= 5,
            $"only {withMinions} builds produce minion output; the minion actor would be effectively untested");
    }

    /// <summary>
    /// Reports the scale of the corpus. Not an assertion about a threshold - a printed number
    /// that tells whoever runs the suite what the oracle is actually worth.
    /// </summary>
    [Fact]
    public void Corpus_ReportsItsScale()
    {
        GoldenManifest manifest = Manifest.Value;

        long numbers = 0, booleans = 0, strings = 0, total = 0;
        long bytes = 0;
        foreach (string path in GoldenCorpus.Files)
        {
            bytes += new FileInfo(path).Length;
            GoldenBuild build = GoldenCorpus.LoadFile(path);
            foreach (GoldenSection section in build.Sections.Values)
            {
                foreach (GoldenValue value in section.Values.Values)
                {
                    total++;
                    switch (value.Kind)
                    {
                        case GoldenValueKind.Number: numbers++; break;
                        case GoldenValueKind.Boolean: booleans++; break;
                        default: strings++; break;
                    }
                }
            }
        }

        string summary = string.Create(CultureInfo.InvariantCulture,
            $"""
             golden corpus: {manifest.BuildCount} builds, tree {manifest.TreeVersion}, {bytes / 1024} KiB on disk
               values : {total} ({numbers} numeric, {booleans} boolean, {strings} string)
               groups : {string.Join(", ", manifest.Groups.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"))}
               builds with no damage output: {manifest.ZeroDpsCount} (auras, curses, warcries, guard skills)
             """);
        TestContext.Current.TestOutputHelper?.WriteLine(summary);

        Assert.True(total > 100_000, $"corpus holds only {total} values");
    }
}
