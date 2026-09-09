-- The build space the golden corpus covers (migration ticket 07).
--
-- The five builds in `spec/TestBuilds/3.13` are all 3.13-era and all hand-authored, which
-- means the corpus grows only when a person sits down and exports another build. This module
-- takes the opposite approach: it enumerates the engine's own data - every keystone on the
-- tree, every class/ascendancy pair, a stratified sample of the gem list, every weapon base
-- class - so the corpus is a function of the data the port has to handle, and it re-derives
-- itself when the game data changes.
--
-- Nothing here is hardcoded to a gem or node name that might not exist. Names come out of
-- `data.gems` and `spec.nodes`; anything the generator cannot resolve is recorded as a
-- failure in the manifest rather than dropped, because a skill the engine has and the corpus
-- silently skips is exactly the hole a conformance oracle is supposed to close.

local m = {}

-- ---------------------------------------------------------------------------------------
-- Gear kits
-- ---------------------------------------------------------------------------------------

-- Rare mod text, not uniques: uniques are the ModParser oracle's job (ticket 05), and using
-- them here would couple every number in the corpus to a specific league's unique roll
-- ranges. These lines are deliberately plain, and the generator asserts that every one of
-- them parses - a mod the engine silently ignores would make a golden number quietly wrong.

local WEAPON_MODS = {
	attack = {
		"Adds 32 to 58 Physical Damage",
		"145% increased Physical Damage",
		"17% increased Attack Speed",
		"+38% to Global Critical Strike Multiplier",
		"28% increased Critical Strike Chance",
	},
	caster = {
		"96% increased Spell Damage",
		"Adds 24 to 46 Fire Damage to Spells",
		"19% increased Cast Speed",
		"+42% to Global Critical Strike Multiplier",
		"88% increased Critical Strike Chance for Spells",
		"+1 to Level of all Spell Skill Gems",
	},
	minion = {
		"+2 to Level of all Minion Skill Gems",
		"52% increased Minion Damage",
		"18% increased Minion Attack Speed",
		"Minions have +32% to Global Critical Strike Multiplier",
	},
}

