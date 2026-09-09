using static Pob.Data.Behaviours.BehaviourEntry;

namespace Pob.Data.Behaviours;

/// <summary>
/// The behaviour roster: one line per behaviour key, hand-maintained.
/// </summary>
/// <remarks>
/// <para>
/// This file is the hand-written side of the behaviour contract. It is deliberately NOT generated
/// from <c>behaviour-keys.json</c> - if it were, the completeness check that compares the two would
/// be checking a file against itself. It was bootstrapped once, when the contract was first
/// enumerated (ticket 04), and is edited by hand from then on.
/// </para>
/// <para>
/// When a league data drop adds a behaviour, re-running
/// <c>dotnet/tools/behaviour-keys/enumerate.lua</c> adds a key to the manifest, the
/// <c>VerifyBehaviourKeyRoster</c> target in Pob.Data.csproj fails the build naming that key, and
/// somebody has to decide here what it is: a duplicate of a behaviour already written, or a new one.
/// That decision is the point of the file.
/// </para>
/// <para>
/// <c>Pending</c> means enumerated but not yet written; ticket 19 turns those into
/// <c>Implemented</c>. Every entry keeps its hook next to it so a mis-triaged key - a
/// <c>postCritFunc</c> registered as a <c>preDamageFunc</c> - fails the manifest test rather than
/// running at the wrong point in the calculation.
/// </para>
/// </remarks>
public static class BehaviourRoster
{
    /// <summary>Every registered behaviour.</summary>
    public static IReadOnlyList<BehaviourEntry> Entries { get; } =
    [
        // initialFunc - pre-pass skill setup, src/Modules/CalcOffence.lua:516
        // src/Data/Skills/other.lua:669
        Pending("BloodSacramentUniqueInitial", BehaviourHook.InitialFunc),
        // src/Data/Skills/act_dex.lua:4809
        Pending("CycloneAltXInitial", BehaviourHook.InitialFunc),
        // src/Data/Skills/act_dex.lua:4703 (+1 more)
        Pending("CycloneInitial", BehaviourHook.InitialFunc),

        // preSkillTypeFunc - skill-type adjustment, src/Modules/CalcOffence.lua:1054
        // src/Data/Skills/act_int.lua:1470 (+1 more)
        Pending("BaneAppliedCurseCount", BehaviourHook.PreSkillTypeFunc),
        // src/Data/Skills/act_str.lua:9082
        Pending("RageVortexPreSkillType", BehaviourHook.PreSkillTypeFunc),

        // preDamageFunc - skillData mutation before damage, src/Modules/CalcOffence.lua:1908
        // src/Data/Skills/sup_str.lua:2540
        Pending("AvengingFlamePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/spectre.lua:7277
        Pending("AzmeriHydraBarragePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:1252
        Pending("BallLightningAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:1375
        Pending("BallLightningAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:1086
        Pending("BallLightningPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:1374 (+1 more)
        Pending("BladeBlastPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:1906 (+1 more)
        Pending("BladeFlurryPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:2209
        Pending("BladeVortexPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:2787
        Pending("BladefallAltZPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:1823
        Pending("BlazingSalvoPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:2298 (+1 more)
        Pending("BodyswapPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:552 (+6 more)
        Pending("BrandActivationFrequency", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:4064
        Pending("ChargedDashAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:3939
        Pending("ChargedDashPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:4592
        Pending("CremationAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:4355 (+1 more)
        Pending("CremationPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:4006
        Pending("DarkPactAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:3889
        Pending("DarkPactPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/other.lua:1282
        Pending("DeathWishPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:4712
        Pending("DivineIreAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:4823
        Pending("DivineIreAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:4602
        Pending("DivineIrePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:8304 (+2 more)
        Pending("DpsMultiplierFloorOfOne", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:16721 (+2 more)
        Pending("DurationOverlapDpsMultiplier", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:3363
        Pending("EarthquakePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/other.lua:6204
        Pending("EnemyExplodePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:7156
        Pending("ExplosiveTrapPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:5611
        Pending("EyeOfWinterAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:5704
        Pending("EyeOfWinterAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:5518
        Pending("EyeOfWinterPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:6905
        Pending("FlameblastAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:7008
        Pending("FlameblastAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:6800
        Pending("FlameblastPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:7809
        Pending("FlamethrowerTrapAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:7691
        Pending("FlamethrowerTrapPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:7549
        Pending("ForbiddenRiteAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:7390
        Pending("ForbiddenRitePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:7686
        Pending("FreezingPulsePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:7933
        Pending("FrostBombAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:4797
        Pending("FrozenSweepAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:4679
        Pending("FrozenSweepPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/minion.lua:756 (+1 more)
        Pending("GAZombieCorpseGroundImpactPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:5743
        Pending("HeraldOfAshPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/other.lua:2258
        Pending("HeraldOfTheBreachPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:9383
        Pending("HeraldOfThunderPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:20542 (+2 more)
        Pending("HitTimeOverrideFromCooldown", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:8543 (+1 more)
        Pending("HitTimeOverrideFromRepeatFrequency", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:8972
        Pending("HydrospherePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:9805 (+1 more)
        Pending("IceSpearPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:10422
        Pending("IncinerateAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:10561
        Pending("IncinerateAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:10284
        Pending("IncineratePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:7241
        Pending("InfernalBlowAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:7115
        Pending("InfernalBlowPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:11227
        Pending("KineticFusilladeAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:11009
        Pending("KineticFusilladePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:10337
        Pending("LancingSteelAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:10234
        Pending("LancingSteelPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:11912
        Pending("LightningSpireTrapAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:12150
        Pending("LightningSpireTrapAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:11667
        Pending("LightningSpireTrapPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:12642
        Pending("LightningTendrilsAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:13527
        Pending("ManabondPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:7599
        Pending("MoltenShellPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:8011
        Pending("MoltenStrikeAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:7832
        Pending("MoltenStrikePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:13621
        Pending("OrbOfStormsPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:13809
        Pending("PenanceBrandPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:14935 (+2 more)
        Pending("PodOverlapDpsMultiplier", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:17423 (+1 more)
        Pending("PoisonousConcoctionPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:1026 (+3 more)
        Pending("ProjectileCountDpsMultiplierOnSecondPart", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:15533
        Pending("RighteousFireAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:15428
        Pending("RighteousFirePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:12990
        Pending("ScourgeArrowPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:13622
        Pending("SeismicTrapAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:13381
        Pending("SeismicTrapPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:13779 (+1 more)
        Pending("ShrapnelBallistaPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:10726
        Pending("StaticStrikePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:18151
        Pending("StormBurstAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:18036
        Pending("StormBurstPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:11851
        Pending("StormRainAltXPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:11958
        Pending("StormRainAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:11747
        Pending("StormRainPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:18127
        Pending("TornadoAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:18034
        Pending("TornadoPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:16051
        Pending("TornadoShotPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_dex.lua:2428
        Pending("VaalBladeVortexPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_str.lua:7701
        Pending("VaalMoltenShellPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:15625
        Pending("VaalRighteousFirePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:20267 (+1 more)
        Pending("VoidSpherePreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:20445
        Pending("VoltaxicBurstPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:20889
        Pending("WaveOfConvictionAltYPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:20795
        Pending("WaveOfConvictionPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:20983
        Pending("WinterOrbPreDamage", BehaviourHook.PreDamageFunc),
        // src/Data/Skills/act_int.lua:21107
        Pending("WintertideBrandPreDamage", BehaviourHook.PreDamageFunc),

        // postCritFunc - post-crit adjustment, src/Modules/CalcOffence.lua:3315
        // src/Data/Skills/act_int.lua:11046 (+1 more)
        Pending("KineticFusilladePostCrit", BehaviourHook.PostCritFunc),
        // src/Data/Skills/act_int.lua:12651
        Pending("LightningTendrilsAltXPostCrit", BehaviourHook.PostCritFunc),

        // explosiveArrowFunc - called directly, src/Modules/CalcOffence.lua:3038
        // src/Data/Skills/act_dex.lua:6699
        Pending("ExplosiveArrowFuseStacking", BehaviourHook.ExplosiveArrowFunc),
    ];
}
