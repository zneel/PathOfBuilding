--[[
	Mod-store dump harness (migration ticket 06).

	Loads every build in spec/TestBuilds/3.13/ through the existing headless wrapper and
	writes one JSON file per build describing the five mod stores the calc engine hands to
	CalcPerform:

		env.modDB, env.enemyDB, env.itemModDB,
		env.player.mainSkill.skillModList, env.minion.modDB (when the main skill has a minion)

	The point is to separate *setup* bugs from *math* bugs: when a ported DPS number is
	wrong, this dump says whether the modifiers going in were already wrong.

	Usage — LuaJIT only (see the assert below), and the working directory must be src/,
	because HeadlessWrapper.lua dofile()s its siblings by relative path:

		cd src && luajit ../dotnet/tools/modstore-dump/dump.lua [outputDir]

	Default output directory: dotnet/Pob.Tests/oracles/modstore/

	Nothing in src/ is modified: the wrapper is dofile()d as-is and the stores are read out
	of build.calcsTab.mainEnv afterwards.

	DETERMINISM is the whole contract here. Lua's pairs() order is undefined, so:
	  * mods are sorted by their own canonical encoding, which begins with the mod name, so
	    the file still reads as ModDB:Print()'s name-grouped listing;
	  * store array order is deliberately NOT preserved -- it is not reproducible; see the
	    comment on sortedModJson;
	  * every JSON object emitted from a Lua hash table has its keys sorted;
	  * conditions and multipliers are sorted by name;
	  * numbers are written in the shortest representation that round-trips, and the
	    round-trip is asserted before the value is emitted;
	  * functions (mod values of type JewelFunc carry one) are written as their definition
	    site from debug.getinfo, never as "function: 0x...".
]]

local DUMP_FORMAT = 1
local GENERATOR = "dotnet/tools/modstore-dump/dump.lua"

----------------------------------------------------------------------------------------
-- Bootstrap
----------------------------------------------------------------------------------------

-- The wrapper and everything it loads live in src/ and address each other relatively.
local wrapper = io.open("HeadlessWrapper.lua", "r")
assert(wrapper, "run this from the src/ directory: cd src && luajit ../" .. GENERATOR)
wrapper:close()

-- Prepended, not replaced, so LUA_PATH / LUA_CPATH still win where they are set. The
-- runtime/lua entries are the pure-Lua modules PoB ships (xml, base64, sha1, dkjson); the
-- cpath entry is where luarocks puts lua-utf8, which Common.lua:25-30 requires.
package.path = "../runtime/lua/?.lua;../runtime/lua/?/init.lua;./?.lua;" .. package.path
package.cpath = "/usr/local/lib/lua/5.1/?.so;" .. package.cpath

local outputDir = (arg and arg[1]) or "../dotnet/Pob.Tests/oracles/modstore"
local buildDir = "../spec/TestBuilds/3.13"

-- Every 3.13 test build, listed explicitly rather than discovered: LuaFileSystem is not
-- available in this runtime, and a fixed list also fixes the emission order.
local BUILDS = {
	{ name = "Dual Savior",               file = "Dual Savior.xml",               slug = "DualSavior" },
	{ name = "Dual Wield Cospris CoC",    file = "Dual Wield Cospris CoC.xml",    slug = "DualWieldCosprisCoC" },
	{ name = "Generals Perforate Zerker", file = "Generals Perforate Zerker.xml", slug = "GeneralsPerforateZerker" },
	{ name = "Mirage Archer Toxic Rain",  file = "Mirage Archer Toxic Rain.xml",  slug = "MirageArcherToxicRain" },
	{ name = "OccVortex",                 file = "OccVortex.xml",                 slug = "OccVortex" },
}

local rawtostring = tostring

-- LuaJIT, not stock Lua 5.1. The tree uses `goto`/labels in 39 places outside TreeData
-- (src/Modules/Data.lua:228 is the first one reached), which is Lua 5.2 syntax that
-- LuaJIT 2.1 implements and stock 5.1 cannot parse; src/Launch.lua:18 also calls
-- jit.opt.start() unconditionally, and bit.* is a LuaJIT builtin. So "run the dump under
-- both interpreters and diff" is not available: PoB itself does not load under 5.1.
assert(jit, "this harness requires LuaJIT: the tree uses Lua 5.2 goto syntax (src/Modules/Data.lua:228)"
	.. " and src/Launch.lua:18 calls jit.opt.start()")

