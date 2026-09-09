using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Guards the ticket 01 constraints that the headless conformance oracles depend on:
/// Pob.Core carries no UI dependency, and the project reference graph is acyclic.
/// The checks read the .csproj files rather than compiled metadata, so they stay
/// meaningful while the projects are still empty and so they automatically cover
/// projects added by later tickets (Pob.Rendering in particular).
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string[] HeadlessProjects =
        ["Pob.Core", "Pob.Parsing", "Pob.Data", "Pob.Calc", "Pob.Platform"];

    private static readonly string[] UiPackagePrefixes =
        ["Avalonia", "SkiaSharp", "HarfBuzzSharp"];

    private static readonly string[] UiProjects =
        ["Pob.Rendering", "Pob.App"];

    [Fact]
    public void PobCore_HasNoUiDependencies()
    {
        Project core = LoadProjects()["Pob.Core"];

        Assert.Empty(core.ProjectReferences);
        Assert.Empty(core.PackageReferences);
    }

    [Fact]
    public void HeadlessProjects_DoNotDependOnUi()
    {
        Dictionary<string, Project> projects = LoadProjects();

        foreach (string name in HeadlessProjects)
        {
            Project project = projects[name];

            Assert.DoesNotContain(project.PackageReferences, IsUiPackage);
            Assert.DoesNotContain(Closure(projects, name), UiProjects.Contains);
        }
    }

    [Fact]
    public void HeadlessProjects_DoNotDependOnUi_AtRuntime()
    {
        // Belt and braces: the same claim, read off the compiled assemblies. Catches a
        // UI type arriving through something other than a direct reference.
        foreach (string name in HeadlessProjects)
        {
            string[] offenders = Assembly.Load(name)
                .GetReferencedAssemblies()
                .Select(static a => a.Name ?? string.Empty)
                .Where(IsUiPackage)
                .ToArray();

            Assert.Empty(offenders);
        }
    }

    [Fact]
    public void ReferenceGraph_IsAcyclic()
    {
        Dictionary<string, Project> projects = LoadProjects();

        Assert.NotEmpty(projects);

        HashSet<string> onStack = new(StringComparer.Ordinal);
        HashSet<string> finished = new(StringComparer.Ordinal);

        foreach (string name in projects.Keys)
        {
            Visit(projects, name, onStack, finished);
        }

        Assert.All(projects.Keys, name => Assert.Contains(name, finished));
    }

    private static void Visit(
        IReadOnlyDictionary<string, Project> projects,
        string name,
        HashSet<string> onStack,
        HashSet<string> finished)
    {
        if (finished.Contains(name))
        {
            return;
        }

        Assert.True(onStack.Add(name), $"Cycle in the project reference graph at {name}.");

        foreach (string reference in References(projects, name))
        {
            Visit(projects, reference, onStack, finished);
        }

        onStack.Remove(name);
        finished.Add(name);
    }

    /// <summary>Every project name transitively reachable from <paramref name="root"/>.</summary>
    private static HashSet<string> Closure(IReadOnlyDictionary<string, Project> projects, string root)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Queue<string> queue = new([root]);

        while (queue.Count > 0)
        {
            foreach (string reference in References(projects, queue.Dequeue()))
            {
                if (seen.Add(reference))
                {
                    queue.Enqueue(reference);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Direct project references of <paramref name="name"/>, or nothing if the project is
    /// not part of this solution.
    /// </summary>
    private static IReadOnlyList<string> References(IReadOnlyDictionary<string, Project> projects, string name) =>
        projects.TryGetValue(name, out Project? project) ? project.ProjectReferences : [];

    private static bool IsUiPackage(string id) =>
        UiPackagePrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));

    private static Dictionary<string, Project> LoadProjects()
    {
        DirectoryInfo root = SolutionDirectory();

        Dictionary<string, Project> projects = new(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            XDocument document = XDocument.Load(path);

            string[] projectReferences = document.Descendants("ProjectReference")
                .Select(static e => (string?)e.Attribute("Include") ?? string.Empty)
                .Select(static include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
                .Where(static name => name.Length > 0)
                .ToArray();

            string[] packageReferences = document.Descendants("PackageReference")
                .Select(static e => (string?)e.Attribute("Include") ?? string.Empty)
                .Where(static id => id.Length > 0)
                .ToArray();

            projects[Path.GetFileNameWithoutExtension(path)] = new Project(projectReferences, packageReferences);
        }

        return projects;
    }

    private static DirectoryInfo SolutionDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PathOfBuilding.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory;
    }

    private sealed record Project(IReadOnlyList<string> ProjectReferences, IReadOnlyList<string> PackageReferences);
}
