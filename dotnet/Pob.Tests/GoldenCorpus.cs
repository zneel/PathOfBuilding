using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Pob.Tests;

/// <summary>
/// The golden output corpus: every build's input XML paired with every value the Lua engine
/// produced for it (migration ticket 07).
/// </summary>
/// <remarks>
/// <para>
/// Produced by <c>dotnet/tools/golden-corpus/generate.lua</c>, which builds each character
/// through the real engine's object API, serialises it, reloads it from that serialisation
/// and captures <c>MAIN</c> and <c>CALCS</c> mode outputs for the player, minion and enemy
/// actors. Regenerate with <c>luajit ../dotnet/tools/golden-corpus/generate.lua</c> from
/// <c>src/</c>.
/// </para>
/// <para>
/// Numbers are stored at full <c>%.17g</c> precision, never rounded. The Lua project's own
/// goldens (<c>spec/TestBuilds/3.13/*.lua</c>) round to four decimal places, which is enough
/// to catch a broken formula and not enough to ever tighten the comparison to bit-exact.
/// Rounding at generation time cannot be undone, so this corpus does not do it - see
/// <see cref="GoldenTolerance"/> for the ladder that spends that precision.
/// </para>
/// </remarks>
public static class GoldenCorpus
{
    /// <summary>Sentinels the generator writes for values JSON has no spelling for.</summary>
    private const string NanSentinel = "__nan";
    private const string PositiveInfinitySentinel = "__inf";
    private const string NegativeInfinitySentinel = "__-inf";

    private static readonly Lazy<string> CorpusDirectory = new(FindCorpusDirectory, isThreadSafe: true);
    private static readonly Lazy<ImmutableArray<string>> CorpusFiles = new(FindCorpusFiles, isThreadSafe: true);

    /// <summary>Absolute path of <c>Pob.Tests/oracles/golden</c>.</summary>
    public static string Directory => CorpusDirectory.Value;

    /// <summary>Every golden file on disk, sorted by build name.</summary>
    public static ImmutableArray<string> Files => CorpusFiles.Value;

    /// <summary>Build names, sorted, derived from the file names.</summary>
    public static ImmutableArray<string> Names =>
        [.. Files.Select(static path => BuildNameOf(path))];

    /// <summary>Reads the corpus manifest, which describes the generation run as a whole.</summary>
    public static GoldenManifest LoadManifest()
    {
        string path = Path.Combine(Directory, "manifest.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"golden corpus manifest missing at {path}; regenerate with " +
                "`luajit ../dotnet/tools/golden-corpus/generate.lua` from src/",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        List<GoldenManifestEntry> builds = [];
        foreach (JsonElement entry in root.GetProperty("builds").EnumerateArray())
        {
            builds.Add(new GoldenManifestEntry(
                Name: entry.GetProperty("name").GetString()!,
                Group: entry.GetProperty("group").GetString()!,
                KeyCount: entry.GetProperty("keyCount").GetInt32(),
                SectionCount: entry.TryGetProperty("sectionCount", out JsonElement sections) ? sections.GetInt32() : 0,
                TotalDps: entry.GetProperty("totalDps").GetDouble(),
                FullDps: entry.GetProperty("fullDps").GetDouble(),
                Life: entry.GetProperty("life").GetDouble()));
        }

        List<string> failures = [];
        foreach (JsonElement failure in root.GetProperty("failures").EnumerateArray())
        {
            failures.Add(
                failure.GetProperty("name").GetString() + ": " + failure.GetProperty("error").GetString());
        }

        return new GoldenManifest(
            Schema: root.GetProperty("schema").GetInt32(),
            TreeVersion: root.GetProperty("treeVersion").GetString()!,
            BuildCount: root.GetProperty("buildCount").GetInt32(),
            ZeroDpsCount: root.GetProperty("zeroDpsCount").GetInt32(),
            UnparsedModLineCount: root.GetProperty("unparsedModLineCount").GetInt32(),
            Groups: root.GetProperty("groups").EnumerateObject()
                .ToImmutableDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal),
            Builds: [.. builds],
            Failures: [.. failures]);
    }