----------------------------------------------------------------------------------------
-- Scalar encoding
----------------------------------------------------------------------------------------

local s_format = string.format
local t_insert = table.insert
local t_sort = table.sort
local m_huge = math.huge
local m_floor = math.floor

--- A stable identity for a Lua function. Mod values of type "JewelFunc" hold a closure
--- built by ModParser; its address changes every run, its definition site does not.
local function functionId(fn)
	local info = debug.getinfo(fn, "S")
	local source = (info and info.source or "?"):gsub("^@", "")
	return s_format("%s:%d", source, (info and info.linedefined) or -1)
end

--- Shortest %g spelling that parses back to the identical double. Integers are written as
--- integers so the goldens stay readable. Mirrors the approach in
--- dotnet/tools/luacompat-oracle/dump.lua; see its README for why the round-trip is
--- asserted rather than assumed.
local function encodeNumber(x)
	if x ~= x or x == m_huge or x == -m_huge then
		-- JSON has no spelling for these. Nothing in a mod store should produce one, so
		-- fail loudly rather than emit something a parser will silently mangle.
		error("mod store holds a non-finite number: " .. rawtostring(x))
	end
	if x == 0 then
		-- 1/0 is inf and 1/-0 is -inf: the only portable way to see the sign bit of a
		-- zero. The engine really does store negative zeros (an enemy's `-0 INC
		-- ActionSpeed` from an inactive Chill, for one), Lua prints them as "-0", and
		-- "%d" would quietly drop the sign.
		return (1 / x < 0) and "-0" or "0"
	end
	if x == m_floor(x) and x >= -9007199254740992 and x <= 9007199254740992 then
		return s_format("%d", x)
	end
	for precision = 1, 17 do
		local s = s_format("%." .. precision .. "g", x)
		if tonumber(s) == x then
			return s
		end
	end
	error("no round-tripping decimal representation for " .. rawtostring(x))
end

local ESCAPES = {
	['"'] = '\\"', ["\\"] = "\\\\", ["\b"] = "\\b", ["\f"] = "\\f",
	["\n"] = "\\n", ["\r"] = "\\r", ["\t"] = "\\t",
}

--- Strings pass through as UTF-8; only the six characters JSON forbids and the C0 range
--- are escaped. Item and mod names are ASCII in practice, and this keeps a golden diff
--- readable when one is not.
local function encodeString(s)
	local out = s:gsub('[%c"\\]', function(c)
		return ESCAPES[c] or s_format("\\u%04x", c:byte())
	end)
	return '"' .. out .. '"'
end

----------------------------------------------------------------------------------------
-- Value encoding
----------------------------------------------------------------------------------------

local encodeValue

--- True when a table looks like a modifier produced by modLib.createMod. Mod values such
--- as { mod = <inner mod> } nest a whole modifier, and rendering those with the mod schema
--- rather than as an anonymous table is what makes an inner mod's `source` visible —
--- modLib.setSource (ModTools.lua:238-244) writes through to mod.value.mod.source, and a
--- mis-attributed inner source is exactly the failure this harness exists to catch.
local function looksLikeMod(t)
	return type(t.name) == "string"
		and type(t.type) == "string"
		and type(t.flags) == "number"
		and type(t.keywordFlags) == "number"
end

local encodeMod

--- Sorted keys of the hash part of a table (i.e. excluding 1..#t).
local function hashKeys(t)
	local n = #t
	local keys = {}
	for k in pairs(t) do
		local isArrayIndex = type(k) == "number" and k >= 1 and k <= n and k == m_floor(k)
		if not isArrayIndex then
			-- A non-string key would have no faithful JSON spelling; none occur in
			-- practice, so refuse rather than guess.
			assert(type(k) == "string", "non-string table key in a mod store: " .. rawtostring(k))
			t_insert(keys, k)
		end
	end
	t_sort(keys)
	return keys, n
end

local function encodeArray(t, first, last, depth)
	local parts = {}
	for i = first, last do
		parts[#parts + 1] = encodeValue(t[i], depth + 1)
	end
	return "[" .. table.concat(parts, ",") .. "]"
end

local function encodeObject(pairsList)
	local parts = {}
	for _, kv in ipairs(pairsList) do
		parts[#parts + 1] = encodeString(kv[1]) .. ":" .. kv[2]
	end
	return "{" .. table.concat(parts, ",") .. "}"
end

--- Generic table: an array when it has only 1..n, an object otherwise. A table with both
--- (a tag list hanging off a mod-shaped table, say) puts the array part under "[]".
local function encodeTable(t, depth)
	if looksLikeMod(t) then
		return encodeMod(t, depth)
	end
	local keys, n = hashKeys(t)
	if #keys == 0 then
		return encodeArray(t, 1, n, depth)
	end
	local fields = {}
	for _, k in ipairs(keys) do
		fields[#fields + 1] = { k, encodeValue(t[k], depth + 1) }
	end
	if n > 0 then
		t_insert(fields, 1, { "[]", encodeArray(t, 1, n, depth) })
	end
	return encodeObject(fields)
end

--- Depth guard. The deepest real nesting observed across the 3.13 corpus is 4
--- ({ mod = { value = { key = ... } } }); anything past 24 is a cycle the schema cannot
--- express, and silently truncating one would corrupt a golden file.
encodeValue = function(v, depth)
	assert(depth < 24, "mod value nests deeper than 24 levels; probable reference cycle")
	local t = type(v)
	if v == nil then
		return "null"
	elseif t == "boolean" then
		return v and "true" or "false"
	elseif t == "number" then
		return encodeNumber(v)
	elseif t == "string" then
		return encodeString(v)
	elseif t == "function" then
		return encodeObject({ { "__lua", '"function"' }, { "def", encodeString(functionId(v)) } })
	elseif t == "table" then
		return encodeTable(v, depth)
	end
	error("mod store holds an unencodable " .. t)
end

----------------------------------------------------------------------------------------
-- ModDB:Print()-style rendering
----------------------------------------------------------------------------------------

-- ModDB.lua:336-370 prints each mod as
--     <value> = <type>|<flags>|<keywordFlags>|<tags>|<source>
-- through modLib.formatValue / formatFlags / formatTags. Those helpers are reused verbatim
-- rather than restated, so the golden line and the engine's own debug output cannot drift.
-- The one substitution is tostring: ModTools.lua looks it up as a global (it localises
-- pairs, ipairs, type, select and the math/bit helpers, but not tostring), so swapping it
-- for the duration replaces "function: 0x7f..." — an address that changes every run — with
-- the closure's definition site.
local function withStableToString(fn, ...)
	local saved = _G.tostring
	_G.tostring = function(v)
		if type(v) == "function" then
			return "function@" .. functionId(v)
		end
		return saved(v)
	end
	local ok, result = pcall(fn, ...)
	_G.tostring = saved
	if not ok then
		error(result, 0)
	end
	return result
end

local function printLine(mod)
	return withStableToString(function()
		return s_format("%s = %s|%s|%s|%s|%s",
			modLib.formatValue(mod.value),
			mod.type,
			modLib.formatFlags(mod.flags, ModFlag),
			modLib.formatFlags(mod.keywordFlags, KeywordFlag),
			modLib.formatTags(mod),
			mod.source or "?")
	end)
end

----------------------------------------------------------------------------------------
-- Mod encoding
----------------------------------------------------------------------------------------

-- The fields modLib.createMod (ModTools.lua:32-66) always writes. Anything else a
-- downstream module bolted on (sourceSlot, replaced, ...) is collected under "extra" so
-- the dump stays lossless without the schema having to enumerate it.
local CORE_FIELDS = { name = true, type = true, value = true, flags = true, keywordFlags = true, source = true }

encodeMod = function(mod, depth)
	assert(depth < 24, "mod nests deeper than 24 levels; probable reference cycle")

	local tags = {}
	for i = 1, #mod do
		tags[#tags + 1] = encodeValue(mod[i], depth + 1)
	end

	local extraKeys = {}
	for k in pairs(mod) do
		if type(k) == "string" and not CORE_FIELDS[k] then
			t_insert(extraKeys, k)
		end
	end
	t_sort(extraKeys)

	local fields = {
		{ "name", encodeString(mod.name) },
		{ "type", encodeString(mod.type) },
		{ "value", encodeValue(mod.value, depth + 1) },
		{ "flags", encodeNumber(mod.flags) },
		{ "flagNames", encodeString(modLib.formatFlags(mod.flags, ModFlag)) },
		{ "keywordFlags", encodeNumber(mod.keywordFlags) },
		{ "keywordFlagNames", encodeString(modLib.formatFlags(mod.keywordFlags, KeywordFlag)) },
		-- `source` is deliberately not defaulted to "": a mod with no source at all is a
		-- different thing from one sourced to the empty string, and ModStore's
		-- source-prefix filtering (`mod.source:match("[^:]+") == source`) treats them
		-- differently. null means the field was absent.
		{ "source", mod.source ~= nil and encodeString(mod.source) or "null" },
		{ "tags", "[" .. table.concat(tags, ",") .. "]" },
	}

	if #extraKeys > 0 then
		local extras = {}
		for _, k in ipairs(extraKeys) do
			extras[#extras + 1] = { k, encodeValue(mod[k], depth + 1) }
		end
		fields[#fields + 1] = { "extra", encodeObject(extras) }
	end

	fields[#fields + 1] = { "print", encodeString(printLine(mod)) }

	return encodeObject(fields)
end

----------------------------------------------------------------------------------------
-- Store encoding
----------------------------------------------------------------------------------------

--- Flattens a store to a flat list of mods. ModDB keeps its mods in per-name buckets
--- (ModDB.lua:29-34); ModList is the array itself (ModList.lua:27-29).
local function storeMods(store)
	local mods = {}
	if store.mods then
		for _, bucket in pairs(store.mods) do
			for _, mod in ipairs(bucket) do
				t_insert(mods, mod)
			end
		end
	else
		for _, mod in ipairs(store) do
			t_insert(mods, mod)
		end
	end
	return mods
end

--- Mods are emitted sorted by their own encoding, which begins with the name, so the file
--- still reads as ModDB:Print()'s name-grouped listing.
---
--- The array order inside a store is NOT preserved, and that is deliberate: it is not
--- reproducible. Two consecutive runs of this script over the same build put
--- `-17 MORE Damage ... Skill:Enfeeble` at different indices of enemyDB's "Damage" bucket,
--- because the code that fills the stores walks item slots, skills and buff sources with
--- pairs(). Pinning that order would pin hash-table iteration order into a golden file.
--- Sorting on the full encoding (not just the name) makes the order total: two mods that
--- compare equal are byte-identical, so which one wins the tie cannot change the output.
local function sortedModJson(mods)
	local encoded = {}
	for _, mod in ipairs(mods) do
		encoded[#encoded + 1] = encodeMod(mod, 0)
	end
	t_sort(encoded)
	return encoded
end

local function encodeStore(name, path, store)
	local mods = storeMods(store)
	local modParts = sortedModJson(mods)

	-- ModDB:Print()'s "=== Conditions ===" section: names whose value is truthy.
	local conditions = {}
	for condName, value in pairs(store.conditions or {}) do
		if value then
			t_insert(conditions, condName)
		end
	end
	t_sort(conditions)
	local condParts = {}
	for _, condName in ipairs(conditions) do
		condParts[#condParts + 1] = encodeString(condName)
	end

	-- ModDB:Print()'s "=== Multipliers ===" section. Print filters to value > 0; the dump
	-- keeps zero and negative entries too, because "present and 0" and "absent" are
	-- different inputs to a Multiplier tag.
	local multNames = {}
	for multName in pairs(store.multipliers or {}) do
		t_insert(multNames, multName)
	end
	t_sort(multNames)
	local multParts = {}
	for _, multName in ipairs(multNames) do
		multParts[#multParts + 1] = encodeObject({
			{ "name", encodeString(multName) },
			{ "value", encodeNumber(store.multipliers[multName]) },
		})
	end

	local json = encodeObject({
		{ "name", encodeString(name) },
		{ "path", encodeString(path) },
		{ "class", encodeString(store.mods and "ModDB" or "ModList") },
		{ "order", encodeString("canonical") },
		{ "hasParent", store.parent and "true" or "false" },
		{ "modCount", encodeNumber(#mods) },
		{ "conditionCount", encodeNumber(#conditions) },
		{ "multiplierCount", encodeNumber(#multNames) },
		{ "conditions", "[" .. table.concat(condParts, ",") .. "]" },
		{ "multipliers", "[\n" .. table.concat(multParts, ",\n") .. "]" },
		{ "mods", "[\n" .. table.concat(modParts, ",\n") .. "]" },
	})

	return json, #mods
end

----------------------------------------------------------------------------------------
-- Run
----------------------------------------------------------------------------------------

dofile("HeadlessWrapper.lua")

assert(type(modLib) == "table" and modLib.formatValue, "modLib did not load")
assert(type(ModFlag) == "table" and type(KeywordFlag) == "table", "flag tables did not load")

local interpreter = (_VERSION or "?") .. " / " .. jit.version

--- HeadlessWrapper.lua:41 wires __mainObject__.continuousIntegrationMode to the CI
--- environment variable, which suppresses src/Data/ModCache.lua. Parsing live and parsing
--- from the cache are supposed to agree, but they are different code paths, so the flag is
--- recorded in every dump rather than left to chance.
local usedModCache = not __mainObject__.continuousIntegrationMode

local function readFile(path)
	local handle = assert(io.open(path, "r"), "cannot open " .. path)
	local text = handle:read("*a")
	handle:close()
	return text
end

local summary = {}

for _, entry in ipairs(BUILDS) do
	loadBuildFromXML(readFile(buildDir .. "/" .. entry.file), entry.name)

	local env = assert(build.calcsTab.mainEnv, "build produced no environment")
	local mainSkill = assert(env.player.mainSkill, "build has no main skill")

	local targets = {
		{ "modDB", "env.modDB", env.modDB },
		{ "enemyDB", "env.enemyDB", env.enemyDB },
		{ "itemModDB", "env.itemModDB", env.itemModDB },
		{ "skillModList", "env.player.mainSkill.skillModList", mainSkill.skillModList },
		-- env.minion is set from env.player.mainSkill.minion (CalcPerform.lua:1316), so it
		-- exists only when the *main* skill summons. None of the 3.13 test builds does;
		-- the branch is kept so a minion build drops straight in.
		{ "minionModDB", "env.minion.modDB", env.minion and env.minion.modDB },
	}

	local storeParts = {}
	local counts = {}
	for _, target in ipairs(targets) do
		local storeName, path, store = target[1], target[2], target[3]
		if store then
			local json, count = encodeStore(storeName, path, store)
			storeParts[#storeParts + 1] = json
			counts[#counts + 1] = { storeName, count }
		end
	end

	local grantedEffect = mainSkill.activeEffect and mainSkill.activeEffect.grantedEffect
	local document = encodeObject({
		{ "format", encodeNumber(DUMP_FORMAT) },
		{ "generator", encodeString(GENERATOR) },
		{ "build", encodeString(entry.name) },
		{ "buildFile", encodeString("spec/TestBuilds/3.13/" .. entry.file) },
		{ "mainSkill", encodeString(grantedEffect and grantedEffect.name or "?") },
		{ "usedModCache", usedModCache and "true" or "false" },
		{ "schema", encodeString(
			"stores[].mods[] = {name,type,value,flags,flagNames,keywordFlags,keywordFlagNames,"
			.. "source,tags[],extra?,print}; value may nest a whole mod under `mod`; "
			.. "`print` reproduces ModDB:Print()'s line; mods are sorted by their own encoding") },
		{ "storeCount", encodeNumber(#storeParts) },
		{ "stores", "[\n" .. table.concat(storeParts, ",\n") .. "]" },
	})

	local outputPath = outputDir .. "/" .. entry.slug .. ".json"
	local out = assert(io.open(outputPath, "wb"), "cannot write " .. outputPath)
	out:write(document)
	out:write("\n")
	out:close()

	local parts = {}
	for _, count in ipairs(counts) do
		parts[#parts + 1] = count[1] .. "=" .. count[2]
	end
	summary[#summary + 1] = s_format("%-26s %s", entry.name, table.concat(parts, " "))
end

io.write(s_format("modstore-dump (%s, ModCache %s) -> %s\n",
	interpreter, usedModCache and "on" or "off", outputDir))
for _, line in ipairs(summary) do
	io.write("  " .. line .. "\n")
end