local ARMOUR_MODS = {
	life = {
		["Body Armour"] = { "+126 to maximum Life", "11% increased maximum Life", "+41% to Fire Resistance", "+38% to Cold Resistance" },
		["Helmet"] = { "+94 to maximum Life", "+42% to Lightning Resistance", "+31 to Intelligence" },
		["Gloves"] = { "+83 to maximum Life", "13% increased Attack Speed", "Adds 11 to 21 Physical Damage to Attacks" },
		["Boots"] = { "+91 to maximum Life", "30% increased Movement Speed", "+37% to Chaos Resistance" },
		["Belt"] = { "+104 to maximum Life", "+43 to Strength", "+34% to Fire Resistance" },
		["Amulet"] = { "+72 to maximum Life", "+51 to all Attributes", "+29% to Global Critical Strike Multiplier" },
		["Ring"] = { "+63 to maximum Life", "+33% to Lightning Resistance", "Adds 6 to 13 Fire Damage to Attacks" },
		["Shield"] = { "+88 to maximum Life", "+36% to Cold Resistance", "9% increased maximum Life" },
		["Quiver"] = { "+74 to maximum Life", "Adds 9 to 16 Physical Damage to Attacks", "14% increased Attack Speed" },
	},
	energyShield = {
		["Body Armour"] = { "+118 to maximum Energy Shield", "94% increased Energy Shield", "+41% to Fire Resistance", "+38% to Cold Resistance" },
		["Helmet"] = { "+87 to maximum Energy Shield", "+42% to Lightning Resistance", "+55 to Intelligence" },
		["Gloves"] = { "+76 to maximum Energy Shield", "13% increased Cast Speed", "26% increased Energy Shield" },
		["Boots"] = { "+81 to maximum Energy Shield", "30% increased Movement Speed", "+37% to Chaos Resistance" },
		["Belt"] = { "+62 to maximum Energy Shield", "+43 to Strength", "+34% to Fire Resistance" },
		["Amulet"] = { "+58 to maximum Energy Shield", "+51 to all Attributes", "12% increased maximum Energy Shield" },
		["Ring"] = { "+49 to maximum Energy Shield", "+33% to Lightning Resistance", "+38 to Intelligence" },
		["Shield"] = { "+96 to maximum Energy Shield", "+36% to Cold Resistance", "28% increased Energy Shield" },
		["Quiver"] = { "+44 to maximum Energy Shield", "Adds 9 to 16 Physical Damage to Attacks", "14% increased Attack Speed" },
	},
	armour = {
		["Body Armour"] = { "+610 to Armour", "128% increased Armour", "+41% to Fire Resistance", "+96 to maximum Life" },
		["Helmet"] = { "+310 to Armour", "+42% to Lightning Resistance", "+31 to Strength" },
		["Gloves"] = { "+260 to Armour", "+68 to maximum Life", "13% increased Attack Speed" },
		["Boots"] = { "+280 to Armour", "30% increased Movement Speed", "+37% to Chaos Resistance" },
		["Belt"] = { "+330 to Armour", "+43 to Strength", "+34% to Fire Resistance" },
		["Amulet"] = { "+72 to maximum Life", "+51 to all Attributes", "12% increased Armour" },
		["Ring"] = { "+63 to maximum Life", "+33% to Lightning Resistance", "+120 to Armour" },
		["Shield"] = { "+420 to Armour", "+36% to Cold Resistance", "+88 to maximum Life" },
		["Quiver"] = { "+74 to maximum Life", "Adds 9 to 16 Physical Damage to Attacks", "14% increased Attack Speed" },
	},
	evasion = {
		["Body Armour"] = { "+720 to Evasion Rating", "134% increased Evasion Rating", "+41% to Fire Resistance", "+96 to maximum Life" },
		["Helmet"] = { "+360 to Evasion Rating", "+42% to Lightning Resistance", "+31 to Dexterity" },
		["Gloves"] = { "+290 to Evasion Rating", "+68 to maximum Life", "13% increased Attack Speed" },
		["Boots"] = { "+320 to Evasion Rating", "30% increased Movement Speed", "+37% to Chaos Resistance" },
		["Belt"] = { "+104 to maximum Life", "+43 to Dexterity", "+34% to Fire Resistance" },
		["Amulet"] = { "+72 to maximum Life", "+51 to all Attributes", "14% increased Evasion Rating" },
		["Ring"] = { "+63 to maximum Life", "+33% to Lightning Resistance", "+140 to Evasion Rating" },
		["Shield"] = { "+380 to Evasion Rating", "+36% to Cold Resistance", "+88 to maximum Life" },
		["Quiver"] = { "+74 to maximum Life", "Adds 9 to 16 Physical Damage to Attacks", "14% increased Attack Speed" },
	},
	hybrid = {
		["Body Armour"] = { "+96 to maximum Life", "+92 to maximum Energy Shield", "+420 to Armour", "+41% to Fire Resistance" },
		["Helmet"] = { "+68 to maximum Life", "+64 to maximum Energy Shield", "+42% to Lightning Resistance" },
		["Gloves"] = { "+62 to maximum Life", "+58 to maximum Energy Shield", "13% increased Attack Speed" },
		["Boots"] = { "+71 to maximum Life", "+61 to maximum Energy Shield", "30% increased Movement Speed" },
		["Belt"] = { "+104 to maximum Life", "+43 to Strength", "+34% to Fire Resistance" },
		["Amulet"] = { "+72 to maximum Life", "+48 to maximum Energy Shield", "+51 to all Attributes" },
		["Ring"] = { "+63 to maximum Life", "+33% to Lightning Resistance", "+44 to maximum Energy Shield" },
		["Shield"] = { "+88 to maximum Life", "+74 to maximum Energy Shield", "+36% to Cold Resistance" },
		["Quiver"] = { "+74 to maximum Life", "Adds 9 to 16 Physical Damage to Attacks", "14% increased Attack Speed" },
	},
}

m.WEAPON_MODS = WEAPON_MODS
m.ARMOUR_MODS = ARMOUR_MODS

-- ---------------------------------------------------------------------------------------
-- Gem classification
-- ---------------------------------------------------------------------------------------

-- Gems the engine ships but nobody can put in a socket: cosmetics, the hidden halves of
-- support-granted skills, and skills only a monster can use.
local function isUsableActive(gemData)
	local ge = gemData.grantedEffect
	if not ge or ge.support or ge.hidden then
		return false
	end
	local types = ge.skillTypes or {}
	if types[SkillType.Microtransaction] or types[SkillType.OwnerCannotUse]
		or types[SkillType.SkillGrantedBySupport] or types[SkillType.Aegis]
	then
		return false
	end
	return true
end

