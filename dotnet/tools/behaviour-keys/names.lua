-- Path of Building — behaviour-key enumeration (migration ticket 04)
--
-- Semantic names for behaviour bodies whose job is unambiguous from reading
-- them. Everything not listed here gets the deterministic <Owner><Hook> name
-- built by enumerate.lua, where Owner is the lexicographically first skill id in
-- the duplicate group (e.g. "ArcPreDamage").
--
-- Keyed by body hash, not by skill id, for two reasons:
--   * one name covers every duplicate of the body, which is the point of the
--     grouping — seven brand skills share one implementation;
--   * if GGG changes the body, the hash changes, the override stops matching,
--     enumerate.lua warns about the orphan, and the key falls back to a derived
--     name instead of silently keeping a description that is no longer true.
--
-- Only add an entry when the body says plainly what it does. A wrong name here
-- is worse than a dull one: ticket 19 implements against these names.

return {
	-- activeSkill.skillData.hitTimeOverride = repeatFrequency / (1 + INC Speed,
	-- BrandActivationFrequency) / More BrandActivationFrequency
	["55a58a4d86e8bdb1"] = "BrandActivationFrequency",

	-- skillPart 2 -> dpsMultiplier = output.ProjectileCount
	["916ea2023cd19543"] = "ProjectileCountDpsMultiplierOnSecondPart",

	-- dpsMultiplier = min(podOverlapMultiplier or 1, ProjectileCount)
	["4cc2f318d0b5c745"] = "PodOverlapDpsMultiplier",

	-- skillPart 2 -> dpsMultiplier scaled by floor(Duration / 0.66) overlaps
	["ce593358c9a957a4"] = "DurationOverlapDpsMultiplier",

	-- hitTimeOverride = output.Cooldown
	["1470b2dc7d4a5c70"] = "HitTimeOverrideFromCooldown",

	-- hitTimeOverride = skillData.repeatFrequency
	["35640df8a4f3561d"] = "HitTimeOverrideFromRepeatFrequency",

	-- dpsMultiplier = max(dpsMultiplier or 1, 1)
	["000c7c11684fa822"] = "DpsMultiplierFloorOfOne",

	-- counts hexes applied by Bane in the socket group into Multiplier:CurseApplied
	["104abbc4f0c428bc"] = "BaneAppliedCurseCount",

	-- fuse application rate, fuse limit and the resulting explosion damage for
	-- Explosive Arrow; the derived name would have been ExplosiveArrowExplosiveArrow
	["9f5dcab763d060af"] = "ExplosiveArrowFuseStacking",
}