    /// <summary>Loads one build by name.</summary>
    public static GoldenBuild Load(string name)
    {
        string? path = Files.FirstOrDefault(p => string.Equals(BuildNameOf(p), name, StringComparison.Ordinal));
        if (path is null)
        {
            throw new FileNotFoundException($"no golden build named '{name}' under {Directory}");
        }

        return LoadFile(path);
    }

    /// <summary>Loads one build from an explicit file path, gzipped or not.</summary>
    public static GoldenBuild LoadFile(string path)
    {
        using Stream stream = OpenGoldenFile(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        int schema = root.GetProperty("schema").GetInt32();
        if (schema != 1)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} declares schema {schema}; this harness understands 1");
        }

        Dictionary<string, GoldenSection> sections = new(StringComparer.Ordinal);
        foreach (JsonProperty section in root.GetProperty("sections").EnumerateObject())
        {
            Dictionary<string, GoldenValue> values = new(StringComparer.Ordinal);
            foreach (JsonProperty value in section.Value.EnumerateObject())
            {
                values[value.Name] = ReadValue(value.Value);
            }

            sections[section.Name] = new GoldenSection(section.Name, values);
        }

        JsonElement notes = root.GetProperty("notes");

        return new GoldenBuild(
            Name: root.GetProperty("name").GetString()!,
            Group: root.GetProperty("group").GetString()!,
            InputXml: root.GetProperty("inputXml").GetString()!,
            TreeVersion: notes.GetProperty("treeVersion").GetString()!,
            KeyCount: notes.GetProperty("keyCount").GetInt32(),
            Sections: sections);
    }

    /// <summary>Opens a corpus file, transparently decompressing the gzipped form.</summary>
    /// <remarks>
    /// The generator gzips the corpus because 270 builds of full-precision output is ~26 MB of
    /// JSON and ~3 MB compressed, and a repository is a bad place for 26 MB of generated text.
    /// It leaves the files plain when <c>gzip</c> is not on the PATH, so both shapes load.
    /// </remarks>
    public static Stream OpenGoldenFile(string path)
    {
        FileStream file = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(file, CompressionMode.Decompress);
        }

        return file;
    }

    /// <summary>Strips <c>.golden.json</c> / <c>.golden.json.gz</c> off a corpus file name.</summary>
    public static string BuildNameOf(string path)
    {
        string name = Path.GetFileName(path);
        const string GzSuffix = ".golden.json.gz";
        const string JsonSuffix = ".golden.json";
        if (name.EndsWith(GzSuffix, StringComparison.Ordinal))
        {
            return name[..^GzSuffix.Length];
        }

        return name.EndsWith(JsonSuffix, StringComparison.Ordinal) ? name[..^JsonSuffix.Length] : name;
    }

    private static GoldenValue ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => GoldenValue.Number(element.GetDouble()),
        JsonValueKind.True => GoldenValue.Boolean(true),
        JsonValueKind.False => GoldenValue.Boolean(false),
        JsonValueKind.String => ReadString(element.GetString()!),
        _ => throw new InvalidOperationException($"golden value of unsupported kind {element.ValueKind}"),
    };

    private static GoldenValue ReadString(string text) => text switch
    {
        NanSentinel => GoldenValue.Number(double.NaN),
        PositiveInfinitySentinel => GoldenValue.Number(double.PositiveInfinity),
        NegativeInfinitySentinel => GoldenValue.Number(double.NegativeInfinity),
        _ => GoldenValue.Text(text),
    };

    private static ImmutableArray<string> FindCorpusFiles()
    {
        string directory = Directory;
        List<string> files =
        [
            .. System.IO.Directory.EnumerateFiles(directory, "*.golden.json.gz"),
            .. System.IO.Directory.EnumerateFiles(directory, "*.golden.json"),
        ];

        // A stale uncompressed file left beside its gzipped replacement would otherwise be
        // replayed as a second, older copy of the same build.
        HashSet<string> gzipped = files
            .Where(static p => p.EndsWith(".gz", StringComparison.Ordinal))
            .Select(BuildNameOf)
            .ToHashSet(StringComparer.Ordinal);

        files.RemoveAll(p => !p.EndsWith(".gz", StringComparison.Ordinal) && gzipped.Contains(BuildNameOf(p)));
        files.Sort(StringComparer.Ordinal);
        return [.. files];
    }

    private static string FindCorpusDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PathOfBuilding.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"could not find PathOfBuilding.sln above {AppContext.BaseDirectory}");
        }

        return Path.Combine(directory.FullName, "Pob.Tests", "oracles", "golden");
    }
}