--- Buckets a gem into one coverage category. First match wins, so the order is the
--- priority order: the mechanic a skill is interesting *for* comes before what it also is.
local function categorise(gemData)
	local ge = gemData.grantedEffect
	local t = ge.skillTypes or {}
	local weapons = ge.weaponTypes or {}
	if t[SkillType.Vaal] then return "vaal" end
	if t[SkillType.Minion] or t[SkillType.CreatesMinion] then return "minion" end
	if t[SkillType.SummonsTotem] then return "totem" end
	if t[SkillType.Brand] then return "brand" end
	if t[SkillType.Hex] or t[SkillType.Mark] or t[SkillType.AppliesCurse] then return "curse" end
	if t[SkillType.Warcry] then return "warcry" end
	if t[SkillType.Herald] then return "herald" end
	if t[SkillType.Aura] or t[SkillType.HasReservation] then return "reservation" end
	if t[SkillType.Guard] then return "guard" end
	if t[SkillType.Golem] then return "golem" end
	if t[SkillType.InbuiltTrigger] or t[SkillType.Triggered] then return "triggered" end
	if t[SkillType.Channel] then return "channelled" end
	if t[SkillType.Movement] or t[SkillType.Travel] or t[SkillType.Blink] then return "movement" end
	if t[SkillType.Attack] then
		if weapons["Bow"] then return "attack-bow" end
		if t[SkillType.Slam] then return "attack-slam" end
		if t[SkillType.Melee] then return "attack-melee" end
		if t[SkillType.Projectile] then return "attack-projectile" end
		return "attack-other"
	end
	if t[SkillType.Spell] then
		if t[SkillType.DamageOverTime] then return "spell-dot" end
		if t[SkillType.Projectile] then return "spell-projectile" end
		if t[SkillType.Area] then return "spell-area" end
		return "spell-other"
	end
	return "other"
end

m.categorise = categorise

-- How many gems to take from each category. Damage-dealing paths get the most, because
-- CalcOffence is where the port's per-key diffs will actually land; the support-shaped
-- categories still get representation because they run different code (reservation
-- arithmetic, warcry buff application, curse effect) that nothing else exercises.
local CATEGORY_QUOTA = {
	["attack-melee"] = 12,
	["attack-bow"] = 8,
	["attack-slam"] = 6,
	["attack-projectile"] = 5,
	["attack-other"] = 4,
	["spell-area"] = 12,
	["spell-projectile"] = 8,
	["spell-dot"] = 6,
	["spell-other"] = 5,
	["minion"] = 10,
	["totem"] = 4,
	["brand"] = 3,
	["curse"] = 5,
	["warcry"] = 4,
	["herald"] = 3,
	["reservation"] = 5,
	["guard"] = 3,
	["golem"] = 3,
	["triggered"] = 5,
	["channelled"] = 5,
	["movement"] = 3,
	["vaal"] = 6,
	["other"] = 3,
}

