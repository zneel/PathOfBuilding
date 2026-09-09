--[[
	Random build recipe generation for the differential fuzzer (migration ticket 08).

	A *recipe* is a pure description of a build: class, ascendancy, level, a list of tree
	node ids, item texts, socket-group paste strings, and config-tab settings. It contains
	no engine objects and no engine-derived values, so it can be generated under any Lua
	and replayed by any engine -- the Lua one today (`run.lua`), a C# one once tickets
	22-27 land.

	Determinism rules observed throughout:

	* Every draw comes from `rng.lua`'s Lehmer LCG. `math.random` is never called.
	* Every choice indexes a dense, ordered array from `pools.lua`. No `pairs` walk ever
	  reaches a decision.
	* Each build draws from its OWN generator, seeded from the master stream. That means
	  build 40's recipe does not shift when the number of draws inside build 39 changes,
	  so a corpus stays comparable across tweaks to a single generator branch.
]]

local plan = {}

local rng = nil    -- injected by plan.init
local pools = nil

--------------------------------------------------------------------------------------
-- Mod-line helpers
--------------------------------------------------------------------------------------

--- Format a number the way item text spells it: integers bare, fractions to two places
--- with trailing zeros trimmed. Never `%g`, whose exponent form ("1e+03") no mod parser
--- accepts.
local function formatNumber(value)
	if value == math.floor(value) then
		return string.format("%d", value)
	end
	local text = string.format("%.2f", value)
	text = text:gsub("0+$", ""):gsub("%.$", "")
	return text
end

--- Does this mod line carry a `(low-high)` roll range?
local function hasRange(line)
	return line:find("%(%-?%d+%.?%d*%-%-?%d+%.?%d*%)") ~= nil
end

--- Replace every `(low-high)` with a concrete roll. Used for config-tab custom mods,
--- which go straight through modLib.parseMod and have no range machinery behind them --
--- unlike item text, which keeps the range and carries a `{range:x}` tag instead.
local function resolveRanges(line, r)
	return (line:gsub("%((%-?%d+%.?%d*)%-(%-?%d+%.?%d*)%)", function(lo, hi)
		local low, high = tonumber(lo), tonumber(hi)
		if not low or not high or high < low then
			return "(" .. lo .. "-" .. hi .. ")"
		end
		local isInteger = (low == math.floor(low)) and (high == math.floor(high))
		if isInteger then
			return formatNumber(r:int(low, high))
		end
		return formatNumber(r:quantised(low, high, 20))
	end))
end

--- One mod line, with a `{range:x}` tag when it has a range to roll. Item text supports
--- the tag (src/Classes/Item.lua parses it); the fuzzer uses it rather than substituting
--- a literal so the engine's own range interpolation stays on the tested path.
local function itemModLine(line, r)
	if hasRange(line) then
		return string.format("{range:%.2f}%s", r:quantised(0, 1, 20), line)
	end
	return line
end

--------------------------------------------------------------------------------------
-- Items
--------------------------------------------------------------------------------------

