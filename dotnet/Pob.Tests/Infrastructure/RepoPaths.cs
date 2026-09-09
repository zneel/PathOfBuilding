namespace Pob.Tests.Infrastructure;

/// <summary>
/// Locates the repository on disk from a test assembly's output directory.
/// </summary>
/// <remarks>
/// <para>
/// Every conformance oracle in this project reads a corpus that a Lua script produced next to
/// the Lua sources, so every one of them needs the repository root rather than
/// <see cref="AppContext.BaseDirectory"/>. Doing that walk once, here, keeps the oracle suites
/// from each growing their own copy that drifts (ticket 08 exists partly to stop three
/// concurrent oracle tickets landing three different answers to "where is the repo?").
/// </para>
/// <para>
/// The anchor is <c>dotnet/PathOfBuilding.sln</c>. Anchoring on the solution rather than on
/// <c>.git</c> means the walk still works from a git worktree, a source archive, or a
/// submodule, none of which have a <c>.git</c> directory in the expected shape.
/// </para>
/// </remarks>
public static class RepoPaths
{
    private static readonly Lazy<string> LazySolutionDirectory = new(FindSolutionDirectory, isThreadSafe: true);

    /// <summary>The <c>dotnet/</c> directory, which holds <c>PathOfBuilding.sln</c>.</summary>
    public static string SolutionDirectory => LazySolutionDirectory.Value;

    /// <summary>The repository root: the parent of <see cref="SolutionDirectory"/>.</summary>
    public static string RepositoryRoot =>
        Directory.GetParent(SolutionDirectory)?.FullName
        ?? throw new InvalidOperationException($"{SolutionDirectory} has no parent directory");

    /// <summary>The Lua source tree.</summary>
    public static string Src => Path.Combine(RepositoryRoot, "src");

    /// <summary>The busted spec suite that ticket 08 inventories.</summary>
    public static string SpecSystem => Path.Combine(RepositoryRoot, "spec", "System");

    /// <summary>Corpora produced by <c>dotnet/tools/fuzz/generate.lua</c>.</summary>
    public static string FuzzCorpus => Path.Combine(SolutionDirectory, "tools", "fuzz", "corpus");

    /// <summary>Corpora produced by the oracle dump scripts (tickets 02, 05, 06, 07).</summary>
    public static string Oracles => Path.Combine(SolutionDirectory, "Pob.Tests", "oracles");

    /// <summary>This directory: shared test infrastructure and the data it owns.</summary>
    public static string TestInfrastructure => Path.Combine(SolutionDirectory, "Pob.Tests", "Infrastructure");

    /// <summary>
    /// Assert a corpus file exists, with a message that names the script that regenerates it.
    /// A missing corpus is the single most common way these suites fail, and "file not found"
    /// on its own does not tell anyone what to run.
    /// </summary>
    /// <param name="path">Absolute path to the expected file.</param>
    /// <param name="regenerateWith">The command that produces it.</param>
    /// <returns><paramref name="path"/>, unchanged.</returns>
    public static string RequireFile(string path, string regenerateWith)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Corpus missing at {path}. Regenerate it with: {regenerateWith}",
                path);
        }

        return path;
    }

    private static string FindSolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PathOfBuilding.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                $"No PathOfBuilding.sln found walking up from {AppContext.BaseDirectory}");
    }
}
