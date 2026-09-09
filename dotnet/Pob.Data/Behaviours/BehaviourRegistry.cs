using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Pob.Data.Behaviours;

/// <summary>
/// The hand-written half of the behaviour contract: a frozen key to delegate table.
/// <see cref="Default"/> is built from <see cref="BehaviourRoster"/>, which every key in
/// <c>behaviour-keys.json</c> must appear in — the MSBuild target <c>VerifyBehaviourKeyRoster</c> in
/// Pob.Data.csproj fails the build otherwise.
/// </summary>
public sealed class BehaviourRegistry : IBehaviourRegistry
{
    private readonly FrozenDictionary<string, BehaviourEntry> _entries;

    /// <summary>Builds a registry over <paramref name="entries"/>.</summary>
    /// <exception cref="ArgumentException">Two entries share a key.</exception>
    public BehaviourRegistry(IEnumerable<BehaviourEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var byKey = new Dictionary<string, BehaviourEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byKey.TryAdd(entry.Key, entry))
            {
                throw new ArgumentException(
                    $"Behaviour key '{entry.Key}' is registered twice.", nameof(entries));
            }
        }

        _entries = byKey.ToFrozenDictionary(StringComparer.Ordinal);
        Keys = [.. byKey.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>The registry the shipped data loads against.</summary>
    public static BehaviourRegistry Default { get; } = new(BehaviourRoster.Entries);

    /// <inheritdoc />
    public IReadOnlyList<string> Keys { get; }

    /// <summary>How many of the registered behaviours have a real implementation. Ticket 19's progress metric.</summary>
    public int ImplementedCount => _entries.Values.Count(entry => entry.IsImplemented);

    /// <inheritdoc />
    public bool TryResolve(string key, [NotNullWhen(true)] out BehaviourEntry? entry) =>
        _entries.TryGetValue(key, out entry);

    /// <inheritdoc />
    public BehaviourEntry Resolve(string key, BehaviourHook hook)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (!_entries.TryGetValue(key, out var entry))
        {
            throw new UnknownBehaviourKeyException(key, hook);
        }

        if (entry.Hook != hook)
        {
            throw new UnknownBehaviourKeyException(
                $"Behaviour '{key}' is registered for hook {BehaviourHookNames.ToLuaName(entry.Hook)} " +
                $"but the data uses it as {BehaviourHookNames.ToLuaName(hook)}.");
        }

        return entry;
    }
}