--- Returns the stratified gem sample: `{ { gem = gemData, category = "..." }, ... }`,
--- deterministic (sorted by gem name within each category).
function m.gemSample()
	local byCategory = {}
	for _, gemData in pairs(data.gems) do
		if isUsableActive(gemData) then
			local cat = categorise(gemData)
			byCategory[cat] = byCategory[cat] or {}
			table.insert(byCategory[cat], gemData)
		end
	end
	local categories = {}
	for cat in pairs(byCategory) do
		categories[#categories + 1] = cat
	end
	table.sort(categories)

	local picked = {}
	for _, cat in ipairs(categories) do
		local list = byCategory[cat]
		table.sort(list, function(a, b) return a.name < b.name end)
		local quota = CATEGORY_QUOTA[cat] or 2
		-- Spread the picks across the alphabetised list instead of taking a prefix, so
		-- the sample is not twelve variants of the same skill letter.
		local step = math.max(1, math.floor(#list / quota))
		local taken = 0
		local i = 1
		while taken < quota and i <= #list do
			picked[#picked + 1] = { gem = list[i], category = cat }
			taken = taken + 1
			i = i + step
		end
	end
	return picked, byCategory
end

-- ---------------------------------------------------------------------------------------
-- Weapon and support selection
-- ---------------------------------------------------------------------------------------

-- Priority order for resolving a skill's `weaponTypes` set to one concrete base class.
local WEAPON_PRIORITY = {
	"Two Handed Axe", "Two Handed Sword", "Two Handed Mace", "Staff", "Warstaff",
	"One Handed Axe", "One Handed Sword", "Thrusting One Handed Sword", "One Handed Mace",
	"Claw", "Dagger", "Rune Dagger", "Sceptre", "Wand", "Bow", "Fishing Rod",
}

-- `weaponTypes` uses a few names that are not item base types.
local WEAPON_TYPE_TO_BASE = {
	["Thrusting One Handed Sword"] = "One Handed Sword",
	["Rune Dagger"] = "Dagger",
	["Warstaff"] = "Staff",
}

--- Chooses the weapon base class a skill can actually be used with.
-- Returns `nil` for a skill with no weapon restriction (the caller picks a default) and
-- `"None"` for one that must be used unarmed.
function m.weaponTypeForSkill(gemData)
	local weapons = gemData.grantedEffect.weaponTypes
	if not weapons then
		return nil
	end
	for _, name in ipairs(WEAPON_PRIORITY) do
		if weapons[name] then
			return WEAPON_TYPE_TO_BASE[name] or name
		end
	end
	if weapons["None"] then
		return "None"
	end
	return nil
end

--- True when a skill can only be used while dual wielding, so the generator knows to put a
--- second weapon on before deciding the build produced no damage.
function m.requiresDualWield(gemData)
	local t = gemData.grantedEffect.skillTypes or {}
	return t[SkillType.DualWieldOnly] == true
end

local attackForWeaponCache = {}

--- Picks an attack skill that can actually be used with a given weapon base class.
-- The weapon sweep is meant to vary the weapon and nothing else, so pairing every base with
-- the same melee skill would have produced five builds whose only finding is "a bow cannot
-- swing a Molten Strike".
function m.attackSkillForWeapon(weaponType)
	if attackForWeaponCache[weaponType] ~= nil then
		return attackForWeaponCache[weaponType]
	end
	local names = {}
	for _, gemData in pairs(data.gems) do
		if isUsableActive(gemData) then
			local ge = gemData.grantedEffect
			local t = ge.skillTypes or {}
			if t[SkillType.Attack] and not t[SkillType.Vaal] and not t[SkillType.Triggered]
				and not t[SkillType.SummonsTotem] and not t[SkillType.Movement]
				and not t[SkillType.DualWieldOnly]
				and ge.weaponTypes and ge.weaponTypes[weaponType]
			then
				names[#names + 1] = gemData.name
			end
		end
	end
	table.sort(names)
	attackForWeaponCache[weaponType] = names[1] or false
	return attackForWeaponCache[weaponType]
end

--- Chooses supports appropriate to a skill's category.
-- A support the skill cannot take is not an error - the engine simply does not apply it -
-- but matching the category keeps most of the sample doing real damage, which is what makes
-- the numbers worth comparing.
local CATEGORY_SUPPORTS = {
	["attack-melee"] = { "Melee Physical Damage", "Multistrike", "Increased Critical Strikes" },
	["attack-slam"] = { "Melee Physical Damage", "Increased Area of Effect", "Increased Critical Strikes" },
	["attack-bow"] = { "Vicious Projectiles", "Greater Multiple Projectiles", "Increased Critical Strikes" },
	["attack-projectile"] = { "Vicious Projectiles", "Faster Attacks", "Increased Critical Strikes" },
	["attack-other"] = { "Faster Attacks", "Added Fire Damage", "Increased Critical Strikes" },
	["spell-area"] = { "Controlled Destruction", "Concentrated Effect", "Spell Echo" },
	["spell-projectile"] = { "Controlled Destruction", "Greater Multiple Projectiles", "Spell Echo" },
	["spell-dot"] = { "Efficacy", "Swift Affliction", "Controlled Destruction" },
	["spell-other"] = { "Controlled Destruction", "Increased Critical Strikes", "Spell Echo" },
	["minion"] = { "Minion Damage", "Minion Speed", "Melee Splash" },
	["totem"] = { "Controlled Destruction", "Elemental Focus" },
	["brand"] = { "Controlled Destruction", "Concentrated Effect" },
	["curse"] = { "Increased Area of Effect" },
	["warcry"] = { "Increased Duration" },
	["herald"] = {},
	["reservation"] = {},
	["guard"] = { "Increased Duration" },
	["golem"] = { "Minion Damage", "Minion Life" },
	["triggered"] = { "Increased Critical Strikes", "Controlled Destruction" },
	["channelled"] = { "Controlled Destruction", "Increased Critical Strikes" },
	["movement"] = { "Increased Duration" },
	["vaal"] = { "Increased Critical Strikes", "Increased Area of Effect" },
	["other"] = { "Increased Duration" },
}

function m.supportsForCategory(category)
	return CATEGORY_SUPPORTS[category] or {}
end

--- Weapon mod set that suits a category.
function m.weaponModsForCategory(category)
	if category == "minion" or category == "golem" then
		return "minion"
	end
	if category:match("^attack") then
		return "attack"
	end
	return "caster"
end

return m
