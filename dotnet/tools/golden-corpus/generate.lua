-- Golden output corpus generator (migration ticket 07).
--
-- Run from `src/`:
--
--   luajit ../dotnet/tools/golden-corpus/generate.lua
--
-- Writes one `<name>.golden.json[.gz]` per build into `dotnet/Pob.Tests/oracles/golden/`,
-- plus `manifest.json` describing the run. Options (environment variables):
--
--   POB_GOLDEN_OUT     output directory (default ../dotnet/Pob.Tests/oracles/golden)
--   POB_GOLDEN_LIMIT   stop after N builds - for a fast smoke run
--   POB_GOLDEN_FILTER  only generate builds whose name contains this substring
--   POB_GOLDEN_NOGZIP  set to 1 to leave the files uncompressed
--
-- What this is not: a modification of `spec/GenerateBuilds.lua`. That script reads whatever
-- `.xml` files happen to be in `spec/TestBuilds` and dumps `build.calcsTab.mainOutput`
-- rounded to four decimal places. Both of those are dead ends for a port oracle - the corpus
-- can only grow by hand, and the rounding cannot be undone once the file is written. This
-- generator constructs its own builds and writes full `%.17g` precision.

local ROOT = "../dotnet/tools/golden-corpus/"

package.path = "../runtime/lua/?.lua;../runtime/lua/?/init.lua;./?.lua;" .. package.path

dofile("HeadlessWrapper.lua")

local json = dofile(ROOT .. "json.lua")
local dumper = dofile(ROOT .. "dump.lua")
local chassis = dofile(ROOT .. "chassis.lua")
local matrix = dofile(ROOT .. "matrix.lua")
local xmlcanon = dofile(ROOT .. "xmlcanon.lua")

local SCHEMA_VERSION = 1
local OUT_DIR = os.getenv("POB_GOLDEN_OUT") or "../dotnet/Pob.Tests/oracles/golden"
local LIMIT = tonumber(os.getenv("POB_GOLDEN_LIMIT"))
local FILTER = os.getenv("POB_GOLDEN_FILTER")
local NOGZIP = os.getenv("POB_GOLDEN_NOGZIP") == "1"

local s_format = string.format

-- ---------------------------------------------------------------------------------------
-- Build assembly
-- ---------------------------------------------------------------------------------------

local TWO_HANDED = {
	["Two Handed Axe"] = true,
	["Two Handed Sword"] = true,
	["Two Handed Mace"] = true,
	["Staff"] = true,
	["Bow"] = true,
	["Fishing Rod"] = true,
}

local ARMOUR_SLOTS = { "Body Armour", "Helmet", "Gloves", "Boots", "Belt", "Amulet" }

--- Equips a full gear set. `spec.gearKit` selects the defensive flavour, `spec.weaponType`
--- the weapon base class (nil for the caster default, "None" for unarmed).
local function equipGear(b, spec, unparsed)
	local kit = matrix.ARMOUR_MODS[spec.gearKit or "life"]
	chassis.resetItemIds()

	for _, slotType in ipairs(ARMOUR_SLOTS) do
		local base = chassis.pickBase(slotType)
		chassis.equip(b, chassis.makeRare(base, "Corpus " .. slotType, kit[slotType], 20, unparsed))
	end
	-- Two rings, so the ring slots are both populated and the "two of the same mod"
	-- stacking path is exercised.
	for i = 1, 2 do
		local base = chassis.pickBase("Ring")
		chassis.equip(b, chassis.makeRare(base, "Corpus Ring " .. i, kit["Ring"], 0, unparsed))
	end

	local weaponType = spec.weaponType
	if weaponType and weaponType ~= "None" then
		local mods = matrix.WEAPON_MODS[spec.weaponMods or "attack"]
		chassis.equip(b, chassis.makeRare(chassis.pickBase(weaponType), "Corpus Main", mods, 20, unparsed))
		if spec.dualWield and not TWO_HANDED[weaponType] then
			chassis.equipInSlot(b, chassis.makeRare(chassis.pickBase(weaponType), "Corpus Off", mods, 20, unparsed), "Weapon 2")
		end
	end

	local offhand = spec.offhand
	if offhand == nil then
		if weaponType == "Bow" then
			offhand = "Quiver"
		elseif spec.dualWield then
			offhand = nil
		elseif not weaponType or not TWO_HANDED[weaponType] then
			offhand = "Shield"
		end
	elseif offhand == "none" then
		offhand = nil
	end
	if offhand then
		chassis.equip(b, chassis.makeRare(chassis.pickBase(offhand), "Corpus " .. offhand, kit[offhand], 20, unparsed))
	end
