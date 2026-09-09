using System.Globalization;

namespace Pob.Tests.Infrastructure;

/// <summary>
/// How strictly two corpus values have to agree.
/// </summary>
/// <param name="BitExact">
/// When <see langword="true"/>, numbers must have identical IEEE-754 bit patterns and no
/// tolerance applies at all. This is the setting for anything the port claims to reproduce
/// exactly -- the <c>LuaCompat</c> primitives, and eventually <c>Sum</c>/<c>More</c>/
/// <c>EvalMod</c>, whose whole contract is bit-for-bit agreement with Lua.
/// </param>
/// <param name="IntegralsExact">
/// When <see langword="true"/>, a pair where BOTH sides are whole numbers must match exactly,
/// with no epsilon. Counts, charges, levels, resistances and flags are integers by
/// construction; letting an epsilon absorb an off-by-one in them would defeat the corpus.
/// Ignored when <paramref name="BitExact"/> is set, which is stricter anyway.
/// </param>
/// <param name="RelativeEpsilon">
/// Allowed relative difference for non-integral numbers, as a fraction of the larger magnitude.
/// </param>
/// <param name="AbsoluteFloor">
/// Allowed absolute difference regardless of magnitude. Without a floor, a relative test on
/// values near zero is effectively a bit-exact test, because the tolerance shrinks with the
/// values -- which is wrong when the two sides reached a near-zero number by different but
/// equally valid routes.
/// </param>
public sealed record ComparisonPolicy(
    bool BitExact,
    bool IntegralsExact,
    double RelativeEpsilon,
    double AbsoluteFloor)
{
    /// <summary>
    /// Bit-for-bit. Nothing is tolerated: not the last ulp, not the sign of zero.
    /// </summary>
    public static ComparisonPolicy Exact { get; } = new(
        BitExact: true,
        IntegralsExact: true,
        RelativeEpsilon: 0.0,
        AbsoluteFloor: 0.0);

    /// <summary>
    /// The default ladder: exact for booleans, strings and integers; 1e-9 relative with a
    /// 1e-12 absolute floor for everything else.
    /// </summary>
    /// <remarks>
    /// 1e-9 is roughly a thousand times the double epsilon at unit magnitude. It is loose
    /// enough to absorb a differently-ordered but mathematically equal summation, and far
    /// tighter than any difference the engine's own rounding rules produce: <c>More</c> rounds
    /// to two decimals per mod name (<c>src/Classes/ModDB.lua:197</c>), so a genuine rounding
    /// divergence shows up at the 1e-2 scale, seven orders of magnitude above this.
    /// </remarks>
    public static ComparisonPolicy Default { get; } = new(
        BitExact: false,
        IntegralsExact: true,
        RelativeEpsilon: 1e-9,
        AbsoluteFloor: 1e-12);

    /// <summary>
    /// The tolerance the busted suite itself uses for DPS-scale numbers
    /// (<c>spec/System/TestOffence_spec.lua:12</c> asserts within 0.5%). Reach for this only
    /// where a spec file being ported already accepted it; a corpus comparison that needs it
    /// is telling you something.
    /// </summary>
    public static ComparisonPolicy SpecRelative { get; } = new(
        BitExact: false,
        IntegralsExact: false,
        RelativeEpsilon: 5e-3,
        AbsoluteFloor: 1e-9);

    /// <summary>A one-line description for a failure message.</summary>
    /// <returns>The policy in words.</returns>
    public string Describe() => BitExact
        ? "bit-exact"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"relative {RelativeEpsilon:G} (absolute floor {AbsoluteFloor:G}){(IntegralsExact ? ", integers exact" : string.Empty)}");
}
