using System.Text.Json;
using Pob.Data.Behaviours;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// The manifest half of the behaviour contract: behaviour-keys.json is generated from src/Data, the
/// roster is hand-written, and the two have to agree.
/// </summary>
/// <remarks>
/// Pob.Data.csproj already fails the build when they disagree, by scraping both files with a regex.
/// These tests make the same comparison the other way round - against the registry as it actually
/// loads - so a roster line the MSBuild regex cannot see fails here instead of passing silently in
/// both places.
/// </remarks>
public sealed class BehaviourKeysManifestTests
{
    private static readonly JsonDocument Manifest = JsonDocument.Parse(BehaviourManifest.ReadAllText());

    private static IReadOnlyList<JsonElement> Behaviours =>
        [.. Manifest.RootElement.GetProperty("behaviours").EnumerateArray()];

    [Fact]
    public void Registry_CoversEveryManifestKey()
    {
        var manifestKeys = Behaviours
            .Select(behaviour => behaviour.GetProperty("key").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = manifestKeys.Except(BehaviourRegistry.Default.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Assert.Empty(missing);
    }

    [Fact]
    public void Registry_HasNoKeysTheManifestDoesNotEmit()
    {
        var manifestKeys = Behaviours
            .Select(behaviour => behaviour.GetProperty("key").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var stale = BehaviourRegistry.Default.Keys.Except(manifestKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Assert.Empty(stale);
    }

    [Fact]
    public void Registry_AgreesWithTheManifestAboutWhichHookEachKeyFills()
    {
        var mismatches = new List<string>();

        foreach (var behaviour in Behaviours)
        {
            var key = behaviour.GetProperty("key").GetString()!;
            var luaHook = behaviour.GetProperty("hook").GetString()!;

            Assert.True(BehaviourHookNames.TryParse(luaHook, out var expected), $"Unknown hook '{luaHook}' for key '{key}'.");
            Assert.True(BehaviourRegistry.Default.TryResolve(key, out var entry), $"Key '{key}' is not registered.");

            if (entry.Hook != expected)
            {
                mismatches.Add($"{key}: manifest says {luaHook}, roster says {BehaviourHookNames.ToLuaName(entry.Hook)}");
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Manifest_CountsMatchItsOwnEntries()
    {
        var summary = Manifest.RootElement.GetProperty("summary");

        Assert.Equal(Behaviours.Count, summary.GetProperty("behaviourKeys").GetInt32());
        Assert.Equal(
            Behaviours.Sum(behaviour => behaviour.GetProperty("siteCount").GetInt32()),
            summary.GetProperty("behaviourSites").GetInt32());
        Assert.Equal(
            Manifest.RootElement.GetProperty("declarative").GetProperty("entries").GetArrayLength(),
            summary.GetProperty("declarativeEntries").GetInt32());
    }

    [Fact]
    public void Manifest_KeysAreUniqueAndIdentifierSafe()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var behaviour in Behaviours)
        {
            var key = behaviour.GetProperty("key").GetString()!;
            Assert.True(seen.Add(key), $"Duplicate key '{key}' in the manifest.");
            Assert.Matches("^[A-Za-z][A-Za-z0-9]*$", key);
        }
    }

    [Fact]
    public void Manifest_RecordsEverySiteWithAFileAndLine()
    {
        foreach (var behaviour in Behaviours)
        {
            var sites = behaviour.GetProperty("sites").EnumerateArray().ToList();
            Assert.Equal(behaviour.GetProperty("siteCount").GetInt32(), sites.Count);

            foreach (var site in sites)
            {
                Assert.StartsWith("src/Data/", site.GetProperty("file").GetString(), StringComparison.Ordinal);
                Assert.True(site.GetProperty("line").GetInt32() > 0);
                Assert.True(site.GetProperty("endLine").GetInt32() >= site.GetProperty("line").GetInt32());
            }
        }
    }

    [Fact]
    public void Manifest_GroupsDuplicateBodiesUnderOneKey()
    {
        // The point of the body hash: one hash, one key, however many skills share the body.
        var keysByHash = Behaviours
            .GroupBy(behaviour => behaviour.GetProperty("bodyHash").GetString()!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(keysByHash);

        // ... and the grouping is doing real work: the brand activation formula is one key over
        // seven copies of the identical body (src/Data/Skills/act_int.lua:552 and six more).
        var brand = Behaviours.Single(behaviour =>
            string.Equals(behaviour.GetProperty("key").GetString(), "BrandActivationFrequency", StringComparison.Ordinal));
        Assert.Equal(7, brand.GetProperty("siteCount").GetInt32());
    }

    [Fact]
    public void Manifest_ModMapAppliesAreDeclarative()
    {
        var entries = Manifest.RootElement.GetProperty("declarative").GetProperty("entries").EnumerateArray().ToList();

        // Every ModMap affix, and no code emitted for any of them.
        Assert.Equal(41, entries.Count);

        var notDeclarative = entries
            .Where(entry => !entry.GetProperty("declarative").GetBoolean())
            .Select(entry => entry.GetProperty("id").GetString()!)
            .ToList();

        // One exception, recorded rather than hidden: "of Exposure" guards its mods with an
        // if-statement (src/Data/ModMap.lua:239).
        Assert.Equal(["of Exposure"], notDeclarative);

        foreach (var entry in entries)
        {
            // check / list / count: the affix type decides the argument list apply is called with
            // (src/Modules/ConfigOptions.lua:120-126), so a row without it cannot be replayed.
            Assert.Contains(entry.GetProperty("type").GetString(), new[] { "check", "list", "count" });

            foreach (var mod in entry.GetProperty("mods").EnumerateArray())
            {
                // The target matters: 18 of the map mods apply to the player, not the enemy, which
                // a {name, type, value, source} schema alone would lose.
                Assert.Contains(mod.GetProperty("target").GetString(), new[] { "modList", "enemyModList" });
                Assert.False(string.IsNullOrEmpty(mod.GetProperty("name").GetString()));
                Assert.False(string.IsNullOrEmpty(mod.GetProperty("type").GetString()));
                Assert.False(string.IsNullOrEmpty(mod.GetProperty("valueExpr").GetString()));
            }
        }
    }

    [Fact]
    public void Manifest_ExcludesDependencyInjectionWrappersFromTheKeyList()
    {
        var excluded = Manifest.RootElement.GetProperty("excluded").EnumerateArray().ToList();

        var wrappers = excluded.Single(group =>
            string.Equals(group.GetProperty("category").GetString(), "di-wrapper", StringComparison.Ordinal));

        // The 35 `return function(...)` file wrappers: every Bases/*.lua and Skills/*.lua, plus
        // Minions.lua, Spectres.lua and SkillStatMap.lua.
        Assert.Equal(35, wrappers.GetProperty("count").GetInt32());

        foreach (var group in excluded)
        {
            Assert.False(string.IsNullOrWhiteSpace(group.GetProperty("reason").GetString()));
        }
    }

    [Fact]
    public void Manifest_KeepsGeneratedUniquesOutOfTheRuntimeContract()
    {
        var transcode = Manifest.RootElement.GetProperty("transcodeTime");

        var files = transcode.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("file").GetString()!)
            .ToList();

        Assert.Contains("src/Data/Uniques/Special/Generated.lua", files, StringComparer.Ordinal);
        Assert.NotEmpty(transcode.GetProperty("functions").EnumerateArray());

        // Nothing from a transcode-time file may appear as a behaviour key.
        foreach (var behaviour in Behaviours)
        {
            foreach (var site in behaviour.GetProperty("sites").EnumerateArray())
            {
                Assert.DoesNotContain(site.GetProperty("file").GetString()!, files, StringComparer.Ordinal);
            }
        }
    }

    [Fact]
    public void Manifest_TextIsFreeOfCharactersTheBuildCheckCannotRead()
    {
        // The MSBuild completeness check passes each line of this file through a property-function
        // argument, where these four are expanded rather than compared. enumerate.lua escapes them
        // as \uXXXX; if that ever regresses, the build check silently stops matching.
        var text = BehaviourManifest.ReadAllText();

        Assert.DoesNotContain('%', text);
        Assert.DoesNotContain('$', text);
        Assert.DoesNotContain('@', text);
        Assert.DoesNotContain('\'', text);
    }
}
