namespace Pob.Data.Behaviours;

/// <summary>
/// The hooks the calc engine calls into. One per field name the Lua data tables bind a function to;
/// the enumerator refuses to emit a key for any hook not listed here, so a new hook arriving in a
/// league data drop is a build-time decision rather than a silent no-op.
/// </summary>
public enum BehaviourHook
{
    /// <summary>Pre-pass skill setup. <c>runSkillFunc("initialFunc")</c>, src/Modules/CalcOffence.lua:516.</summary>
    InitialFunc,

    /// <summary>Adjusts skill types before resolution. <c>runSkillFunc("preSkillTypeFunc")</c>, src/Modules/CalcOffence.lua:1054.</summary>
    PreSkillTypeFunc,

    /// <summary>Mutates skillData before the damage calculation. <c>runSkillFunc("preDamageFunc")</c>, src/Modules/CalcOffence.lua:1908.</summary>
    PreDamageFunc,

    /// <summary>Post-crit adjustment. <c>runSkillFunc("postCritFunc")</c>, src/Modules/CalcOffence.lua:3315.</summary>
    PostCritFunc,

    /// <summary>Explosive Arrow fuse handling. Called directly at src/Modules/CalcOffence.lua:3038 with a wider signature.</summary>
    ExplosiveArrowFunc,
}

/// <summary>
/// Maps <see cref="BehaviourHook"/> to and from the Lua field names used in
/// <c>behaviour-keys.json</c> and in the transcoded data.
/// </summary>
public static class BehaviourHookNames
{
    private static readonly (BehaviourHook Hook, string Name)[] Pairs =
    [
        (BehaviourHook.InitialFunc, "initialFunc"),
        (BehaviourHook.PreSkillTypeFunc, "preSkillTypeFunc"),
        (BehaviourHook.PreDamageFunc, "preDamageFunc"),
        (BehaviourHook.PostCritFunc, "postCritFunc"),
        (BehaviourHook.ExplosiveArrowFunc, "explosiveArrowFunc"),
    ];

    /// <summary>The Lua field name for <paramref name="hook"/>, for example <c>preDamageFunc</c>.</summary>
    public static string ToLuaName(BehaviourHook hook)
    {
        foreach (var (candidate, name) in Pairs)
        {
            if (candidate == hook)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(hook), hook, "Unknown behaviour hook.");
    }

    /// <summary>Resolves a Lua field name such as <c>preDamageFunc</c> to its hook.</summary>
    public static bool TryParse(string luaName, out BehaviourHook hook)
    {
        foreach (var (candidate, name) in Pairs)
        {
            if (string.Equals(name, luaName, StringComparison.Ordinal))
            {
                hook = candidate;
                return true;
            }
        }

        hook = default;
        return false;
    }
}
