--[[
	Pure data pools for the differential fuzzer (migration ticket 08).

	Everything the recipe generator draws from is loaded HERE, straight off disk, and never
	from the running engine's `data` table. Two reasons:

	* Purity. `plan.lua` has to produce an identical recipe list under stock Lua 5.1 and
	  under LuaJIT. The engine only boots under LuaJIT (src/Launch.lua:18 calls `jit.opt`,
	  and src/Modules/Common.lua:19 needs LuaBitOp's `bit`), so anything reachable from the
	  generator must be plain `return { ... }` data.
	* Stability. If the generator drew from the engine, the recipes would silently change
	  whenever the engine's load order or filtering changed, and an old seed would stop
	  reproducing an old corpus.

	Every pool is a dense array in a deterministic order -- sorted, or the source file's own
	order -- never a `pairs` walk. Hash iteration order is unspecified in Lua and differs
	between the two interpreters; a single `pairs` leak here would break reproduction.

	The file lists below are hard-coded rather than globbed on purpose: a glob would make
	the pool depend on directory listing order and on whatever happens to be on disk, and
	the seed->corpus mapping has to be a function of the checked-in tree alone. Adding a
	file to a list is a deliberate, reviewable change to the corpus.
]]

local pools = {}

--- Mod databases, in this order. `ModCache.lua` is deliberately absent: it is the parser's
--- pre-parsed answer key (ticket 05), not a source of mod *text*. `ModFoulbornMap.lua` is a
--- name->name mapping, not mod lines.
local MOD_FILES = {
	"ModCorrupted", "ModDelve", "ModEldritch", "ModExplicit", "ModFlask",
	"ModFoulborn", "ModGraft", "ModItemExclusive", "ModJewel", "ModJewelAbyss",
	"ModJewelCharm", "ModJewelCluster", "ModMap", "ModMaster", "ModMercenary",
	"ModNecropolis", "ModScalability", "ModScourge", "ModSynthesis", "ModTincture",
	"ModVeiled",
}

--- Item base groups, in this order. Each entry becomes one equippable slot family.
--- `fishing`, `tincture` and `graft` are excluded: their slots either do not exist on a
--- default character or need dedicated setup that would just produce load failures.
local BASE_FILES = {
	"amulet", "axe", "belt", "body", "boots", "bow", "claw", "dagger", "flask",
	"gloves", "helmet", "jewel", "mace", "quiver", "ring", "shield", "staff",
	"sword", "wand",
}

--- Config-option types the fuzzer knows how to produce a value for. `list` options need
--- their value enumeration read out of the option table, which a text scan cannot do
--- safely; see README.md ("Not yet fuzzed").
local CONFIG_TYPES = { check = true, count = true, integer = true, float = true }

local function readFile(path)
	local f = io.open(path, "rb")
	if not f then
		error("fuzz/pools: cannot open " .. path)
	end
	local text = f:read("*a")
	f:close()
	return (text:gsub("\r\n", "\n"))
end

local function loadData(path)
	local chunk = loadfile(path)
	if not chunk then
		error("fuzz/pools: cannot load " .. path)
	end
	local ok, result = pcall(chunk)
	if not ok then
		error("fuzz/pools: error loading " .. path .. ": " .. tostring(result))
	end
	return result
end

--------------------------------------------------------------------------------------
-- Tree
--------------------------------------------------------------------------------------

--- The latest tree version, read out of src/GameVersions.lua rather than hard-coded, so a
--- tree bump does not silently leave the fuzzer on a stale tree. That file only assigns
--- globals, so it loads standalone.
local function loadTreeVersion(repoRoot)
	local chunk = loadfile(repoRoot .. "/src/GameVersions.lua")
	if not chunk then
		error("fuzz/pools: cannot load src/GameVersions.lua")
	end
	chunk()
	local version = _G.latestTreeVersion
	if type(version) ~= "string" then
		error("fuzz/pools: latestTreeVersion not set by src/GameVersions.lua")
	end
	return version
end

--- Allocatable tree node ids, ascending.
---
--- Filtered to what a fuzz build can actually take: class-start nodes are allocated for
--- free, ascendancy nodes are reached through the ascendancy start rather than the tree
--- proper, masteries need an effect choice popup, and proxy/expansion nodes only exist
--- once a cluster jewel is socketed. Anything left that the engine turns out not to
--- expose is skipped by the runner and counted, so an over-inclusive filter costs a
--- recorded miss rather than a crash.
local function loadNodeIds(repoRoot, treeVersion)
	local tree = loadData(repoRoot .. "/src/TreeData/" .. treeVersion .. "/tree.lua")
	local ids = {}
	for _, node in pairs(tree.nodes) do
		local id = tonumber(node.skill)
		if id
			and node.name
			and not node.isMastery
			and not node.isProxy
			and not node.isBlighted
			and node.ascendancyName == nil
			and node.classStartIndex == nil
			and node.expansionJewel == nil
		then
			ids[#ids + 1] = id
		end
	end
	table.sort(ids)

	local classes = {}
	for index, cls in ipairs(tree.classes) do
		classes[index] = {
			classId = index - 1,
			name = cls.name,
			ascendancyCount = cls.ascendancies and #cls.ascendancies or 0,
		}
	end

	return ids, classes
end

--------------------------------------------------------------------------------------
-- Gems
--------------------------------------------------------------------------------------

--- SkillsTab:PasteSocketGroup parses gem names with `([ %a']+) (%d+)/(%d+) ?(%a*) (%d+)`
--- (src/Classes/SkillsTab.lua:720), so a name containing a digit, comma or hyphen simply
--- would not be recognised. Filtering here keeps the recipe honest: every gem in a plan
--- is one the paste path can actually round-trip.
local function loadGems(repoRoot)
	local gems = loadData(repoRoot .. "/src/Data/Gems.lua")
	local active, support = {}, {}
	local seen = {}
	for _, gem in pairs(gems) do
		local name = gem.name
		if type(name) == "string" and name:match("^[ %a']+$") and not seen[name] then
			seen[name] = true
			local entry = {
				name = name,
				maxLevel = tonumber(gem.naturalMaxLevel) or 20,
			}
			if gem.tags and gem.tags.support then
				support[#support + 1] = entry
			elseif gem.tags and gem.tags.grants_active_skill then
				active[#active + 1] = entry
			end
		end
	end
	local byName = function(a, b) return a.name < b.name end
	table.sort(active, byName)
	table.sort(support, byName)
	return active, support
end

--------------------------------------------------------------------------------------
-- Item bases
--------------------------------------------------------------------------------------

local function loadBases(repoRoot)
	local groups = {}
	for _, file in ipairs(BASE_FILES) do
		local chunk = loadfile(repoRoot .. "/src/Data/Bases/" .. file .. ".lua")
		if not chunk then
			error("fuzz/pools: cannot load src/Data/Bases/" .. file .. ".lua")
		end
		local apply = chunk()
		if type(apply) ~= "function" then
			error("fuzz/pools: src/Data/Bases/" .. file .. ".lua did not return a function")
		end
		local bases = {}
		apply(bases)

		local list = {}
		for name, base in pairs(bases) do
			list[#list + 1] = { name = name, itemType = base.type, tags = base.tags or {} }
		end
		table.sort(list, function(a, b) return a.name < b.name end)
		groups[#groups + 1] = { file = file, bases = list }
	end
	return groups
end

--------------------------------------------------------------------------------------
-- Mod lines
--------------------------------------------------------------------------------------

--- Every mod entry, flattened to (source file, mod id, its stat lines, its weighting).
--- Ordered by the MOD_FILES order and then by mod id, so an entry's index in the pool is
--- a pure function of the checked-in data.
local function loadMods(repoRoot)
	local mods = {}
	for _, file in ipairs(MOD_FILES) do
		local db = loadData(repoRoot .. "/src/Data/" .. file .. ".lua")
		local ids = {}
		for id in pairs(db) do
			ids[#ids + 1] = id
		end
		table.sort(ids)
		for _, id in ipairs(ids) do
			local entry = db[id]
			-- The stat lines live in the array part; everything else is metadata.
			local lines = {}
			for i = 1, #entry do
				if type(entry[i]) == "string" and entry[i] ~= "" then
					lines[#lines + 1] = entry[i]
				end
			end
			if #lines > 0 then
				mods[#mods + 1] = {
					source = file,
					id = id,
					lines = lines,
					weightKey = entry.weightKey,
					weightVal = entry.weightVal,
				}
			end
		end
	end
	return mods
end

--- PoB's own affix-eligibility rule: walk `weightKey` in order, stop at the first key that
--- the base carries (or the catch-all "default"), and the mod can roll only if that
--- entry's weight is above zero. A mod with no weighting at all is treated as universal.
function pools.modFitsBase(mod, base)
	if not mod.weightKey or not mod.weightVal then
		return true
	end
	for i = 1, #mod.weightKey do
		local key = mod.weightKey[i]
		if base.tags[key] or key == "default" then
			return (mod.weightVal[i] or 0) > 0
		end
	end
	return false
end

--------------------------------------------------------------------------------------
-- Config options
--------------------------------------------------------------------------------------

--- src/Modules/ConfigOptions.lua cannot be `dofile`d standalone -- it interpolates
--- `data.monsterLifeTable` into a tooltip at load time (line 155) and closes over engine
--- globals. So the option list is scanned out of the source text instead, in file order,
--- exactly as dotnet/tools/luacompat-oracle/dump.lua extracts helper bodies out of
--- Common.lua rather than retyping them. If upstream renames a var the pool changes with
--- it; if upstream stops using the `{ var = "x", type = "y"` spelling this returns fewer
--- options and `pools.load` fails the sanity floor below rather than silently shrinking.
local function loadConfigVars(repoRoot)
	local source = readFile(repoRoot .. "/src/Modules/ConfigOptions.lua")
	local vars = {}
	local seen = {}
	for name, kind in source:gmatch('{%s*var%s*=%s*"([%w_]+)"%s*,%s*type%s*=%s*"(%a+)"') do
		if CONFIG_TYPES[kind] and not seen[name] then
			seen[name] = true
			vars[#vars + 1] = { var = name, kind = kind }
		end
	end
	return vars
end

--------------------------------------------------------------------------------------

--- Load every pool. `repoRoot` is the repository root (the directory holding `src/`).
function pools.load(repoRoot)
	local treeVersion = loadTreeVersion(repoRoot)
	local nodeIds, classes = loadNodeIds(repoRoot, treeVersion)
	local activeGems, supportGems = loadGems(repoRoot)
	local baseGroups = loadBases(repoRoot)
	local mods = loadMods(repoRoot)
	local configVars = loadConfigVars(repoRoot)

	-- Sanity floors. These are not style checks: a pool that quietly collapsed to a
	-- handful of entries would still produce a "reproducible" corpus, just a worthless
	-- one, and the failure would be invisible in the output.
	local function floor(name, count, minimum)
		if count < minimum then
			error(string.format(
				"fuzz/pools: %s pool has only %d entries (expected at least %d) -- upstream data layout probably changed",
				name, count, minimum))
		end
	end
	floor("node", #nodeIds, 1000)
	floor("class", #classes, 7)
	floor("active gem", #activeGems, 200)
	floor("support gem", #supportGems, 100)
	floor("base group", #baseGroups, #BASE_FILES)
	floor("mod", #mods, 10000)
	floor("config var", #configVars, 300)

	local baseCount = 0
	for _, group in ipairs(baseGroups) do
		baseCount = baseCount + #group.bases
	end

	return {
		--- Attached so `plan.lua` can reach the eligibility rule through the same table it
		--- draws pools from, rather than holding a second reference to this module.
		modFitsBase = pools.modFitsBase,
		treeVersion = treeVersion,
		nodeIds = nodeIds,
		classes = classes,
		activeGems = activeGems,
		supportGems = supportGems,
		baseGroups = baseGroups,
		mods = mods,
		configVars = configVars,
		counts = {
			nodes = #nodeIds,
			classes = #classes,
			activeGems = #activeGems,
			supportGems = #supportGems,
			baseGroups = #baseGroups,
			bases = baseCount,
			mods = #mods,
			configVars = #configVars,
		},
	}
end

return pools