--- Draw a mod that PoB's affix weighting says can actually appear on this base.
--- Rejection sampling against the ordered pool: deterministic, and cheap enough that
--- precomputing a per-base eligibility index would only add a large hidden data
--- structure. Returns nil after `attempts` misses, and the caller emits a shorter item.
local function drawFittingMod(base, r, attempts)
	for _ = 1, attempts do
		local mod = pools.mods[r:int(1, #pools.mods)]
		if pools.modFitsBase(mod, base) then
			return mod
		end
	end
	return nil
end

--- Build one item's raw text.
---
--- Two flavours, chosen per item:
---
--- * "legal"  -- mods filtered through PoB's own affix weighting, so the item is roughly
---               something the game could produce. These keep the corpus anchored to
---               realistic builds.
--- * "chaos"  -- any mod line from any database on any base, weighting ignored. These are
---               what the fuzzer is actually for: a flask mod on a bow, a cluster-jewel
---               notable on a belt, an Eldritch implicit on a quiver. The mod text is
---               real (every line is a shipped GGG line), only the pairing is not, and
---               that is precisely the shape that drives EvalMod down tag-combination
---               paths no hand-written test covers.
local function generateItem(group, index, r)
	local base = group.bases[r:int(1, #group.bases)]
	local chaos = r:chance(0.35)
	local modCount = r:int(1, 6)

	local lines = {}
	for _ = 1, modCount do
		local mod
		if chaos then
			mod = pools.mods[r:int(1, #pools.mods)]
		else
			mod = drawFittingMod(base, r, 64)
		end
		if mod then
			for _, line in ipairs(mod.lines) do
				lines[#lines + 1] = itemModLine(line, r)
			end
		end
	end

	local text = {
		"Rarity: RARE",
		string.format("Fuzz %s %d", group.file, index),
		base.name,
		string.format("Quality: %d", r:int(0, 20)),
		"LevelReq: 1",
		"Implicits: 0",
	}
	for _, line in ipairs(lines) do
		text[#text + 1] = line
	end

	return {
		group = group.file,
		base = base.name,
		flavour = chaos and "chaos" or "legal",
		raw = table.concat(text, "\n"),
	}
end

--------------------------------------------------------------------------------------
-- Skills
--------------------------------------------------------------------------------------

--- One socket group in SkillsTab:PasteSocketGroup's wire format:
--- `<name> <level>/<quality>  <count>`, one gem per line.
local function generateSocketGroup(r)
	local lines = {}
	local active = pools.activeGems[r:int(1, #pools.activeGems)]
	lines[#lines + 1] = string.format("%s %d/%d  1", active.name, r:int(1, active.maxLevel), r:int(0, 23))
	for _ = 1, r:int(0, 4) do
		local support = pools.supportGems[r:int(1, #pools.supportGems)]
		lines[#lines + 1] = string.format("%s %d/%d  1", support.name, r:int(1, support.maxLevel), r:int(0, 23))
	end
	return table.concat(lines, "\n")
end

--------------------------------------------------------------------------------------
-- Config
--------------------------------------------------------------------------------------

local function generateConfig(r)
	local entries = {}
	local used = {}
	for _ = 1, r:int(0, 10) do
		local option = pools.configVars[r:int(1, #pools.configVars)]
		if not used[option.var] then
			used[option.var] = true
			local value
			if option.kind == "check" then
				-- A `check` left false is indistinguishable from one never set, so the
				-- fuzzer only ever turns them on.
				value = true
			elseif option.kind == "float" then
				value = r:quantised(0, 10, 40)
			else
				-- Small values most of the time, occasionally a large one: several count
				-- options feed multipliers whose behaviour changes at scale.
				value = r:chance(0.15) and r:int(1, 500) or r:int(1, 12)
			end
			entries[#entries + 1] = { var = option.var, kind = option.kind, value = value }
		end
	end
	table.sort(entries, function(a, b) return a.var < b.var end)
	return entries
end

--- Free-text custom mods. These land directly in the player's mod list without passing
--- through an item, so they reach mod forms that no item can express.
local function generateCustomMods(r)
	local lines = {}
	for _ = 1, r:int(0, 4) do
		local mod = pools.mods[r:int(1, #pools.mods)]
		for _, line in ipairs(mod.lines) do
			lines[#lines + 1] = resolveRanges(line, r)
		end
	end
	return table.concat(lines, "\n")
end

--------------------------------------------------------------------------------------
-- Tree
--------------------------------------------------------------------------------------

--- Node ids to allocate, in draw order. The runner allocates them in this order and
--- `PassiveSpec:AllocNode` pulls in each node's whole path, so a short list still yields
--- a connected tree of a few dozen nodes.
---
--- Deliberately NOT deduplicated into a sorted set: allocation order matters, because
--- allocating node A first changes which path node B gets.
local function generateNodes(r)
	local ids = {}
	for _ = 1, r:int(0, 25) do
		ids[#ids + 1] = pools.nodeIds[r:int(1, #pools.nodeIds)]
	end
	return ids
end

--------------------------------------------------------------------------------------

--- Bind the generator to a pool set. Call once before `generate`.
function plan.init(loadedPools, rngModule)
	pools = loadedPools
	rng = rngModule
end

--- One build recipe from a dedicated seed.
function plan.generateOne(index, seed)
	local r = rng.new(seed)

	local class = pools.classes[r:int(1, #pools.classes)]
	local items = {}
	for _, group in ipairs(pools.baseGroups) do
		if r:chance(0.45) then
			items[#items + 1] = generateItem(group, #items + 1, r)
		end
	end

	local socketGroups = {}
	for i = 1, r:int(1, 3) do
		socketGroups[i] = generateSocketGroup(r)
	end

	return {
		index = index,
		seed = seed,
		classId = class.classId,
		className = class.name,
		ascendClassId = r:int(0, class.ascendancyCount),
		level = r:int(1, 100),
		nodes = generateNodes(r),
		items = items,
		socketGroups = socketGroups,
		config = generateConfig(r),
		customMods = generateCustomMods(r),
	}
end

--- `count` recipes from `seed`. The master generator only ever produces child seeds, so
--- recipe N depends on the seed and on N alone.
function plan.generate(seed, count)
	local master = rng.new(seed)
	local builds = {}
	for i = 1, count do
		builds[i] = plan.generateOne(i, master:childSeed())
	end
	return builds
end

return plan
