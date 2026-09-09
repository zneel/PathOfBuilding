using System.Text.Json;

namespace Pob.Tests.Infrastructure;

/// <summary>One randomly generated build, as the fuzz generator described it.</summary>
/// <param name="Index">1-based position in the corpus.</param>
/// <param name="Seed">The per-build RNG seed the master stream derived.</param>
/// <param name="ClassName">Character class.</param>
/// <param name="ClassId">Class id passed to <c>PassiveSpec:SelectClass</c>.</param>
/// <param name="AscendClassId">Ascendancy id, 0 for none.</param>
/// <param name="Level">Character level.</param>
/// <param name="NodeIds">Tree nodes to allocate, in allocation order.</param>
/// <param name="Items">Item texts, each already in PoB's item format.</param>
/// <param name="SocketGroups">Socket-group paste strings.</param>
/// <param name="ConfigVars">Config-tab variables the build sets.</param>
/// <param name="CustomMods">Free-text custom mods, newline separated.</param>
public sealed record FuzzRecipe(
    int Index,
    double Seed,
    string ClassName,
    int ClassId,
    int AscendClassId,
    int Level,
    IReadOnlyList<int> NodeIds,
    IReadOnlyList<string> Items,
    IReadOnlyList<string> SocketGroups,
    IReadOnlyList<string> ConfigVars,
    string CustomMods)
{
    /// <summary>
    /// Structural equality over the whole recipe, list contents included.
    /// </summary>
    /// <remarks>
    /// The compiler-generated record <c>Equals</c> is NOT this: it compares the list-valued
    /// members with the default equality comparer, which for <see cref="IReadOnlyList{T}"/> is
    /// reference equality, so two recipes read from two files never compare equal no matter
    /// what they contain. Anything checking that two corpora describe the same builds wants
    /// this method.
    /// </remarks>
    /// <param name="other">The recipe to compare against.</param>
    /// <returns><see langword="true"/> when every field matches.</returns>
    public bool Matches(FuzzRecipe other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Index == other.Index
            && Seed.Equals(other.Seed)
            && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
            && ClassId == other.ClassId
            && AscendClassId == other.AscendClassId
            && Level == other.Level
            && NodeIds.SequenceEqual(other.NodeIds)
            && Items.SequenceEqual(other.Items, StringComparer.Ordinal)
            && SocketGroups.SequenceEqual(other.SocketGroups, StringComparer.Ordinal)
            && ConfigVars.SequenceEqual(other.ConfigVars, StringComparer.Ordinal)
            && string.Equals(CustomMods, other.CustomMods, StringComparison.Ordinal);
    }
}

/// <summary>One build's result: the recipe, whether it calculated, and what it produced.</summary>
/// <param name="Recipe">The build description.</param>
/// <param name="Status">"ok", "partial" or "error".</param>
/// <param name="Notes">Engine failures recorded while applying the recipe.</param>
/// <param name="Outputs">Scalar <c>mainOutput</c> keys, empty when the build did not calculate.</param>
/// <param name="NonScalarOutputs">Output keys that held a table, recorded rather than flattened.</param>
public sealed record FuzzBuild(
    FuzzRecipe Recipe,
    string Status,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, ScalarValue> Outputs,
    IReadOnlyList<string> NonScalarOutputs);

/// <summary>
/// A corpus produced by <c>dotnet/tools/fuzz/generate.lua</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds live under <c>dotnet/tools/fuzz/corpus/</c>. A <c>plan</c> holds recipes only and
/// is generated without booting the engine, which is what lets it be produced under stock Lua
/// 5.1 as well as LuaJIT and so proves the generator itself is interpreter-independent. A
/// <c>corpus</c> holds the same recipes plus the reference engine's outputs for them.
/// </para>
/// <para>
/// The point of the outputs is the differential test that cannot be written yet: once
/// <c>Pob.Calc</c> exists (issues 22-27), the same recipes get replayed against it and the two
/// output sets get run through <see cref="CorpusDiff"/>. Until then this type exists so the
/// corpus is loaded, shape-checked and kept honest rather than sitting unread.
/// </para>
/// </remarks>
public sealed class FuzzCorpus
{
    private const int Format = 1;

    /// <summary>The command that regenerates a corpus, quoted in every failure message.</summary>
    public const string RegenerateCommand =
        "cd src && luajit ../dotnet/tools/fuzz/generate.lua --seed <seed> --count <n>";

