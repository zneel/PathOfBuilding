using System.Diagnostics.CodeAnalysis;
using Pob.Data.Behaviours;
using Xunit;

namespace Pob.Tests;

/// <summary>
/// The resolution half of the behaviour contract: keys become delegates once, at load, and a key the
/// registry does not know fails loudly at that point rather than later.
/// </summary>
public sealed class BehaviourKeysRegistryTests
{
    private sealed class CountingRegistry(IBehaviourRegistry inner) : IBehaviourRegistry
    {
        public int ResolveCalls { get; private set; }

        public IReadOnlyList<string> Keys => inner.Keys;

        public bool TryResolve(string key, [NotNullWhen(true)] out BehaviourEntry? entry) => inner.TryResolve(key, out entry);

        public BehaviourEntry Resolve(string key, BehaviourHook hook)
        {
            ResolveCalls++;
            return inner.Resolve(key, hook);
        }
    }

    private static BehaviourRegistry TestRegistry(params BehaviourEntry[] entries) => new(entries);

    [Fact]
    public void Bind_ResolvesEachKeyExactlyOnceHoweverOftenTheBehaviourRuns()
    {
        var calls = 0;
        var registry = new CountingRegistry(TestRegistry(
            BehaviourEntry.Implemented("Sample", BehaviourHook.PreDamageFunc, (_, _, _) => calls++)));

        var behaviours = GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { PreDamageFunc = "Sample" }, registry);

        for (var i = 0; i < 1000; i++)
        {
            behaviours.PreDamage!(null!, null!, null);
        }

        Assert.Equal(1, registry.ResolveCalls);
        Assert.Equal(1000, calls);
    }

    [Fact]
    public void Bind_StoresTheDelegateOnTheRecord()
    {
        var registry = TestRegistry(
            BehaviourEntry.Implemented("Sample", BehaviourHook.PostCritFunc, (_, _, _) => { }));

        var behaviours = GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { PostCritFunc = "Sample" }, registry);

        Assert.NotNull(behaviours.PostCrit);
        Assert.Null(behaviours.PreDamage);
        Assert.Null(behaviours.Initial);
        Assert.Same(behaviours.PostCrit, GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { PostCritFunc = "Sample" }, registry).PostCrit);
    }

    [Fact]
    public void Bind_OnUnknownKey_ThrowsNamingTheKey()
    {
        var registry = TestRegistry(BehaviourEntry.Pending("Known", BehaviourHook.PreDamageFunc));

        var exception = Assert.Throws<UnknownBehaviourKeyException>(() => GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { PreDamageFunc = "ArrivedWithTheNewLeague" }, registry));

        Assert.Equal("ArrivedWithTheNewLeague", exception.Key);
        Assert.Contains("ArrivedWithTheNewLeague", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_OnKeyRegisteredForAnotherHook_Throws()
    {
        var registry = TestRegistry(BehaviourEntry.Pending("Sample", BehaviourHook.PostCritFunc));

        Assert.Throws<UnknownBehaviourKeyException>(() => GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { PreDamageFunc = "Sample" }, registry));
    }

    [Fact]
    public void Bind_WithNoKeys_ReturnsTheSharedEmptyInstance()
    {
        var behaviours = GrantedEffectBehaviours.Bind(GrantedEffectBehaviourKeys.None, BehaviourRegistry.Default);

        Assert.Same(GrantedEffectBehaviours.None, behaviours);
        Assert.Null(behaviours.PreDamage);
    }

    [Fact]
    public void PendingBehaviour_BindsButThrowsNamingTheKeyWhenInvoked()
    {
        var registry = TestRegistry(BehaviourEntry.Pending("NotWrittenYet", BehaviourHook.PreDamageFunc));

        var behaviours = GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { PreDamageFunc = "NotWrittenYet" }, registry);

        // Binding succeeds - a pending behaviour is a known one - but running it is never silent.
        Assert.NotNull(behaviours.PreDamage);
        var exception = Assert.Throws<PendingBehaviourException>(() => behaviours.PreDamage!(null!, null!, null));
        Assert.Equal("NotWrittenYet", exception.Key);
    }

    [Fact]
    public void ExplosiveArrow_BindsThroughItsOwnDelegate()
    {
        var ran = false;
        var registry = TestRegistry(
            BehaviourEntry.Implemented("Fuses", (_, _, _, _, _) => ran = true));

        var behaviours = GrantedEffectBehaviours.Bind(
            new GrantedEffectBehaviourKeys { ExplosiveArrowFunc = "Fuses" }, registry);

        behaviours.ExplosiveArrow!(null!, null!, null!, null, null!);

        Assert.True(ran);
    }

    [Fact]
    public void Implemented_RejectsASkillBehaviourForTheExplosiveArrowHook()
    {
        Assert.Throws<ArgumentException>(() =>
            BehaviourEntry.Implemented("Wrong", BehaviourHook.ExplosiveArrowFunc, (_, _, _) => { }));
    }

    [Fact]
    public void Registry_RejectsDuplicateKeys()
    {
        Assert.Throws<ArgumentException>(() => TestRegistry(
            BehaviourEntry.Pending("Same", BehaviourHook.PreDamageFunc),
            BehaviourEntry.Pending("Same", BehaviourHook.InitialFunc)));
    }

    [Fact]
    public void DefaultRegistry_LoadsTheWholeRosterAndReportsItsCoverage()
    {
        var registry = BehaviourRegistry.Default;

        Assert.NotEmpty(registry.Keys);
        Assert.Equal(registry.Keys.Count, registry.Keys.Distinct(StringComparer.Ordinal).Count());

        // Ticket 19 raises this; today every enumerated behaviour is pending. The assertion is on
        // the range rather than the number so implementing one does not break the build.
        Assert.InRange(registry.ImplementedCount, 0, registry.Keys.Count);
    }

    [Fact]
    public void DefaultRegistry_ResolvesTheBrandBehaviourSevenSkillsShare()
    {
        Assert.True(BehaviourRegistry.Default.TryResolve("BrandActivationFrequency", out var entry));
        Assert.Equal(BehaviourHook.PreDamageFunc, entry.Hook);
    }
}
