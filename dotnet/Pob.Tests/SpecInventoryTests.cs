using System.Globalization;
using System.Text;
using Pob.Tests.Infrastructure;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// Keeps <c>Infrastructure/spec-inventory.json</c> honest about <c>spec/System</c>.
/// </summary>
/// <remarks>
/// The inventory is the ticket-08 deliverable that says which of the busted spec files each
/// engine ticket unblocks. Its value is entirely in being current: a stale inventory quietly
/// understates what issues 22-27 have to satisfy. So the machine-extractable half is re-derived
/// from the Lua sources on every test run and compared with what is committed.
/// </remarks>
public sealed class SpecInventoryTests
{
    private static readonly Lazy<SpecInventory> Inventory = new(SpecInventory.Load, isThreadSafe: true);
    private static readonly Lazy<IReadOnlyList<SpecFacts>> Scanned = new(SpecScanner.ScanAll, isThreadSafe: true);

    /// <summary>
    /// Exactly the spec files on disk, no more and no fewer. A new spec file that nobody
    /// classified is the failure mode this exists to catch.
    /// </summary>
    [Fact]
    public void Inventory_CoversEverySpecFile()
    {
        string[] onDisk = [.. Scanned.Value.Select(static f => f.File).Order(StringComparer.Ordinal)];
        string[] recorded = [.. Inventory.Value.Files.Select(static f => f.File).Order(StringComparer.Ordinal)];

        Assert.Equal(onDisk, recorded);
    }

    /// <summary>
    /// The mechanically extracted fields still describe the files. Reported in one go, because
    /// a rebase that touches several specs should produce one actionable list, not a game of
    /// whack-a-mole.
    /// </summary>
    [Fact]
    public void Inventory_MechanicalFieldsAreCurrent()
    {
        Dictionary<string, SpecFacts> scanned = Scanned.Value.ToDictionary(static f => f.File, StringComparer.Ordinal);
        List<string> drift = [];

        foreach (SpecInventoryEntry entry in Inventory.Value.Files)
        {
            if (!scanned.TryGetValue(entry.File, out SpecFacts? facts))
            {
                // Covered by Inventory_CoversEverySpecFile; skip rather than double-report.
                continue;
            }

            Compare(drift, entry.File, "loc", entry.Loc, facts.Loc);
            Compare(drift, entry.File, "testCount", entry.TestCount, facts.Tests.Count);
            Compare(drift, entry.File, "testNames", entry.TestNames, [.. facts.Tests.Select(static t => t.Name)]);
            Compare(drift, entry.File, "engineSurface", entry.EngineSurface, facts.Apis);
            Compare(drift, entry.File, "outputKeys", entry.OutputKeys, facts.OutputKeys);
            Compare(drift, entry.File, "modQueries", entry.ModQueries, facts.ModQueries);
        }

        Assert.True(
            drift.Count == 0,
            $"spec-inventory.json is out of date in {drift.Count} place(s); "
            + $"update dotnet/Pob.Tests/Infrastructure/spec-inventory.json:{Environment.NewLine}"
            + string.Join(Environment.NewLine, drift.Take(40)));
    }