    private FuzzCorpus(string kind, double seed, int count, string treeVersion, IReadOnlyList<FuzzBuild> builds)
    {
        Kind = kind;
        Seed = seed;
        Count = count;
        TreeVersion = treeVersion;
        Builds = builds;
    }

    /// <summary>"plan" (recipes only) or "corpus" (recipes plus engine outputs).</summary>
    public string Kind { get; }

    /// <summary>The master seed the corpus was generated from.</summary>
    public double Seed { get; }

    /// <summary>How many builds were requested.</summary>
    public int Count { get; }

    /// <summary>Passive tree version the recipes' node ids refer to.</summary>
    public string TreeVersion { get; }

    /// <summary>The builds, in generation order.</summary>
    public IReadOnlyList<FuzzBuild> Builds { get; }

    /// <summary>The committed corpus with engine outputs.</summary>
    /// <returns>The loaded corpus.</returns>
    public static FuzzCorpus LoadCorpus() =>
        Load(Path.Combine(RepoPaths.FuzzCorpus, "fuzz-corpus.json"));

    /// <summary>The committed recipes-only plan.</summary>
    /// <returns>The loaded plan.</returns>
    public static FuzzCorpus LoadPlan() =>
        Load(Path.Combine(RepoPaths.FuzzCorpus, "fuzz-plan.json"));

    /// <summary>Load a corpus from an explicit path.</summary>
    /// <param name="path">Absolute path to the JSON file.</param>
    /// <returns>The loaded corpus.</returns>
    public static FuzzCorpus Load(string path)
    {
        using JsonDocument document = CorpusReader.Open(path, Format, RegenerateCommand);
        JsonElement root = document.RootElement;

        List<FuzzBuild> builds = [];
        foreach (JsonElement element in root.GetProperty("builds").EnumerateArray())
        {
            builds.Add(ReadBuild(element));
        }

        return new FuzzCorpus(
            root.GetProperty("kind").GetString()!,
            CorpusReader.ReadNumber(root.GetProperty("seed")),
            CorpusReader.ReadInt32(root.GetProperty("count")),
            root.GetProperty("treeVersion").GetString()!,
            builds);
    }

    private static FuzzBuild ReadBuild(JsonElement element)
    {
        FuzzRecipe recipe = ReadRecipe(element.GetProperty("recipe"));

        List<string> notes = [];
        if (element.TryGetProperty("notes", out JsonElement notesElement))
        {
            notes.AddRange(notesElement.EnumerateArray().Select(static n => n.GetString()!));
        }

        Dictionary<string, ScalarValue> outputs = new(StringComparer.Ordinal);
        if (element.TryGetProperty("outputs", out JsonElement outputsElement))
        {
            foreach (JsonProperty property in outputsElement.EnumerateObject())
            {
                outputs[property.Name] = CorpusReader.ReadScalar(property.Value);
            }
        }

        List<string> nonScalar = [];
        if (element.TryGetProperty("nonScalarOutputs", out JsonElement nonScalarElement))
        {
            nonScalar.AddRange(nonScalarElement.EnumerateArray().Select(static n => n.GetString()!));
        }

        string status = element.TryGetProperty("status", out JsonElement statusElement)
            ? statusElement.GetString()!
            : "plan";

        return new FuzzBuild(recipe, status, notes, outputs, nonScalar);
    }

    private static FuzzRecipe ReadRecipe(JsonElement element) => new(
        CorpusReader.ReadInt32(element.GetProperty("index")),
        CorpusReader.ReadNumber(element.GetProperty("seed")),
        element.GetProperty("className").GetString()!,
        CorpusReader.ReadInt32(element.GetProperty("classId")),
        CorpusReader.ReadInt32(element.GetProperty("ascendClassId")),
        CorpusReader.ReadInt32(element.GetProperty("level")),
        [.. element.GetProperty("nodes").EnumerateArray().Select(CorpusReader.ReadInt32)],
        [.. element.GetProperty("items").EnumerateArray().Select(static i => i.GetProperty("raw").GetString()!)],
        [.. element.GetProperty("socketGroups").EnumerateArray().Select(static g => g.GetString()!)],
        [.. element.GetProperty("config").EnumerateArray().Select(static c => c.GetProperty("var").GetString()!)],
        element.GetProperty("customMods").GetString()!);
}
