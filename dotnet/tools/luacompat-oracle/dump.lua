--[[
	LuaCompat conformance oracle (migration ticket 02).

	Emits a JSON corpus of (function, arguments, results) tuples produced by the REAL
	rounding helpers in src/Modules/Common.lua, so that Pob.Core/LuaCompat.cs can be
	replayed against them bit-exactly.

	The helper bodies are NOT retyped here. They are extracted verbatim from
	src/Modules/Common.lua at run time and loaded with loadstring, together with the
	`local m_xxx = math.xxx` alias block they close over. If upstream changes a helper,
	the corpus changes with it; if upstream renames or deletes one, this script fails
	loudly instead of silently testing a stale copy.

	Usage (from anywhere):
		luajit  dotnet/tools/luacompat-oracle/dump.lua [outputPath]
		lua5.1  dotnet/tools/luacompat-oracle/dump.lua [outputPath]

	Default output: dotnet/Pob.Tests/oracles/luacompat.json

	Doubles are serialised as strings, never as JSON numbers, so no JSON reader ever
	gets a chance to reinterpret them. Every value is written in the shortest "%g"
	precision that round-trips, and the script asserts the round-trip before emitting
	it; -0, inf, -inf and nan get explicit spellings.
]]

local ORACLE_FORMAT = 1

----------------------------------------------------------------------------------------
-- Paths
----------------------------------------------------------------------------------------

local scriptPath = arg and arg[0] or "dotnet/tools/luacompat-oracle/dump.lua"
local scriptDir = scriptPath:match("^(.*)[/\\][^/\\]*$") or "."
local repoRoot = scriptDir .. "/../../.."
local commonPath = repoRoot .. "/src/Modules/Common.lua"
local outputPath = arg and arg[1] or (repoRoot .. "/dotnet/Pob.Tests/oracles/luacompat.json")

----------------------------------------------------------------------------------------
-- Source the helpers out of src/Modules/Common.lua
----------------------------------------------------------------------------------------

local function readFile(path)
	local f = assert(io.open(path, "rb"), "cannot open " .. path)
	local text = f:read("*a")
	f:close()
	return (text:gsub("\r\n", "\n"))
end

local commonSource = readFile(commonPath)

