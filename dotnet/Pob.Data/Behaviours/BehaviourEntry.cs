namespace Pob.Data.Behaviours;

/// <summary>
/// One row of the behaviour registry: a key from <c>behaviour-keys.json</c>, the hook it fills, and
/// the delegate that implements it. An entry always carries a delegate — a pending entry carries one
/// that throws <see cref="PendingBehaviourException"/> — so resolution never yields
/// <see langword="null"/> and a missing implementation can never read as "this skill has no
/// behaviour".
/// </summary>
public sealed class BehaviourEntry
{
    private BehaviourEntry(string key, BehaviourHook hook, Delegate implementation, bool isImplemented)
    {
        Key = key;
        Hook = hook;
        Implementation = implementation;
        IsImplemented = isImplemented;
    }

    /// <summary>The stable key the transcoded data refers to this behaviour by.</summary>
    public string Key { get; }

    /// <summary>The hook this behaviour fills.</summary>
    public BehaviourHook Hook { get; }

    /// <summary>
    /// <see langword="false"/> while the body is still to be written (ticket 19). Callers use it for
    /// coverage reporting; invoking a pending behaviour throws.
    /// </summary>
    public bool IsImplemented { get; }

    /// <summary>
    /// The delegate: a <see cref="SkillBehaviour"/> for every hook except
    /// <see cref="BehaviourHook.ExplosiveArrowFunc"/>, which carries an <see cref="ExplosiveArrowBehaviour"/>.
    /// </summary>
    public Delegate Implementation { get; }

    /// <summary>Registers a written behaviour for one of the four <see cref="SkillBehaviour"/> hooks.</summary>
    public static BehaviourEntry Implemented(string key, BehaviourHook hook, SkillBehaviour behaviour)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(behaviour);
        RequireSkillHook(hook);
        return new BehaviourEntry(key, hook, behaviour, isImplemented: true);
    }

    /// <summary>Registers a written behaviour for <see cref="BehaviourHook.ExplosiveArrowFunc"/>.</summary>
    public static BehaviourEntry Implemented(string key, ExplosiveArrowBehaviour behaviour)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(behaviour);
        return new BehaviourEntry(key, BehaviourHook.ExplosiveArrowFunc, behaviour, isImplemented: true);
    }

    /// <summary>
    /// Registers a behaviour that is enumerated but not yet written. The delegate it installs throws
    /// <see cref="PendingBehaviourException"/> naming the key, so an unimplemented behaviour that is
    /// actually reached is a loud failure rather than a wrong number.
    /// </summary>
    public static BehaviourEntry Pending(string key, BehaviourHook hook)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Delegate stub = hook == BehaviourHook.ExplosiveArrowFunc
            ? new ExplosiveArrowBehaviour((_, _, _, _, _) => throw new PendingBehaviourException(key, hook))
            : new SkillBehaviour((_, _, _) => throw new PendingBehaviourException(key, hook));
        return new BehaviourEntry(key, hook, stub, isImplemented: false);
    }

    /// <summary>The delegate as a <see cref="SkillBehaviour"/>.</summary>
    /// <exception cref="InvalidOperationException">The entry fills <see cref="BehaviourHook.ExplosiveArrowFunc"/>.</exception>
    public SkillBehaviour AsSkillBehaviour() =>
        Implementation as SkillBehaviour
        ?? throw new InvalidOperationException(
            $"Behaviour '{Key}' fills hook {BehaviourHookNames.ToLuaName(Hook)} and is not a {nameof(SkillBehaviour)}.");

    /// <summary>The delegate as an <see cref="ExplosiveArrowBehaviour"/>.</summary>
    /// <exception cref="InvalidOperationException">The entry does not fill <see cref="BehaviourHook.ExplosiveArrowFunc"/>.</exception>
    public ExplosiveArrowBehaviour AsExplosiveArrowBehaviour() =>
        Implementation as ExplosiveArrowBehaviour
        ?? throw new InvalidOperationException(
            $"Behaviour '{Key}' fills hook {BehaviourHookNames.ToLuaName(Hook)} and is not an {nameof(ExplosiveArrowBehaviour)}.");

    private static void RequireSkillHook(BehaviourHook hook)
    {
        if (hook == BehaviourHook.ExplosiveArrowFunc)
        {
            throw new ArgumentException(
                $"Hook {BehaviourHookNames.ToLuaName(hook)} takes an {nameof(ExplosiveArrowBehaviour)}, not a {nameof(SkillBehaviour)}.",
                nameof(hook));
        }
    }
}
