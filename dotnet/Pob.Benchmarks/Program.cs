using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Pob.Benchmarks;

/// <summary>Entry point for the benchmark harness.</summary>
public static class Program
{
    /// <summary>
    /// Hands control to BenchmarkDotNet's switcher, so every benchmark class in the assembly is
    /// selectable from the command line without this file needing to know about it.
    /// </summary>
    /// <param name="args">
    /// Passed straight through, e.g. <c>--filter '*' --job short</c>. With no arguments the
    /// switcher prompts interactively.
    /// </param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BuildConfig());

    /// <summary>
    /// The stock configuration, with the artifacts directory pinned to this project rather than
    /// to the working directory.
    /// </summary>
    /// <remarks>
    /// BenchmarkDotNet defaults to <c>./BenchmarkDotNet.Artifacts</c> relative to wherever the
    /// process was started, which for the usual invocation means dropping a directory of
    /// reports into <c>dotnet/</c>. Pinning it keeps the reports next to the project that
    /// produced them and inside the <c>.gitignore</c> that covers them.
    /// </remarks>
    private static ManualConfig BuildConfig() =>
        DefaultConfig.Instance.WithArtifactsPath(Path.Combine(ProjectDirectory(), "BenchmarkDotNet.Artifacts"));

    private static string ProjectDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pob.Benchmarks.csproj")))
        {
            directory = directory.Parent;
        }

        // Running from a published output rather than from bin/: fall back to next to the
        // executable, which is at worst untidy.
        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
