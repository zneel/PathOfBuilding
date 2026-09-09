namespace Pob.Data.Behaviours;

/// <summary>
/// The behaviour keys a transcoded granted effect carries. This is the shape the transcoder
/// (ticket 03) emits where the Lua data held a closure:
/// <c>"preDamageFunc": "BrandActivationFrequency"</c>.
/// </summary>
public sealed record GrantedEffectBehaviourKeys
{
    /// <summary>No behaviours, which is the case for the overwhelming majority of granted effects.</summary>
    public static GrantedEffectBehaviourKeys None { get; } = new();

    /// <summary>Key for the <c>initialFunc</c> hook, if any.</summary>
    public string? InitialFunc { get; init; }

    /// <summary>Key for the <c>preSkillTypeFunc</c> hook, if any.</summary>
    public string? PreSkillTypeFunc { get; init; }

    /// <summary>Key for the <c>preDamageFunc</c> hook, if any.</summary>
    public string? PreDamageFunc { get; init; }

    /// <summary>Key for the <c>postCritFunc</c> hook, if any.</summary>
    public string? PostCritFunc { get; init; }

    /// <summary>Key for the <c>explosiveArrowFunc</c> hook, if any.</summary>
    public string? ExplosiveArrowFunc { get; init; }

    /// <summary><see langword="true"/> when no hook is filled.</summary>
    public bool IsEmpty =>
        InitialFunc is null
        && PreSkillTypeFunc is null
        && PreDamageFunc is null
        && PostCritFunc is null
        && ExplosiveArrowFunc is null;
}

/// <summary>
/// The resolved behaviours of one granted effect: plain delegate fields, bound once when the data is
/// loaded. The calc engine calls <see cref="PreDamage"/> and friends directly; it never sees a key
/// and never touches the registry.
/// </summary>
public sealed class GrantedEffectBehaviours
{
    private GrantedEffectBehaviours()
    {
    }

    /// <summary>The instance used for granted effects with no behaviours at all.</summary>
    public static GrantedEffectBehaviours None { get; } = new();

    /// <summary><c>initialFunc</c>, or <see langword="null"/>.</summary>
    public SkillBehaviour? Initial { get; private init; }

    /// <summary><c>preSkillTypeFunc</c>, or <see langword="null"/>.</summary>
    public SkillBehaviour? PreSkillType { get; private init; }

    /// <summary><c>preDamageFunc</c>, or <see langword="null"/>.</summary>
    public SkillBehaviour? PreDamage { get; private init; }

    /// <summary><c>postCritFunc</c>, or <see langword="null"/>.</summary>
    public SkillBehaviour? PostCrit { get; private init; }

    /// <summary><c>explosiveArrowFunc</c>, or <see langword="null"/>.</summary>
    public ExplosiveArrowBehaviour? ExplosiveArrow { get; private init; }

    /// <summary>
    /// Resolves every key in <paramref name="keys"/> against <paramref name="registry"/>, once.
    /// Call this while loading data; keep the result on the granted effect.
    /// </summary>
    /// <exception cref="UnknownBehaviourKeyException">A key is not in the registry.</exception>
    public static GrantedEffectBehaviours Bind(GrantedEffectBehaviourKeys keys, IBehaviourRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(registry);

        if (keys.IsEmpty)
        {
            return None;
        }

        return new GrantedEffectBehaviours
        {
            Initial = BindSkill(keys.InitialFunc, BehaviourHook.InitialFunc, registry),
            PreSkillType = BindSkill(keys.PreSkillTypeFunc, BehaviourHook.PreSkillTypeFunc, registry),
            PreDamage = BindSkill(keys.PreDamageFunc, BehaviourHook.PreDamageFunc, registry),
            PostCrit = BindSkill(keys.PostCritFunc, BehaviourHook.PostCritFunc, registry),
            ExplosiveArrow = keys.ExplosiveArrowFunc is null
                ? null
                : registry.Resolve(keys.ExplosiveArrowFunc, BehaviourHook.ExplosiveArrowFunc).AsExplosiveArrowBehaviour(),
        };
    }

    private static SkillBehaviour? BindSkill(string? key, BehaviourHook hook, IBehaviourRegistry registry) =>
        key is null ? null : registry.Resolve(key, hook).AsSkillBehaviour();
}
