--[[
	ModCache parser oracle (migration ticket 05).

	Re-emits src/Data/ModCache.lua -- 23,215 lines of pre-parsed mod text written by
	main:SaveModCache (src/Modules/Main.lua:315-348) -- as a deterministic JSON corpus that
	Pob.Tests/ModCacheOracleTests.cs replays against the C# ModParser port (tickets 12-14).

	Usage (from anywhere):
		luajit dotnet/tools/modcache-oracle/dump.lua [outputPath]

	Default output: dotnet/Pob.Tests/oracles/modcache.json

	LuaJIT specifically, not stock lua5.1: src/Modules/ModTools.lua:15 binds `bit.band`, and
	the bit library is a LuaJIT extension. LuaJIT is also what PoB ships (runtime/lua51.dll).

	Nothing is retyped. The canonical mod strings come from the real modLib.formatMod in
	src/Modules/ModTools.lua, and the ModFlag/KeywordFlag bit tables it needs are lifted
	verbatim out of src/Data/Global.lua. An upstream rename makes this script fail loudly
	rather than silently pinning a stale copy.

	See README.md for the canonical-form design and its documented limits.
]]

local ORACLE_FORMAT = 1

-- String comparison in Lua 5.1 / LuaJIT goes through strcoll, so the sort order of the
-- corpus would otherwise depend on the ambient locale. Pin it, and compare bytes anyway.
pcall(os.setlocale, "C")

----------------------------------------------------------------------------------------
-- Paths
----------------------------------------------------------------------------------------

local scriptPath = arg and arg[0] or "dotnet/tools/modcache-oracle/dump.lua"
local scriptDir = scriptPath:match("^(.*)[/\\][^/\\]*$") or "."
local repoRoot = scriptDir .. "/../../.."
local globalPath = repoRoot .. "/src/Data/Global.lua"
local modToolsPath = repoRoot .. "/src/Modules/ModTools.lua"
local modCachePath = repoRoot .. "/src/Data/ModCache.lua"
local outputPath = arg and arg[1] or (repoRoot .. "/dotnet/Pob.Tests/oracles/modcache.json")

package.path = repoRoot .. "/runtime/lua/?.lua;" .. repoRoot .. "/runtime/lua/?/init.lua;" .. package.path

local dkjson = require("dkjson")

local function readFile(path)
	local f = assert(io.open(path, "rb"), "cannot open " .. path)
	local text = f:read("*a")
	f:close()
	return text
end

----------------------------------------------------------------------------------------
-- ModFlag / KeywordFlag, lifted verbatim out of src/Data/Global.lua
--
-- Global.lua cannot simply be dofile'd: line 79 calls copyTable, which lives in
-- src/Modules/Common.lua, which in turn requires lcurl.safe, xml, base64, sha1 and
-- lua-utf8 and touches the `launch` global. The bit tables are a self-contained run of
-- plain assignments, so they are extracted and loadstring'd instead.
----------------------------------------------------------------------------------------

local function loadFlagTables()
	local source = readFile(globalPath):gsub("\r\n", "\n")
	local block = source:match("\nModFlag = { }\n(.-)\n%-%- Helper function to compare KeywordFlags\n")
	assert(block, "the ModFlag/KeywordFlag block moved in " .. globalPath .. "; fix the extraction")
	assert(block:find("\nKeywordFlag = { }\n", 1, true), "KeywordFlag table missing from the extracted block")

	local chunk = assert(loadstring("ModFlag = { }\n" .. block, "@Global.lua:ModFlag"))
	chunk()

	assert(ModFlag.Attack == 0x00000001, "ModFlag.Attack changed value")
	assert(ModFlag.WeaponMask == 0x2FFF0000, "ModFlag.WeaponMask changed value")
	assert(KeywordFlag.MatchAll == 0x40000000, "KeywordFlag.MatchAll changed value")
end

----------------------------------------------------------------------------------------
-- modLib, from the real src/Modules/ModTools.lua
--
-- ModTools.lua's only load-time dependency on the PoB host is
-- `LoadModule("Modules/ModParser")`, which it uses purely to republish parseMod and
-- parseModCache. ModParser is 7k lines and pulls in the whole data tree, so it is stubbed;
-- the formatting functions this script uses do not touch it.
----------------------------------------------------------------------------------------