end

--- Reads a build XML straight off disk. Used for the five hand-authored builds already in
--- `spec/TestBuilds/3.13`, which are real exported characters and therefore cover item and
--- skill combinations no generator would invent.
local function readFile(path)
	local handle, err = io.open(path, "rb")
	if not handle then
		error("cannot read " .. path .. ": " .. tostring(err))
	end
	local text = handle:read("*a")
	handle:close()
	return text
end

--- Constructs, calculates and captures a single build. Returns the golden record.
local function buildOne(spec)
	chassis.resetErrorState()
	if spec.xmlPath then
		local xmlText = readFile(spec.xmlPath)
		loadBuildFromXML(xmlText, spec.name)
		chassis.assertNoEngineError("loading " .. spec.xmlPath)
		local sections, census = dumper.captureActors(build)
		local mainOutput = build.calcsTab.mainOutput
		return {
			__order = { "schema", "name", "group", "notes", "inputXml", "sections" },
			schema = SCHEMA_VERSION,
			name = spec.name,
			group = spec.group,
			notes = {
				__order = { "treeVersion", "source" },
				treeVersion = build.spec.treeVersion,
				source = spec.xmlPath,
				-- The `.lua` file next to this XML holds values frozen in 3.13. They are
				-- NOT what is recorded here: `legacy-check.lua` shows the live engine has
				-- drifted from them, and a corpus of stale numbers is worse than none.
				frozenValuesAreStale = true,
				gearKit = "n/a",
				weaponType = "n/a",
				keystones = json.array({}),
				unparsedModLines = json.array({}),
				gemErrors = json.array({}),
				totalDps = mainOutput.TotalDPS or 0,
				fullDps = mainOutput.FullDPS or 0,
				life = mainOutput.Life or 0,
				energyShield = mainOutput.EnergyShield or 0,
				keyCount = census.keys,
			},
			inputXml = xmlText,
			sections = sections,
		}, census
	end

	newBuild()
	chassis.assertNoEngineError("newBuild")
	local b = build
	b.characterLevel = spec.level or 92
	b.characterLevelAutoMode = false

	local shape = {
		classId = spec.classId,
		ascendId = spec.ascendId,
		keystones = spec.keystones,
		budget = spec.budget or 88,
		ascendPoints = spec.ascendPoints or 8,
	}
	local nodes = chassis.treeNodes(b, shape)
	chassis.applyTree(b, shape, nodes)

	local unparsed = {}
	equipGear(b, spec, unparsed)

	local gemErrors = {}
	local mainIndex
	for i, group in ipairs(spec.groups) do
		local index, errors = chassis.addSocketGroup(b, group)
		for _, err in ipairs(errors) do
			gemErrors[#gemErrors + 1] = err
		end
		if index and (group.main or (i == 1 and not mainIndex)) then
			mainIndex = index
		end
	end
	if not mainIndex then
		error("no socket group could be created: " .. table.concat(gemErrors, "; "))
	end
	b.mainSocketGroup = mainIndex

	for key, value in pairs(spec.config or {}) do
		b.configTab.input[key] = value
	end
	b.configTab:BuildModList()

	b.buildFlag = true
	chassis.frame("calculating the constructed build")

	-- Deliberately not `b:SaveDB()`: see xmlcanon.lua. The saved bytes have to be identical
	-- from run to run or the corpus cannot be diffed or hashed.
	local xmlText = xmlcanon.saveBuild(b)

	-- Reload from exactly the text that will be stored, so the recorded input provably
	-- produces the recorded output through the normal load path.
	loadBuildFromXML(xmlText, spec.name)
	chassis.assertNoEngineError("reloading the serialised build")

	local sections, census = dumper.captureActors(build)
	local mainOutput = build.calcsTab.mainOutput

	local sectionsOut = {}
	for name, flat in pairs(sections) do
		sectionsOut[name] = flat
	end

	return {
		__order = { "schema", "name", "group", "notes", "inputXml", "sections" },
		schema = SCHEMA_VERSION,
		name = spec.name,
		group = spec.group,
		notes = {
			__order = { "treeVersion", "classId", "ascendClassId", "level", "allocatedNodes" },
			treeVersion = build.spec.treeVersion,
			classId = spec.classId,
			ascendClassId = spec.ascendId,
			level = b.characterLevel,
			allocatedNodes = #nodes,
			gearKit = spec.gearKit or "life",
			weaponType = spec.weaponType or "none",
			mainSkill = spec.mainSkill,
			category = spec.category,
			keystones = json.array(spec.keystones or {}),
			unparsedModLines = json.array(unparsed),
			gemErrors = json.array(gemErrors),
			totalDps = mainOutput.TotalDPS or 0,
			fullDps = mainOutput.FullDPS or 0,
			life = mainOutput.Life or 0,
			energyShield = mainOutput.EnergyShield or 0,
			keyCount = census.keys,
		},
		inputXml = xmlText,
		sections = sectionsOut,
	}, census
end

-- ---------------------------------------------------------------------------------------
-- The matrix
-- ---------------------------------------------------------------------------------------

local usedNames = {}

local function slug(text)
	local s = text:lower():gsub("[^%w]+", "-"):gsub("^%-+", ""):gsub("%-+$", "")
	return s
end

local function uniqueName(prefix, text)
	local base = prefix .. "-" .. slug(text)
	local name = base
	local n = 1
	while usedNames[name] do
		n = n + 1
		name = base .. "-" .. n
	end
	usedNames[name] = true
	return name
end

--- Reference chassis: a Duelist Slayer with a two-handed axe. Used wherever the build is a
--- control and only one dimension is meant to vary.
local function controlSpec(overrides)
	local spec = {
		classId = 4,
		ascendId = 1,
		gearKit = "life",
		weaponType = "Two Handed Axe",
		weaponMods = "attack",
		mainSkill = "Cleave",
		category = "attack-melee",
		groups = {
			{ gems = {
				{ name = "Cleave" }, { name = "Melee Physical Damage" },
				{ name = "Multistrike" }, { name = "Pulverise" }, { name = "Impale" },
			} },
		},
	}
	for k, v in pairs(overrides or {}) do
		spec[k] = v
	end
	return spec
end

local function skillGroup(gemData, category)
	local gems = { { name = gemData.name, level = math.min(20, gemData.naturalMaxLevel or 20) } }
	for _, support in ipairs(matrix.supportsForCategory(category)) do
		gems[#gems + 1] = { name = support }
	end
	return { gems = gems }
end

--- Builds the whole spec list. Order is fixed, so the corpus file set is reproducible.
local function buildMatrix(b)
	local specs = {}

	-- 1. Class x ascendancy. Every start position and every ascendancy start node, which is
	--    the cheapest broad coverage of PassiveSpec and the ascendancy mod sources.
	local classIds = {}
	for id in pairs(b.spec.tree.classes) do
		classIds[#classIds + 1] = id
	end
	table.sort(classIds)
	for _, classId in ipairs(classIds) do
		local class = b.spec.tree.classes[classId]
		local ascendIds = {}
		for id in pairs(class.classes) do
			if id > 0 then
				ascendIds[#ascendIds + 1] = id
			end
		end
		table.sort(ascendIds)
		for _, ascendId in ipairs(ascendIds) do
			local ascend = class.classes[ascendId]
			specs[#specs + 1] = controlSpec({
				name = uniqueName("class", class.name .. "-" .. ascend.name),
				group = "class-ascendancy",
				classId = classId,
				ascendId = ascendId,
			})
		end
	end

	-- 2. Keystones. One build per keystone on the control chassis, so a keystone's effect is
	--    the only difference between its build and the control. This is the single highest
	--    value-per-line group in the corpus: keystones are where the engine's conditional
	--    branches live.
	local keystoneNames = {}
	local seenKeystone = {}
	for _, node in pairs(b.spec.nodes) do
		if node.type == "Keystone" and not node.ascendancyName and node.dn and not seenKeystone[node.dn] then
			seenKeystone[node.dn] = true
			keystoneNames[#keystoneNames + 1] = node.dn
		end
	end
	table.sort(keystoneNames)
	for _, keystone in ipairs(keystoneNames) do
		specs[#specs + 1] = controlSpec({
			name = uniqueName("keystone", keystone),
			group = "keystone",
			keystones = { keystone },
		})
	end

	-- 3. Skills. A stratified sample of the gem list: see matrix.CATEGORY_QUOTA.
	local sample = matrix.gemSample()
	for _, entry in ipairs(sample) do
		local gemData, category = entry.gem, entry.category
		local weaponType = matrix.weaponTypeForSkill(gemData)
		local isMinionish = category == "minion" or category == "golem"
		specs[#specs + 1] = {
			name = uniqueName("skill", gemData.name),
			group = "skill",
			category = category,
			mainSkill = gemData.name,
			-- Witch/Necromancer for minions (so the ascendancy actually feeds
			-- env.minion), Duelist/Slayer for everything else.
			classId = isMinionish and 3 or 4,
			ascendId = isMinionish and 3 or 1,
			gearKit = isMinionish and "energyShield" or "life",
			weaponType = weaponType ~= "None" and (weaponType or "Sceptre") or nil,
			weaponMods = matrix.weaponModsForCategory(category),
			dualWield = matrix.requiresDualWield(gemData),
			groups = { skillGroup(gemData, category) },
		}
	end

	-- 4. Weapons. One build per weapon base class plus the three configurations that change
	--    which weapon-slot code runs at all: dual wield, sword-and-board, unarmed.
	local weaponTypes = {}
	for name, base in pairs(data.itemBases) do
		if base.weapon and not base.influence and not weaponTypes[base.type] then
			weaponTypes[base.type] = true
		end
	end
	local weaponTypeNames = {}
	for name in pairs(weaponTypes) do
		weaponTypeNames[#weaponTypeNames + 1] = name
	end
	table.sort(weaponTypeNames)
	for _, weaponType in ipairs(weaponTypeNames) do
		-- The skill has to be one the weapon can actually swing, or the build's only
		-- finding is that a bow cannot use a melee strike.
		local skillName = matrix.attackSkillForWeapon(weaponType)
		specs[#specs + 1] = controlSpec({
			name = uniqueName("weapon", weaponType),
			group = "weapon",
			weaponType = weaponType,
			mainSkill = skillName or "Molten Strike",
			groups = { { gems = {
				{ name = skillName or "Molten Strike" },
				{ name = "Faster Attacks" },
				{ name = "Increased Critical Strikes" },
			} } },
		})
	end
	for _, variant in ipairs({
		{ suffix = "dual-wield-sword", weaponType = "One Handed Sword", dualWield = true },
		{ suffix = "dual-wield-claw", weaponType = "Claw", dualWield = true,
			groups = { { gems = { { name = "Dual Strike" }, { name = "Melee Physical Damage" }, { name = "Multistrike" } } } } },
		{ suffix = "one-hand-and-shield", weaponType = "One Handed Mace", offhand = "Shield",
			groups = { { gems = { { name = "Shield Crush" }, { name = "Melee Physical Damage" }, { name = "Increased Critical Strikes" } } } } },
		{ suffix = "unarmed", weaponType = nil, offhand = "Shield" },
		{ suffix = "bow-and-quiver", weaponType = "Bow", offhand = "Quiver",
			groups = { { gems = { { name = "Lightning Arrow" }, { name = "Vicious Projectiles" }, { name = "Mirage Archer" } } } } },
	}) do
		specs[#specs + 1] = controlSpec({
			name = uniqueName("weapon", variant.suffix),
			group = "weapon",
			weaponType = variant.weaponType,
			dualWield = variant.dualWield,
			offhand = variant.offhand,
			groups = variant.groups,
		})
	end

	-- 5. Defence. The gear kits crossed with the keystones that redefine how a pool works.
	for _, variant in ipairs({
		{ suffix = "life", gearKit = "life" },
		{ suffix = "energy-shield", gearKit = "energyShield", classId = 3, ascendId = 1 },
		{ suffix = "hybrid", gearKit = "hybrid" },
		{ suffix = "armour", gearKit = "armour", classId = 1, ascendId = 1 },
		{ suffix = "evasion", gearKit = "evasion", classId = 2, ascendId = 2 },
		{ suffix = "chaos-inoculation", gearKit = "energyShield", classId = 3, ascendId = 1, keystones = { "Chaos Inoculation" } },
		{ suffix = "mind-over-matter", gearKit = "hybrid", classId = 5, ascendId = 2, keystones = { "Mind Over Matter" } },
		{ suffix = "eldritch-battery", gearKit = "energyShield", classId = 3, ascendId = 2, keystones = { "Eldritch Battery" } },
		{ suffix = "blood-magic", gearKit = "life", classId = 1, ascendId = 3, keystones = { "Blood Magic" } },
		{ suffix = "iron-reflexes", gearKit = "evasion", classId = 4, ascendId = 3, keystones = { "Iron Reflexes" } },
		{ suffix = "acrobatics", gearKit = "evasion", classId = 6, ascendId = 2, keystones = { "Acrobatics" } },
		{ suffix = "glancing-blows", gearKit = "life", classId = 4, ascendId = 2, keystones = { "Glancing Blows" }, offhand = "Shield", weaponType = "One Handed Sword" },
	}) do
		specs[#specs + 1] = controlSpec({
			name = uniqueName("defence", variant.suffix),
			group = "defence",
			gearKit = variant.gearKit,
			classId = variant.classId or 4,
			ascendId = variant.ascendId or 1,
			keystones = variant.keystones,
			weaponType = variant.weaponType or "Two Handed Axe",
			offhand = variant.offhand,
		})
	end

	-- 6. Mechanics that only exist as a *link* between gems, so no amount of sampling the
	--    gem list one skill at a time reaches them: triggers (CalcTriggers), mirages
	--    (CalcMirages), totem/trap/mine conversion, and the ailment paths that need a
	--    specific support to produce a number at all.
	for _, variant in ipairs({
		{ suffix = "trigger-cast-on-critical", weaponType = "Two Handed Sword", gems = {
			"Cyclone", "Cast On Critical Strike", "Ice Nova", "Increased Critical Strikes" } },
		{ suffix = "trigger-cast-while-channelling", weaponType = "Two Handed Sword", gems = {
			"Cyclone", "Cast while Channelling", "Ice Nova", "Controlled Destruction" } },
		{ suffix = "trigger-cast-when-damage-taken", gems = {
			{ name = "Cast when Damage Taken", level = 1 }, { name = "Immortal Call", level = 1 },
			{ name = "Increased Duration" } } },
		{ suffix = "trigger-cast-on-melee-kill", gems = {
			"Cleave", "Cast on Melee Kill", "Fireball", "Controlled Destruction" } },
		{ suffix = "trigger-arcanist-brand", classId = 3, ascendId = 1, gearKit = "energyShield",
			weaponType = "Sceptre", weaponMods = "caster", gems = {
				"Arcanist Brand", "Frostbolt", "Controlled Destruction" } },
		{ suffix = "trigger-mirage-archer", weaponType = "Bow", offhand = "Quiver", gems = {
			"Lightning Arrow", "Mirage Archer", "Vicious Projectiles" } },
		{ suffix = "trigger-manaforged-arrows", weaponType = "Bow", offhand = "Quiver", gems = {
			"Barrage", "Manaforged Arrows", "Lightning Arrow" } },
		{ suffix = "totem-spell", classId = 5, ascendId = 2, gearKit = "hybrid",
			weaponType = "Sceptre", weaponMods = "caster", gems = {
				"Spell Totem", "Fireball", "Controlled Destruction" } },
		{ suffix = "totem-ballista", weaponType = "Bow", offhand = "Quiver", gems = {
			"Ballista Totem", "Lightning Arrow", "Vicious Projectiles" } },
		{ suffix = "trap-fireball", classId = 6, ascendId = 3, gearKit = "energyShield",
			weaponType = "Dagger", weaponMods = "caster", gems = {
				"Fireball", "Trap", "Trap and Mine Damage" } },
		{ suffix = "mine-fireball", classId = 6, ascendId = 3, gearKit = "energyShield",
			weaponType = "Dagger", weaponMods = "caster", gems = {
				"Fireball", "Blastchain Mine", "Trap and Mine Damage" } },
		{ suffix = "ailment-ignite", classId = 3, ascendId = 2, gearKit = "energyShield",
			weaponType = "Sceptre", weaponMods = "caster", gems = {
				"Fireball", "Burning Damage", "Combustion", "Elemental Focus" },
			config = { ailmentMode = "AVERAGE" } },
		{ suffix = "ailment-poison", weaponType = "Dagger", gems = {
			"Viper Strike", "Unbound Ailments", "Void Manipulation", "Vile Toxins" } },
		{ suffix = "ailment-bleed", gems = {
			"Cleave", "Chance to Bleed", "Brutality", "Melee Physical Damage" } },
		{ suffix = "ailment-shock", classId = 3, ascendId = 2, gearKit = "energyShield",
			weaponType = "Sceptre", weaponMods = "caster", gems = {
				"Arc", "Increased Critical Strikes", "Controlled Destruction" } },
		{ suffix = "ailment-freeze", classId = 3, ascendId = 2, gearKit = "energyShield",
			weaponType = "Sceptre", weaponMods = "caster", gems = {
				"Ice Nova", "Hypothermia", "Controlled Destruction" } },
		{ suffix = "minion-spectre-chassis", classId = 3, ascendId = 3, gearKit = "energyShield",
			weaponType = "Sceptre", weaponMods = "minion", gems = {
				"Raise Zombie", "Minion Damage", "Melee Physical Damage", "Feeding Frenzy" } },
		{ suffix = "multi-group-full-dps", weaponType = "Two Handed Axe", groups = {
			{ gems = { { name = "Cleave" }, { name = "Melee Physical Damage" } }, main = true },
			{ gems = { { name = "Ancestral Warchief" }, { name = "Melee Physical Damage" } } },
			{ gems = { { name = "Herald of Ash" } } },
			{ gems = { { name = "Hatred" } } },
		} },
	}) do
		local groups = variant.groups
		if not groups then
			local gems = {}
			for _, gem in ipairs(variant.gems) do
				gems[#gems + 1] = type(gem) == "string" and { name = gem } or gem
			end
			groups = { { gems = gems } }
		end
		specs[#specs + 1] = controlSpec({
			name = uniqueName("mechanic", variant.suffix),
			group = "mechanic",
			classId = variant.classId or 4,
			ascendId = variant.ascendId or 1,
			gearKit = variant.gearKit or "life",
			weaponType = variant.weaponType or "Two Handed Axe",
			weaponMods = variant.weaponMods or "attack",
			offhand = variant.offhand,
			config = variant.config,
			mainSkill = variant.suffix,
			groups = groups,
		})
	end

	-- 7. Configuration. The Config tab is a mod source like any other, and every one of
	--    these toggles reaches a different branch of CalcPerform.
	for _, variant in ipairs({
		{ suffix = "boss-pinnacle", config = { enemyIsBoss = "Pinnacle" } },
		{ suffix = "boss-uber", config = { enemyIsBoss = "Uber Pinnacle" } },
		{ suffix = "boss-none", config = { enemyIsBoss = "None" } },
		{ suffix = "enemy-shocked", config = { conditionEnemyShocked = true, ShockedConfig = 20 } },
		{ suffix = "enemy-chilled", config = { conditionEnemyChilled = true, ChilledConfig = 30 } },
		{ suffix = "enemy-frozen", config = { conditionEnemyFrozen = true } },
		{ suffix = "enemy-ignited", config = { conditionEnemyIgnited = true } },
		{ suffix = "enemy-poisoned", config = { conditionEnemyPoisoned = true } },
		{ suffix = "enemy-bleeding", config = { conditionEnemyBleeding = true } },
		{ suffix = "enemy-cursed", config = { conditionEnemyCursed = true } },
		{ suffix = "enemy-intimidated", config = { conditionEnemyIntimidated = true } },
		{ suffix = "enemy-maimed", config = { conditionEnemyMaimed = true } },
		{ suffix = "enemy-scorched", config = { conditionEnemyScorched = true, ScorchedConfig = 30 } },
		{ suffix = "enemy-brittle", config = { conditionEnemyBrittle = true, BrittleConfig = 15 } },
		{ suffix = "enemy-sapped", config = { conditionEnemySapped = true, SappedConfig = 20 } },
		{ suffix = "self-onslaught", config = { buffOnslaught = true } },
		{ suffix = "self-fortification", config = { buffFortification = true } },
		{ suffix = "self-elusive", config = { buffElusive = true } },
		{ suffix = "self-unholy-might", config = { buffUnholyMight = true } },
		{ suffix = "self-phasing", config = { buffPhasing = true } },
		{ suffix = "self-low-life", config = { conditionLowLife = true } },
		{ suffix = "self-full-life", config = { conditionFullLife = true } },
		{ suffix = "self-leeching", config = { conditionLeeching = true } },
		{ suffix = "self-moving", config = { conditionMoving = true } },
		{ suffix = "self-stationary", config = { conditionStationary = true } },
		{ suffix = "self-killed-recently", config = { conditionKilledRecently = true } },
		{ suffix = "self-crit-recently", config = { conditionCritRecently = true } },
		{ suffix = "self-rage", config = { CanGainRage = true } },
		{ suffix = "ailment-mode-average", config = { ailmentMode = "AVERAGE" } },
		{ suffix = "unbuffed", config = { misc_buffMode = "UNBUFFED" } },
	}) do
		specs[#specs + 1] = controlSpec({
			name = uniqueName("config", variant.suffix),
			group = "config",
			config = variant.config,
		})
	end

	-- 8. The five builds that were already here. They are real exported characters - gear,
	--    skill links and tree choices no generator would produce - and they load a 3.13 tree,
	--    so they are the corpus's only coverage of the tree-version conversion path. Kept
	--    last so the tree the rest of the matrix uses is never swapped out mid-run.
	local listing = io.popen('ls "../spec/TestBuilds/3.13"')
	local legacyNames = {}
	if listing then
		for file in listing:lines() do
			local stem = file:match("^(.+)%.xml$")
			if stem then
				legacyNames[#legacyNames + 1] = stem
			end
		end
		listing:close()
	end
	table.sort(legacyNames)
	for _, stem in ipairs(legacyNames) do
		specs[#specs + 1] = {
			name = uniqueName("legacy", stem),
			group = "legacy",
			xmlPath = "../spec/TestBuilds/3.13/" .. stem .. ".xml",
		}
	end

	return specs
end

-- ---------------------------------------------------------------------------------------
-- Driver
-- ---------------------------------------------------------------------------------------

local function writeFile(path, text)
	local handle, err = io.open(path, "wb")
	if not handle then
		error("cannot write " .. path .. ": " .. tostring(err))
	end
	handle:write(text)
	handle:close()
end

local function main()
	chassis.resetErrorState()
	newBuild()
	chassis.assertNoEngineError("initial newBuild")
	-- Read before anything runs: the legacy builds at the end of the matrix load a 3.13
	-- tree, so asking `build.spec` afterwards reports whichever tree was loaded last.
	local defaultTreeVersion = build.spec.treeVersion
	local specs = buildMatrix(build)

	os.execute('mkdir -p "' .. OUT_DIR .. '"')
	-- Stale files from a previous run whose build no longer exists would otherwise stay in
	-- the corpus forever and be replayed as if they were current.
	os.execute('rm -f "' .. OUT_DIR .. '"/*.golden.json "' .. OUT_DIR .. '"/*.golden.json.gz')

	local summaries = {}
	local failures = {}
	local groupCounts = {}
	local categoryCounts = {}
	local totals = { numbers = 0, booleans = 0, strings = 0, anomalies = 0, keys = 0 }
	local generated = 0
	local zeroDps = 0
	local unparsedTotal = 0
	local startClock = os.clock()

	for index, spec in ipairs(specs) do
		if (not FILTER or spec.name:find(FILTER, 1, true)) and (not LIMIT or generated < LIMIT) then
			local ok, record, census = pcall(buildOne, spec)
			if ok then
				local path = OUT_DIR .. "/" .. spec.name .. ".golden.json"
				writeFile(path, json.encode(record))
				generated = generated + 1
				for key in pairs(totals) do
					totals[key] = totals[key] + (census[key] or 0)
				end
				groupCounts[spec.group] = (groupCounts[spec.group] or 0) + 1
				if spec.category then
					categoryCounts[spec.category] = (categoryCounts[spec.category] or 0) + 1
				end
				local notes = record.notes
				if (notes.totalDps or 0) == 0 and (notes.fullDps or 0) == 0 then
					zeroDps = zeroDps + 1
				end
				unparsedTotal = unparsedTotal + #(notes.unparsedModLines.__array)
				local sectionCount = 0
				for _ in pairs(record.sections) do
					sectionCount = sectionCount + 1
				end
				summaries[#summaries + 1] = {
					__order = { "name", "group", "keyCount", "totalDps", "fullDps", "life" },
					name = spec.name,
					group = spec.group,
					category = spec.category,
					mainSkill = spec.mainSkill,
					keyCount = notes.keyCount,
					totalDps = notes.totalDps,
					fullDps = notes.fullDps,
					life = notes.life,
					energyShield = notes.energyShield,
					sectionCount = sectionCount,
					unparsedModLines = #(notes.unparsedModLines.__array),
					gemErrors = #(notes.gemErrors.__array),
				}
				io.write(s_format("[%3d/%3d] %-52s %8d keys  dps=%.4g\n",
					index, #specs, spec.name, notes.keyCount, notes.totalDps or 0))
			else
				-- A build the engine cannot calculate is a finding about the engine or
				-- about this generator, not something to quietly drop.
				failures[#failures + 1] = {
					name = spec.name,
					group = spec.group,
					category = spec.category,
					mainSkill = spec.mainSkill,
					error = tostring(record),
				}
				io.write(s_format("[%3d/%3d] %-52s FAILED: %s\n", index, #specs, spec.name, tostring(record)))
			end
			io.flush()
		end
	end

	table.sort(summaries, function(a, b) return a.name < b.name end)
	table.sort(failures, function(a, b) return a.name < b.name end)

	local manifest = {
		__order = { "schema", "treeVersion", "buildCount", "failureCount", "groups", "categories", "totals", "builds", "failures" },
		schema = SCHEMA_VERSION,
		treeVersion = defaultTreeVersion,
		buildCount = generated,
		failureCount = #failures,
		zeroDpsCount = zeroDps,
		unparsedModLineCount = unparsedTotal,
		groups = groupCounts,
		categories = categoryCounts,
		totals = totals,
		builds = summaries,
		failures = json.array(failures),
	}
	writeFile(OUT_DIR .. "/manifest.json", json.encode(manifest))

	io.write(s_format("\n%d builds, %d failures, %d keys, %.1fs\n",
		generated, #failures, totals.keys, os.clock() - startClock))

	if not NOGZIP then
		-- -n keeps the original name and mtime out of the gzip header, which is what makes
		-- two runs produce byte-identical archives.
		local ok = os.execute('gzip -9nf "' .. OUT_DIR .. '"/*.golden.json')
		if ok == 0 or ok == true then
			io.write("compressed with gzip -9n\n")
		else
			io.write("gzip unavailable; corpus left uncompressed\n")
		end
	end
end

main()
