namespace Pob.Data.Behaviours;

/// <summary>
/// Thrown at data-load time when transcoded data names a behaviour key the registry does not know.
/// Load-time, not call-time: a build with a stale registry fails while the data is being read,
/// not three screens later inside a damage calculation.
/// </summary>
public sealed class UnknownBehaviourKeyException : Exception
{
    /// <summary>Creates the exception for <paramref name="key"/>.</summary>
    public UnknownBehaviourKeyException(string key, BehaviourHook hook)
        : base($"No behaviour registered for key '{key}' (hook {BehaviourHookNames.ToLuaName(hook)}). " +
               "Either the data was transcoded from a newer src/Data than Pob.Data/behaviour-keys.json " +
               "describes, or the key is missing from Pob.Data/Behaviours/BehaviourRoster.cs.")
    {
        Key = key;
        Hook = hook;
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    public UnknownBehaviourKeyException(string message)
        : base(message)
    {
        Key = string.Empty;
    }

    /// <summary>Creates the exception with a caller-supplied message and inner exception.</summary>
    public UnknownBehaviourKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Key = string.Empty;
    }

    /// <summary>Creates the exception with no detail.</summary>
    public UnknownBehaviourKeyException()
        : base("No behaviour registered for the requested key.")
    {
        Key = string.Empty;
    }

    /// <summary>The behaviour key that could not be resolved.</summary>
    public string Key { get; }

    /// <summary>The hook the key was being resolved for.</summary>
    public BehaviourHook Hook { get; }
}

/// <summary>
/// Thrown when a behaviour that is enumerated and registered, but not yet written, is actually
/// invoked. Ticket 19 replaces the pending entries in the roster with real implementations; until
/// then this is what a build gets instead of a silently absent calculation.
/// </summary>
public sealed class PendingBehaviourException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="key"/>.</summary>
    public PendingBehaviourException(string key, BehaviourHook hook)
        : base($"Behaviour '{key}' (hook {BehaviourHookNames.ToLuaName(hook)}) is enumerated in " +
               "behaviour-keys.json and registered, but its implementation is still pending (ticket 19).")
    {
        Key = key;
        Hook = hook;
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    public PendingBehaviourException(string message)
        : base(message)
    {
        Key = string.Empty;
    }

    /// <summary>Creates the exception with a caller-supplied message and inner exception.</summary>
    public PendingBehaviourException(string message, Exception innerException)
        : base(message, innerException)
    {
        Key = string.Empty;
    }

    /// <summary>Creates the exception with no detail.</summary>
    public PendingBehaviourException()
        : base("The requested behaviour has no implementation yet.")
    {
        Key = string.Empty;
    }

    /// <summary>The behaviour key that has no implementation yet.</summary>
    public string Key { get; }

    /// <summary>The hook the pending behaviour fills.</summary>
    public BehaviourHook Hook { get; }
}