    /// <summary>
    /// Every entry classifies itself, and every issue it names is one the inventory describes.
    /// A typo'd issue number would otherwise look like a real dependency forever.
    /// </summary>
    [Fact]
    public void Inventory_ClassificationsAreWellFormed()
    {
        string[] categories = ["engine", "domain", "ui", "platform", "superseded", "drop"];
        List<string> problems = [];

        foreach (SpecInventoryEntry entry in Inventory.Value.Files)
        {
            if (!categories.Contains(entry.Category, StringComparer.Ordinal))
            {
                problems.Add($"{entry.File}: unknown category '{entry.Category}'");
            }

            if (entry.Note.Length < 20)
            {
                problems.Add($"{entry.File}: note is too short to be a review");
            }

            foreach (int issue in entry.UnblockedBy)
            {
                if (!Inventory.Value.Tickets.ContainsKey(issue))
                {
                    problems.Add($"{entry.File}: issue #{issue} is not in the tickets table");
                }
            }

            switch (entry.Category)
            {
                case "drop":
                    if (entry.PrimaryUnblocker is not null || entry.UnblockedBy.Count > 0)
                    {
                        problems.Add($"{entry.File}: a 'drop' file cannot have an unblocker");
                    }

                    break;

                default:
                    if (entry.PrimaryUnblocker is null)
                    {
                        problems.Add($"{entry.File}: category '{entry.Category}' needs a primaryUnblocker");
                    }
                    else if (!entry.UnblockedBy.Contains(entry.PrimaryUnblocker.Value))
                    {
                        problems.Add($"{entry.File}: primaryUnblocker #{entry.PrimaryUnblocker} is not in unblockedBy");
                    }

                    break;
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The survey is not vacuous: it covers the whole suite, names the engine specs the ticket
    /// calls out by name, and records real output keys for them.
    /// </summary>
    [Fact]
    public void Inventory_IsSubstantive()
    {
        IReadOnlyList<SpecInventoryEntry> files = Inventory.Value.Files;

        Assert.True(files.Count >= 43, $"only {files.Count} spec files inventoried");
        Assert.True(files.Sum(static f => f.TestCount) >= 500, "fewer than 500 tests inventoried");

        // The files the ticket lists explicitly, and where each of them lands.
        (string File, int Issue)[] namedByTicket =
        [
            ("spec/System/TestOffence_spec.lua", 25),
            ("spec/System/TestDefence_spec.lua", 26),
            ("spec/System/TestAilments_spec.lua", 25),
            ("spec/System/TestImpale_spec.lua", 25),
            ("spec/System/TestTriggers_spec.lua", 27),
            ("spec/System/TestSkills_spec.lua", 23),
            ("spec/System/TestItemMods_spec.lua", 16),
            ("spec/System/TestBifurcatedCrit_spec.lua", 25),
        ];

        foreach ((string file, int issue) in namedByTicket)
        {
            SpecInventoryEntry entry = Assert.Single(files, f => string.Equals(f.File, file, StringComparison.Ordinal));
            Assert.Equal(issue, entry.PrimaryUnblocker);
        }

        // The engine specs are the ones that cannot be ported today, and they are the ones that
        // assert on output keys. If either of those stopped being true the survey would be wrong.
        IEnumerable<SpecInventoryEntry> engineSpecs = files.Where(static f => string.Equals(f.Category, "engine", StringComparison.Ordinal));
        Assert.True(engineSpecs.Count() >= 10, "expected at least ten engine-blocked spec files");
        Assert.All(engineSpecs, static f => Assert.InRange(f.PrimaryUnblocker ?? 0, 22, 27));

        Assert.True(
            files.SelectMany(static f => f.OutputKeys).Distinct(StringComparer.Ordinal).Count() >= 50,
            "the survey records fewer than 50 distinct engine output keys");
    }

    private static void Compare(List<string> drift, string file, string field, int expected, int actual)
    {
        if (expected != actual)
        {
            drift.Add(string.Create(CultureInfo.InvariantCulture, $"{file}: {field} says {expected}, source has {actual}"));
        }
    }

    private static void Compare(List<string> drift, string file, string field, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            return;
        }

        StringBuilder message = new();
        message.Append(CultureInfo.InvariantCulture, $"{file}: {field} differs");

        string[] onlyRecorded = [.. expected.Except(actual, StringComparer.Ordinal)];
        string[] onlySource = [.. actual.Except(expected, StringComparer.Ordinal)];

        if (onlyRecorded.Length > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $"; recorded but absent from source: {string.Join(", ", onlyRecorded.Take(6))}");
        }

        if (onlySource.Length > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $"; in source but not recorded: {string.Join(", ", onlySource.Take(6))}");
        }

        if (onlyRecorded.Length == 0 && onlySource.Length == 0)
        {
            message.Append("; same members, different order");
        }

        drift.Add(message.ToString());
    }
}
