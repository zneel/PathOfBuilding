-- Flattens a calculated build's actor outputs into the flat key/value maps that make up a
-- golden record (migration ticket 07).
--
-- `spec/GenerateBuilds.lua` dumps only `build.calcsTab.mainOutput`, which is
-- `mainEnv.player.output` - one actor, one mode. This dumps every surface the ticket names:
--
--   MAIN/player   CALCS/player    the two calc modes (they differ: CALCS resolves the
--   MAIN/minion   CALCS/minion    "Calcs" tab's own skill/part selection, MAIN the
--   MAIN/enemy    CALCS/enemy     build's, so a build with two socket groups produces
--                                 genuinely different numbers in the two)
--
-- `SkillDPS` (the full-DPS skill breakdown) lives inside `player.output` and is flattened
-- in place as `SkillDPS/1/dps` and friends, so it needs no separate section and needs no
-- separate comparison path in C#.
--
-- Nested output tables (`MainHand`, `OffHand`, `ReqStrItem`, `SkillDPS`, ...) are flattened
-- with `/` separators rather than nested, because the whole point of the corpus is per-key
-- comparison and a flat map makes "which key drifted, and by how much" a one-line answer.
--
-- Two of those nested tables are not value objects at all: `output.ReqStrItem.sourceItem`
-- and `.sourceGem` are live references to the Item and gem-instance the requirement came
-- from, and following them reaches the entire gem/mod database. Walking an output table
-- naively produces 780,000 keys and a 160 MB file per build. They are cut at the reference
-- and recorded as the marker `"__table"`, which keeps the key's existence in the contract
-- without dragging the object graph in.

local m = {}

local s_format = string.format
local m_floor = math.floor

-- Recursion limit, in path segments. Everything the engine legitimately puts in an output
-- table is within it: per-weapon sub-outputs (`MainHand/CritChance`) and the full-DPS
-- breakdown (`SkillDPS/1/dps`) are both depth 3.
local MAX_DEPTH = 3

-- Keys whose values are references to engine objects rather than results.
local REFERENCE_KEYS = {
	sourceGem = true,
	sourceItem = true,
	sourceSkill = true,
	sourceActor = true,
}

-- A section far over this is a walk that escaped into the object graph, not a build with a
-- lot of stats: the largest honest section observed is under 1,300 keys. Failing loudly
-- beats writing a corpus nobody can load.
local SECTION_KEY_LIMIT = 20000

local function keyText(k)
	if type(k) == "number" and k == m_floor(k) then
		return s_format("%.0f", k)
	end
	return tostring(k)
end

local function flattenInto(out, stats, prefix, tbl, depth, seen)
	if seen[tbl] then
		out[prefix .. "__cycle"] = "__cycle"
		stats.anomalies = stats.anomalies + 1
		return
	end
	if depth > MAX_DEPTH then
		out[prefix .. "__depth"] = "__truncated"
		stats.anomalies = stats.anomalies + 1
		return
	end
	seen[tbl] = true
	local keys = {}
	for k in pairs(tbl) do
		keys[#keys + 1] = k
	end
	table.sort(keys, function(a, b)
		local ta, tb = type(a), type(b)
		if ta ~= tb then
			return ta == "number"
		end
		return a < b
	end)
	for _, k in ipairs(keys) do
		local v = tbl[k]
		local t = type(v)
		local path = prefix .. keyText(k)
		if t == "number" then
			out[path] = v
			stats.numbers = stats.numbers + 1
		elseif t == "boolean" then
			out[path] = v
			stats.booleans = stats.booleans + 1
		elseif t == "string" then
			out[path] = v
			stats.strings = stats.strings + 1
		elseif t == "table" then
			if REFERENCE_KEYS[k] or depth >= MAX_DEPTH then
				out[path] = "__table"
				stats.anomalies = stats.anomalies + 1
			else
				flattenInto(out, stats, path .. "/", v, depth + 1, seen)
			end
		else
			-- functions and userdata: nothing a port could compare against.
			out[path] = "__" .. t
			stats.anomalies = stats.anomalies + 1
		end
	end
	seen[tbl] = nil
end

--- Flattens one actor output table. Returns the flat map and a type census.
function m.flatten(tbl)
	local out = {}
	local stats = { numbers = 0, booleans = 0, strings = 0, anomalies = 0 }
	if tbl then
		flattenInto(out, stats, "", tbl, 1, {})
	end
	return out, stats
end

--- Captures every actor output of a calculated build as `{ ["MODE/actor"] = flatMap }`.
-- `build` must already have been calculated (`runCallback("OnFrame")` after `buildFlag`),
-- which is what `loadBuildFromXML` does.
function m.captureActors(b)
	local sections = {}
	local census = { numbers = 0, booleans = 0, strings = 0, anomalies = 0, keys = 0 }
	local envs = { MAIN = b.calcsTab.mainEnv, CALCS = b.calcsTab.calcsEnv }
	for _, mode in ipairs({ "MAIN", "CALCS" }) do
		local env = envs[mode]
		if env then
			for _, actor in ipairs({ "player", "minion", "enemy" }) do
				local a = env[actor]
				-- `env.minion` is nil for any build whose main skill summons nothing;
				-- the section is then absent rather than empty, so the C# side can tell
				-- "this build has no minion" from "the minion produced no output".
				if a and a.output then
					local flat, stats = m.flatten(a.output)
					local count = 0
					for _ in pairs(flat) do
						count = count + 1
					end
					if count > SECTION_KEY_LIMIT then
						error(s_format("section %s/%s has %d keys - the output walk escaped into the object graph",
							mode, actor, count))
					end
					sections[mode .. "/" .. actor] = flat
					for key, value in pairs(stats) do
						census[key] = census[key] + value
					end
					census.keys = census.keys + count
				end
			end
		end
	end
	return sections, census
end

return m
