using System.Diagnostics.CodeAnalysis;

namespace Pob.Data.Behaviours;

/// <summary>
/// Resolves the behaviour keys that transcoded data carries in place of Lua closures.
/// </summary>
/// <remarks>
/// Resolution happens once, while data is being loaded, and the result is stored as a delegate field
/// on the record that referenced it (see <see cref="GrantedEffectBehaviours"/>). Nothing in the calc
/// engine is expected to look a key up per call: a dictionary probe in the middle of a damage
/// calculation is both slower and later than it needs to be, and it turns a missing key into a
/// mid-calculation failure instead of a load failure.
/// </remarks>
public interface IBehaviourRegistry
{
    /// <summary>Every key the registry knows, in ordinal order.</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>Looks up <paramref name="key"/>.</summary>
    bool TryResolve(string key, [NotNullWhen(true)] out BehaviourEntry? entry);

    /// <summary>Looks up <paramref name="key"/>, expecting it to fill <paramref name="hook"/>.</summary>
    /// <exception cref="UnknownBehaviourKeyException">The key is not registered.</exception>
    BehaviourEntry Resolve(string key, BehaviourHook hook);
}