/// <summary>One value out of an actor's output table: a number, a boolean or a string.</summary>
/// <remarks>
/// Lua has one numeric type, so every number here is a <see cref="double"/> and any use of
/// <see cref="decimal"/> against this corpus would be a bug rather than a precision
/// improvement. Booleans and strings are in the outputs too (<c>AnyBypass</c>,
/// <c>ManaCostWarningList/1</c>) and must compare exactly.
/// </remarks>
public readonly record struct GoldenValue
{
    private GoldenValue(GoldenValueKind kind, double number, bool boolean, string? text)
    {
        Kind = kind;
        NumberValue = number;
        BooleanValue = boolean;
        StringValue = text;
    }

    public GoldenValueKind Kind { get; }

    public double NumberValue { get; }

    public bool BooleanValue { get; }

    public string? StringValue { get; }

    public static GoldenValue Number(double value) => new(GoldenValueKind.Number, value, false, null);

    public static GoldenValue Boolean(bool value) => new(GoldenValueKind.Boolean, 0, value, null);

    public static GoldenValue Text(string value) => new(GoldenValueKind.Text, 0, false, value);

    /// <summary>
    /// True when this is a number the engine produced as a whole number - the values the
    /// tolerance ladder holds to exact equality.
    /// </summary>
    public bool IsIntegral =>
        Kind == GoldenValueKind.Number
        && double.IsFinite(NumberValue)
        && NumberValue == Math.Truncate(NumberValue)
        && Math.Abs(NumberValue) < 9007199254740992d;

    public override string ToString() => Kind switch
    {
        GoldenValueKind.Number => NumberValue.ToString("R", CultureInfo.InvariantCulture),
        GoldenValueKind.Boolean => BooleanValue ? "true" : "false",
        _ => "\"" + StringValue + "\"",
    };
}

public enum GoldenValueKind
{
    Number,
    Boolean,
    Text,
}

/// <summary>One flattened actor output table, e.g. <c>MAIN/player</c>.</summary>
public sealed record GoldenSection(string Name, IReadOnlyDictionary<string, GoldenValue> Values);

/// <summary>One golden build: the input the engine was given and everything it produced.</summary>
public sealed record GoldenBuild(
    string Name,
    string Group,
    string InputXml,
    string TreeVersion,
    int KeyCount,
    IReadOnlyDictionary<string, GoldenSection> Sections);

/// <summary>Corpus-level description of one generation run.</summary>
public sealed record GoldenManifest(
    int Schema,
    string TreeVersion,
    int BuildCount,
    int ZeroDpsCount,
    int UnparsedModLineCount,
    ImmutableDictionary<string, int> Groups,
    ImmutableArray<GoldenManifestEntry> Builds,
    ImmutableArray<string> Failures);

/// <summary>The manifest's summary of one build.</summary>
public sealed record GoldenManifestEntry(
    string Name,
    string Group,
    int KeyCount,
    int SectionCount,
    double TotalDps,
    double FullDps,
    double Life);