local function loadModLib()
	local stubbed = false
	local previousLoadModule = _G.LoadModule
	_G.LoadModule = function(name)
		assert(name == "Modules/ModParser", "ModTools.lua asked for an unexpected module: " .. tostring(name))
		stubbed = true
		return { parseMod = false, parseModCache = {} }
	end

	dofile(modToolsPath)
	_G.LoadModule = previousLoadModule

	assert(stubbed, "ModTools.lua no longer loads Modules/ModParser; check the parseModCache plumbing")
	assert(type(modLib) == "table", "ModTools.lua did not define modLib")
	assert(type(modLib.formatMod) == "function", "modLib.formatMod is gone (was ModTools.lua:231)")
	assert(type(modLib.formatSourceMod) == "function", "modLib.formatSourceMod is gone (was ModTools.lua:235)")
	assert(type(modLib.parseModCache) == "table", "modLib.parseModCache is gone")
end

----------------------------------------------------------------------------------------
-- The cache itself
--
-- src/Data/ModCache.lua is a plain Lua chunk: `local c = {}`, five IIFEs of <=5,001
-- `c[<line>] = { <mods>, <remainder> }` statements each (ModCache.lua:2, 5004, 10006,
-- 15008, 20010 -- the split dodges LuaJIT's per-prototype constant limit), then `return c`.
-- loadfile handles that structure with no special casing, because the split is inside the
-- file rather than something the reader has to reassemble. What the reader must NOT do is
-- wrap the whole file in another function, which is exactly what LoadModule/require does
-- and exactly why main:SaveModCache splits it in the first place.
----------------------------------------------------------------------------------------

local function loadModCache()
	local source = readFile(modCachePath)

	local rawLineCount = 0
	for _ in source:gmatch("[^\n]*\n") do
		rawLineCount = rawLineCount + 1
	end
	if source:sub(-1) ~= "\n" then
		rawLineCount = rawLineCount + 1
	end

	local chunkCount = 1
	for _ in source:gmatch("end%)%(%);%(function%(%)") do
		chunkCount = chunkCount + 1
	end
	assert(chunkCount == 5, "expected 5 IIFE chunks in ModCache.lua, found " .. chunkCount)

	local cache = assert(loadfile(modCachePath))()
	assert(type(cache) == "table", "ModCache.lua did not return a table")

	-- This is the table main:Init installs at Main.lua:140-142; go through it so the
	-- corpus is read from the same place the engine reads it from.
	for line, entry in pairs(cache) do
		modLib.parseModCache[line] = entry
	end

	return rawLineCount, chunkCount
end

----------------------------------------------------------------------------------------
-- Canonical form
----------------------------------------------------------------------------------------

--- Byte-wise "<" for strings, independent of locale and of Lua's strcoll-backed operator.
local s_byte = string.byte
local function byteLess(a, b)
	local la, lb = #a, #b
	local n = la < lb and la or lb
	for i = 1, n do
		local ca, cb = s_byte(a, i), s_byte(b, i)
		if ca ~= cb then
			return ca < cb
		end
	end
	return la < lb
end

--- A faithful, injective serialisation of a mod table, used ONLY to audit how much
--- information modLib.formatMod throws away. It is not what gets emitted.
local function structure(value)
	local t = type(value)
	if t == "table" then
		local keys = {}
		for k in pairs(value) do
			keys[#keys + 1] = k
		end
		table.sort(keys, function(a, b)
			local ta, tb = type(a), type(b)
			if ta ~= tb then
				return ta < tb
			end
			if ta == "string" then
				return byteLess(a, b)
			end
			return a < b
		end)
		local parts = {}
		for i = 1, #keys do
			parts[i] = structure(keys[i]) .. ":" .. structure(value[keys[i]])
		end
		return "{" .. table.concat(parts, ",") .. "}"
	elseif t == "string" then
		return string.format("s%q", value)
	elseif t == "number" then
		return string.format("n%.17g", value)
	elseif t == "boolean" then
		return "b" .. tostring(value)
	end
	return "?" .. t
end

--- True when `mod` carries a tag behind a nil hole in its array part. ipairs stops at the
--- hole, so both modLib.formatMod and ModStore:EvalMod (src/Classes/ModStore.lua:368)
--- ignore such a tag entirely -- it is dead weight in the cache, not lost information.
local function hasShadowedTag(mod)
	local highest = 0
	for k in pairs(mod) do
		if type(k) == "number" and k > highest then
			highest = k
		end
	end
	return highest > #mod
end

----------------------------------------------------------------------------------------
-- Build the corpus
----------------------------------------------------------------------------------------

loadFlagTables()
loadModLib()
local rawLineCount, chunkCount = loadModCache()

local lines = {}
for line in pairs(modLib.parseModCache) do
	lines[#lines + 1] = line
end
table.sort(lines, byteLess)

local entries = {}
local modCount = 0
local nilModListCount, emptyModListCount, withModsCount = 0, 0, 0
local remainderCount, fullyParsedCount = 0, 0
local nilModsWithRemainderCount, nilModsNoRemainderCount = 0, 0
local shadowedTagCount = 0
local canonicalToStructure = {}
local ambiguous = {}
local distinctCanonical, distinctCanonicalCount = {}, 0

for i = 1, #lines do
	local line = lines[i]
	local entry = modLib.parseModCache[line]
	local modList, remainder = entry[1], entry[2]

	local mods = nil
	if modList then
		mods = {}
		for j = 1, #modList do
			local mod = modList[j]
			modCount = modCount + 1

			local canonical = modLib.formatMod(mod)
			mods[j] = canonical

			if not distinctCanonical[canonical] then
				distinctCanonical[canonical] = true
				distinctCanonicalCount = distinctCanonicalCount + 1
			end

			if hasShadowedTag(mod) then
				shadowedTagCount = shadowedTagCount + 1
			end

			-- Audit: does the canonical text ever stand for two different mod tables?
			local shape = structure(mod)
			local seen = canonicalToStructure[canonical]
			if seen == nil then
				canonicalToStructure[canonical] = shape
			elseif seen ~= shape then
				ambiguous[#ambiguous + 1] = {
					canonical = canonical,
					line = line,
					shapeA = seen,
					shapeB = shape,
					shadowedTag = hasShadowedTag(mod),
				}
			end
		end

		if #modList == 0 then
			emptyModListCount = emptyModListCount + 1
		else
			withModsCount = withModsCount + 1
		end
	else
		nilModListCount = nilModListCount + 1
	end

	if remainder then
		remainderCount = remainderCount + 1
	else
		fullyParsedCount = fullyParsedCount + 1
	end

	if not modList then
		if remainder then
			nilModsWithRemainderCount = nilModsWithRemainderCount + 1
		else
			nilModsNoRemainderCount = nilModsNoRemainderCount + 1
		end
	end

	-- [line, mods (null when the cache stored nil), remainder (null when fully consumed)]
	entries[i] = { line, mods or dkjson.null, remainder or dkjson.null }
end

table.sort(ambiguous, function(a, b)
	if a.canonical ~= b.canonical then
		return byteLess(a.canonical, b.canonical)
	end
	return byteLess(a.line, b.line)
end)

local corpus = {
	format = ORACLE_FORMAT,
	source = "src/Data/ModCache.lua",
	canonicalForm = "modLib.formatMod",
	sourceLineCount = rawLineCount,
	sourceChunkCount = chunkCount,
	entryCount = #entries,
	modCount = modCount,
	distinctModCount = distinctCanonicalCount,
	-- Lines main:SaveModCache stored with no leftover text: PoB consumed them whole.
	-- This is the denominator the C# port is scored against.
	luaFullyParsedCount = fullyParsedCount,
	-- Lines PoB itself could not finish. They must NOT count against the port.
	luaRemainderCount = remainderCount,
	luaNilModListCount = nilModListCount,
	luaEmptyModListCount = emptyModListCount,
	luaWithModsCount = withModsCount,
	-- Of the nil-mod-list lines, the ones PoB handed back untouched...
	luaNilModsWithRemainderCount = nilModsWithRemainderCount,
	-- ...and the ones it consumed entirely while producing nothing.
	luaNilModsNoRemainderCount = nilModsNoRemainderCount,
	shadowedTagCount = shadowedTagCount,
	ambiguousCanonicalCount = #ambiguous,
	ambiguousCanonical = ambiguous,
	entries = entries,
}

-- No state table: dkjson then sorts object keys itself (dkjson.lua:355-364), which is the
-- determinism guarantee this corpus needs. The entries array is pre-sorted above.
local encoded = assert(dkjson.encode(corpus))

local out = assert(io.open(outputPath, "wb"), "cannot write " .. outputPath)
out:write(encoded)
out:write("\n")
out:close()

io.stdout:write(string.format(
	"modcache oracle -> %s\n" ..
	"  source lines      %d (%d IIFE chunks)\n" ..
	"  entries           %d\n" ..
	"  mods              %d (%d distinct canonical strings)\n" ..
	"  fully parsed      %d\n" ..
	"  with remainder    %d\n" ..
	"  nil mod list      %d\n" ..
	"  empty mod list    %d\n" ..
	"  nil mods, remndr  %d\n" ..
	"  nil mods, no rem  %d\n" ..
	"  shadowed tags     %d\n" ..
	"  ambiguous canon.  %d\n" ..
	"  bytes             %d\n",
	outputPath, rawLineCount, chunkCount, #entries, modCount, distinctCanonicalCount,
	fullyParsedCount, remainderCount, nilModListCount, emptyModListCount,
	nilModsWithRemainderCount, nilModsNoRemainderCount,
	shadowedTagCount, #ambiguous, #encoded + 1))