--- Every `local m_foo = math.foo` alias declared at the top of Common.lua. The helper
--- bodies reference these as upvalues; loaded standalone they would be nil globals.
local function extractMathAliases(source)
	local out = {}
	for line in source:gmatch("[^\n]+") do
		local alias = line:match("^(local%s+m_%a+%s*=%s*math%.%a+)%s*$")
		if alias then
			out[#out + 1] = alias
		end
	end
	assert(#out > 0, "no `local m_xxx = math.xxx` aliases found in Common.lua")
	return table.concat(out, "\n")
end

--- The verbatim text of a top-level `function <name>(...) ... end` block. Common.lua
--- indents every body with tabs, so the first line-initial `end` closes the function.
local function extractFunction(source, name)
	local header = "\nfunction " .. name .. "("
	local startPos = source:find(header, 1, true)
	assert(startPos, "function " .. name .. " not found in " .. commonPath)
	local endPos = source:find("\nend\n", startPos + #header, true)
	assert(endPos, "unterminated function " .. name .. " in " .. commonPath)
	return source:sub(startPos + 1, endPos + 4)
end

local HELPERS = {
	"round",                -- Common.lua:709  half-up toward +inf
	"floor",                -- Common.lua:721  +0.0001 epsilon, shadows nothing (math.floor is untouched)
	"roundSymmetric",       -- Common.lua:733  half away from zero
	"alwaysPositiveRound",  -- Common.lua:753
	"floorSymmetric",       -- Common.lua:766
	"ceilSymmetric",        -- Common.lua:778
	"ceil_b",               -- Common.lua:1013
	"floor_b",              -- Common.lua:1019
}

local chunkParts = { extractMathAliases(commonSource) }
for _, name in ipairs(HELPERS) do
	chunkParts[#chunkParts + 1] = extractFunction(commonSource, name)
end
local chunkSource = table.concat(chunkParts, "\n")

local chunk = assert(loadstring(chunkSource, "@Common.lua-extract"))
chunk()

for _, name in ipairs(HELPERS) do
	assert(type(_G[name]) == "function", "helper " .. name .. " did not load")
end

----------------------------------------------------------------------------------------
-- Exact double <-> string
----------------------------------------------------------------------------------------

local huge = math.huge

local function numToString(x)
	if x ~= x then
		return "nan"
	elseif x == huge then
		return "inf"
	elseif x == -huge then
		return "-inf"
	elseif x == 0 then
		-- 1/0 == inf, 1/-0 == -inf: the only portable way to see the sign bit of zero.
		return (1 / x < 0) and "-0" or "0"
	end
	-- Shortest %g precision that still round-trips. 17 always works for a double; the
	-- search just keeps the corpus small, and it also stops LuaJIT and Lua 5.1 from
	-- spelling the same double differently -- their printf implementations round the
	-- 17th significant digit differently (both spellings parse back identically, but a
	-- byte-identical corpus from either interpreter is worth more than that).
	for precision = 1, 17 do
		local s = string.format("%." .. precision .. "g", x)
		if tonumber(s) == x then
			return s
		end
	end
	error("no round-tripping decimal representation for a double")
end

----------------------------------------------------------------------------------------
-- Input corpus
----------------------------------------------------------------------------------------

-- Two tiers. `values` is everything; every helper is exercised over all of it with no
-- `dec` argument and with dec = 2 (the precision every call site actually uses).
-- `coreValues` is the hand-picked subset — halves, ulp neighbours, float traps, extreme
-- magnitudes — which additionally gets the full dec sweep. Two tiers rather than one
-- keeps the corpus a couple of megabytes instead of fifteen without losing a shape.
local values = {}
local coreValues = {}
local seenValue = {}
local seenCore = {}

local function addValue(x)
	if type(x) ~= "number" then
		return
	end
	local key = numToString(x)
	if not seenValue[key] then
		seenValue[key] = true
		values[#values + 1] = x
	end
end

local function addCore(x)
	addValue(x)
	local key = numToString(x)
	if not seenCore[key] then
		seenCore[key] = true
		coreValues[#coreValues + 1] = x
	end
end

--- x and its immediate floating-point neighbours in both directions. Lua 5.1 has no
--- nextafter, so nudge by a relative ulp and let rounding land on an adjacent double.
local EPS = 2.2204460492503131e-16
local function addWithNeighbours(x, core)
	local put = core and addCore or addValue
	put(x)
	if x == x and x ~= huge and x ~= -huge and x ~= 0 then
		local step = math.abs(x) * EPS
		put(x + step)
		put(x - step)
	end
end

-- Exact halves and their neighbours: the whole point of `round` vs Math.Round.
for k = -40, 40 do
	addWithNeighbours(k + 0.5, true)
	addCore(k)
	addValue(k / 4)
	addValue(k / 8)
end

-- Halves at the 1e-2 scale: `round(x, 2)` multiplies by 100 first, and (k + 0.5) / 100
-- is essentially never exactly representable, so the product lands just under or just
-- over the true half. This is the shape `round(modResult, 2)` sees in More.
for k = -40, 40 do
	addWithNeighbours((k + 0.5) / 100, true)
	addWithNeighbours((k + 0.5) / 1000, false)
	addWithNeighbours((k + 0.05) / 10, false)
end

-- Decimal-looking values that are not binary-exact.
for k = -100, 100 do
	addValue(k / 10)
	addValue(k / 100)
	addValue(k / 1000)
	addValue(k / 3)
	addValue(k / 7)
end

-- Classic float traps, magnitudes, and the signed/degenerate zeros.
local SPECIALS = {
	0, -0.0, 1 / huge, -1 / huge,
	0.1, 0.2, 0.3, 0.1 + 0.2, 0.7, 1.005, 2.675, 8.475, 1.015, 0.145, 0.615, 1.0049999999999999,
	1 / 3, 2 / 3, -1 / 3, 5 / 6,
	4503599627370495.5, 4503599627370496, 4503599627370497, -- 2^52-0.5, 2^52, 2^52+1
	9007199254740991, 9007199254740992, 9007199254740993,   -- 2^53-1, 2^53, "2^53+1"
	-9007199254740992, -4503599627370496,
	1e15, 1e15 + 0.5, 1e16, 1e17, 1e21, 1e300, 1e308, 1.7976931348623157e308,
	-1e15, -1e16, -1e300, -1.7976931348623157e308,
	1e-5, 1e-4, 9.9999e-5, 0.00010000000000000001, 5e-5, -5e-5, -1e-4, -0.00009,
	1e-300, 1e-308, 5e-324, 2.2250738585072014e-308,
	-1e-300, -5e-324,
	huge, -huge, 0 / 0,
	-- The +0.0001 epsilon in Common.lua's `floor` and in ModStore.lua:403 exists for
	-- exactly these: a quotient that should be an integer but lands one ulp short.
	0.30000000000000004 / 0.1, 2.9999999999999996, 3.0000000000000004,
	0.9999999999999999, 1.0000000000000002, 4.999999999999999, 5.000000000000001,
}
for _, v in ipairs(SPECIALS) do
	addWithNeighbours(v, true)
end

-- A pseudo-random spread so the corpus is not purely hand-picked. math.random is NOT
-- used: LuaJIT and Lua 5.1 ship different generators, and the corpus has to be
-- reproducible from either interpreter. This is a Lehmer LCG whose whole orbit stays
-- under 2^53, so every step is exact in double arithmetic on both.
local rngState = 20260209
local function nextRandom()
	rngState = (rngState * 16807) % 2147483647
	return rngState / 2147483647
end

for _ = 1, 150 do
	addValue((nextRandom() - 0.5) * 2000)
	addValue((nextRandom() - 0.5) * 20)
	addValue((nextRandom() - 0.5) * 2)
	addValue((nextRandom() - 0.5) * 2e-3)
end

----------------------------------------------------------------------------------------
-- Case emission
----------------------------------------------------------------------------------------

local cases = {}
local out = {}

local function emit(name, args, results)
	local a = {}
	for i = 1, args.n do
		local v = args[i]
		if v == nil then
			a[i] = "null"
		elseif type(v) == "number" then
			a[i] = '"' .. numToString(v) .. '"'
		else
			error("bad argument type " .. type(v))
		end
	end
	local r = {}
	for i = 1, results.n do
		r[i] = '"' .. numToString(results[i]) .. '"'
	end
	cases[#cases + 1] = string.format('["%s",[%s],[%s]]', name, table.concat(a, ","), table.concat(r, ","))
end

local function pack(...)
	return { n = select("#", ...), ... }
end

--- Call fn(...) and record the call and every value it returned.
local function record(name, fn, ...)
	emit(name, pack(...), pack(fn(...)))
end

-- --- Family 1: the six Common.lua helpers, no `dec` and across the dec sweep -----------

-- dec = 0 is NOT the same code path as no dec at all: Lua treats 0 as truthy, so
-- `floor(v, 0)` still adds the 0.0001 epsilon while `floor(v)` does not.
-- Every dec literal that appears at a call site in src/Classes and src/Modules is 1, 2, 3,
-- 6 or 10; 2 is swept over the whole input set above, the rest are here, along with 0 and
-- the negative decs that the helpers accept but nothing currently passes.
local DEC_SWEEP = { 0, 1, 3, 4, 6, 10, -1, -2 }

-- `floorSymmetric(val)` with no dec is `return select(1, math.modf(val))`, and Lua's
-- select(1, ...) yields EVERY value from index 1 onward -- so that branch returns both of
-- modf's results, not one. `alwaysPositiveRound(val)` tail-calls it and inherits the same
-- two-value return. The dec branches divide by the factor, which adjusts the call to a
-- single value, so only the no-dec branches are affected. Every real call site assigns the
-- result to one variable (e.g. src/Modules/ItemTools.lua:64), which also adjusts it to one
-- value, so that is the form recorded under the plain function name; the raw two-value
-- return is recorded separately below under a ".multi" name so the quirk stays pinned.
local function adjustToOneValue(fn)
	return function(...)
		return (fn(...))
	end
end

local HELPER_FNS = {
	{ "round", round },
	{ "floor", floor },
	{ "roundSymmetric", roundSymmetric },
	{ "alwaysPositiveRound", adjustToOneValue(alwaysPositiveRound) },
	{ "floorSymmetric", adjustToOneValue(floorSymmetric) },
	{ "ceilSymmetric", ceilSymmetric },
}

for _, entry in ipairs(HELPER_FNS) do
	local name, fn = entry[1], entry[2]
	for _, v in ipairs(values) do
		record(name, fn, v)
		record(name, fn, v, 2)
	end
	for _, v in ipairs(coreValues) do
		for _, dec in ipairs(DEC_SWEEP) do
			record(name, fn, v, dec)
		end
	end
end

-- The unadjusted two-value returns of the no-dec branches, so that the select(1, ...)
-- quirk above is asserted rather than merely commented on.
for _, v in ipairs(values) do
	record("floorSymmetric.multi", floorSymmetric, v)
	record("alwaysPositiveRound.multi", alwaysPositiveRound, v)
end

-- --- Family 2: the raw math primitives LuaCompat wraps ---------------------------------

for _, v in ipairs(values) do
	record("math.floor", math.floor, v)
	record("math.ceil", math.ceil, v)
	record("math.modf", math.modf, v)
end

for _, dec in ipairs({ -5, -2, -1, 0, 1, 2, 3, 4, 5, 6, 10, 15, 22 }) do
	emit("pow10", pack(dec), pack(10 ^ dec))
end

-- --- Family 3: ceil_b / floor_b --------------------------------------------------------

for _, base in ipairs({ 1, 2, 5, 0.5, 0.1 }) do
	for k = -40, 40 do
		record("ceil_b", ceil_b, k / 7, base)
		record("floor_b", floor_b, k / 7, base)
	end
end

-- --- Family 4: the four call-site shapes -----------------------------------------------

-- ModDB.lua:197 / ModList.lua:147 -- `result * round(modResult, 2)`, once per mod name.
local function moreScale(modResult)
	return round(modResult, 2)
end

-- ModDB.lua:193-195 / ModList.lua:143-145 -- the data.highPrecisionMods override.
-- NOTE: this is raw `math.floor`, NOT Common.lua's global `floor`; there is no epsilon.
local function highPrecisionMore(result, modResult, precision)
	local power = 10 ^ precision
	return math.floor(result * modResult * power) / power
end

-- ModStore.lua:403 -- `m_floor(base / (tag.div or 1) + 0.0001)` in the Multiplier tag.
-- NOTE: hand-rolled with the same epsilon as Common.lua's `floor`, but it is `m_floor`
-- (math.floor), not a call to the global helper.
local function multiplierFloor(base, div)
	return math.floor(base / div + 0.0001)
end

-- ModStore.lua:82 -- `m_modf(round(subMod.value * scale, 2))` in ScaleAddMod; only the
-- integral part is kept (Lua discards the second return in an assignment).
local function scaleModValue(value, scale)
	return (math.modf(round(value * scale, 2)))
end

-- ModStore.lua:79-80 -- the high-precision branch of the same site.
local function scaleModValueHighPrecision(value, scale, precision)
	local power = 10 ^ precision
	return math.floor(value * scale * power) / power
end

for _, v in ipairs(values) do
	record("site.moreScale", moreScale, v)
end

-- Multiplier tags: integral-ish counts over small integer divisors, plus the quotients
-- that land one ulp short of an integer, which is what the epsilon is defending against.
local MULT_DIVS = { 1, 2, 3, 5, 6, 10, 12, 25, 100, 0.5 }
for _, div in ipairs(MULT_DIVS) do
	for base = -20, 80 do
		record("site.multiplierFloor", multiplierFloor, base, div)
	end
	for base = -20, 20 do
		record("site.multiplierFloor", multiplierFloor, base + 0.5, div)
		record("site.multiplierFloor", multiplierFloor, base / 3, div)
		record("site.multiplierFloor", multiplierFloor, base * 0.1, div)
		-- 0.30000000000000004 = 0.1 + 0.2: quotients that land one ulp under an integer,
		-- which is precisely what the +0.0001 in ModStore.lua:403 exists to absorb.
		record("site.multiplierFloor", multiplierFloor, base * 0.30000000000000004, div)
	end
	-- Bases whose quotient lands exactly one ulp either side of a whole number: the only
	-- inputs on which the epsilon changes the answer at all, and therefore the only ones
	-- that can catch a port that dropped it.
	for n = -20, 20 do
		local exact = n * div
		local step = math.abs(exact) * EPS
		record("site.multiplierFloor", multiplierFloor, exact - step, div)
		record("site.multiplierFloor", multiplierFloor, exact + step, div)
		record("site.multiplierFloor", multiplierFloor, exact - step * 0.5, div)
	end
end

local SCALES = { 0.25, 1 / 3, 0.5, 0.75, 0.9, 1, 1.1, 1.5, 2, 0.1, -1, -0.5 }
for _, scale in ipairs(SCALES) do
	for v = -20, 80 do
		record("site.scaleModValue", scaleModValue, v, scale)
	end
	for v = -15, 15 do
		record("site.scaleModValue", scaleModValue, v + 0.5, scale)
		record("site.scaleModValue", scaleModValue, v / 10, scale)
		record("site.scaleModValue", scaleModValue, v / 100, scale)
	end
end

for _, precision in ipairs({ 1, 2, 3, 4 }) do
	for _, result in ipairs({ 1, 1.5, 0.5, 3.7, 100, 1 / 3 }) do
		for m = -20, 60 do
			record("site.highPrecisionMore", highPrecisionMore, result, 1 + m / 100, precision)
			record("site.scaleModValueHighPrecision", scaleModValueHighPrecision, result, m / 10, precision)
		end
	end
end

----------------------------------------------------------------------------------------
-- Write
----------------------------------------------------------------------------------------

out[#out + 1] = "{"
out[#out + 1] = string.format('"format":%d,', ORACLE_FORMAT)
out[#out + 1] = string.format('"generator":"%s",', "dotnet/tools/luacompat-oracle/dump.lua")
out[#out + 1] = string.format('"source":"%s",', "src/Modules/Common.lua")
out[#out + 1] = string.format('"interpreter":"%s",', (_VERSION or "?") .. (jit and (" / " .. jit.version) or ""))
out[#out + 1] = string.format('"inputValues":%d,', #values)
out[#out + 1] = string.format('"coreInputValues":%d,', #coreValues)
out[#out + 1] = string.format('"caseCount":%d,', #cases)
out[#out + 1] = '"schema":"[function, [arguments], [results]]; every number is a string, `null` means the Lua argument was absent (nil)",'
out[#out + 1] = '"cases":['
out[#out + 1] = table.concat(cases, ",\n")
out[#out + 1] = "]}"

local f = assert(io.open(outputPath, "wb"))
f:write(table.concat(out, "\n"))
f:close()

io.write(string.format("%s: %d input values, %d cases -> %s\n",
	(_VERSION or "?") .. (jit and (" / " .. jit.version) or ""), #values, #cases, outputPath))
