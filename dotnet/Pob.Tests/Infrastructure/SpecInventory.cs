using System.Text.Json;

namespace Pob.Tests.Infrastructure;

/// <summary>One spec file's entry in the inventory.</summary>
/// <param name="File">Repository-relative path.</param>
/// <param name="Loc">Line count at the time the entry was written.</param>
/// <param name="TestCount">Number of <c>it("...")</c> blocks.</param>
/// <param name="Category">
/// Why it cannot be ported yet: <c>engine</c>, <c>domain</c>, <c>ui</c>, <c>platform</c>,
/// <c>superseded</c> or <c>drop</c>.
/// </param>
/// <param name="PrimaryUnblocker">
/// The GitHub issue that most directly unblocks it, or <see langword="null"/> for a file that
/// should not be ported at all.
/// </param>
/// <param name="UnblockedBy">Every issue that has to land first.</param>
/// <param name="EngineSurface">Which engine surfaces the file touches.</param>
/// <param name="OutputKeys">Engine output keys it asserts on.</param>
/// <param name="ModQueries">Mod names it queries through a ModStore call.</param>
/// <param name="TestNames">The test names, in file order.</param>
/// <param name="Note">The reviewer's note on what the file actually covers.</param>
public sealed record SpecInventoryEntry(
    string File,
    int Loc,
    int TestCount,
    string Category,
    int? PrimaryUnblocker,
    IReadOnlyList<int> UnblockedBy,
    IReadOnlyList<string> EngineSurface,
    IReadOnlyList<string> OutputKeys,
    IReadOnlyList<string> ModQueries,
    IReadOnlyList<string> TestNames,
    string Note);

/// <summary>
/// The ticket-08 survey of the busted spec suite, read back from
/// <c>Infrastructure/spec-inventory.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The ticket asks for the 44 spec files to be ported to xUnit. They cannot be: every engine
/// spec asserts on <c>build.calcsTab.mainOutput</c>, which does not exist in C# until issues
/// 22-27 land. What CAN be done now, and what this is, is the survey: for each file, what it
/// exercises, which output keys it pins down, and which issue has to land before it can be
/// ported. That inverts usefully -- read by unblocking issue, it is the list of behaviours
/// each engine ticket has to satisfy before its spec files can come across.
/// </para>
/// <para>
/// Unlike the Lua-generated corpora this file is ordinary JSON with ordinary JSON numbers: it
/// is written by tooling on the C# side of the fence, so the "spell doubles as strings"
/// convention (<see cref="LuaNumber"/>) does not apply and would only obscure it.
/// </para>
/// </remarks>
public sealed class SpecInventory
{
    private const int Format = 1;

    private SpecInventory(IReadOnlyList<SpecInventoryEntry> files, IReadOnlyDictionary<int, string> tickets)
    {
        Files = files;
        Tickets = tickets;
    }

    /// <summary>Every spec file, ordered by path.</summary>
    public IReadOnlyList<SpecInventoryEntry> Files { get; }

    /// <summary>Issue number to a short description of what that issue delivers.</summary>
    public IReadOnlyDictionary<int, string> Tickets { get; }

    /// <summary>Absolute path to the committed inventory.</summary>
    public static string Path => System.IO.Path.Combine(RepoPaths.TestInfrastructure, "spec-inventory.json");

    /// <summary>Load the committed inventory.</summary>
    /// <returns>The parsed inventory.</returns>
    public static SpecInventory Load()
    {
        using JsonDocument document = CorpusReader.Open(
            Path,
            Format,
            "regenerate by hand; SpecInventoryTests reports exactly what drifted");

        JsonElement root = document.RootElement;

        Dictionary<int, string> tickets = new();
        foreach (JsonProperty property in root.GetProperty("tickets").EnumerateObject())
        {
            tickets[int.Parse(property.Name, System.Globalization.CultureInfo.InvariantCulture)] = property.Value.GetString()!;
        }

        List<SpecInventoryEntry> files = [];
        foreach (JsonElement element in root.GetProperty("files").EnumerateArray())
        {
            JsonElement primary = element.GetProperty("primaryUnblocker");
            files.Add(new SpecInventoryEntry(
                element.GetProperty("file").GetString()!,
                element.GetProperty("loc").GetInt32(),
                element.GetProperty("testCount").GetInt32(),
                element.GetProperty("category").GetString()!,
                primary.ValueKind == JsonValueKind.Null ? null : primary.GetInt32(),
                [.. element.GetProperty("unblockedBy").EnumerateArray().Select(static e => e.GetInt32())],
                [.. element.GetProperty("engineSurface").EnumerateArray().Select(static e => e.GetString()!)],
                [.. element.GetProperty("outputKeys").EnumerateArray().Select(static e => e.GetString()!)],
                [.. element.GetProperty("modQueries").EnumerateArray().Select(static e => e.GetString()!)],
                [.. element.GetProperty("tests").EnumerateArray().Select(static e => e.GetProperty("name").GetString()!)],
                element.GetProperty("note").GetString()!));
        }

        return new SpecInventory(files, tickets);
    }
}
