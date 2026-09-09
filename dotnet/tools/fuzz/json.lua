--[[
	Deterministic JSON writer for the fuzz corpus (migration ticket 08).

	Three properties the C# side depends on:

	1. Object keys are emitted in `table.sort` order, never `pairs` order. Lua's hash
	   iteration order is not specified and differs between LuaJIT and Lua 5.1, so any
	   `pairs` walk that reaches the output would break byte-identical reproduction.
	2. Doubles are emitted as JSON *strings*, never as JSON numbers: integers bare, and
	   everything else in the shortest "%.Ng" spelling that round-trips through
	   `tonumber`. A JSON number would invite the reader to reinterpret it; a string
	   cannot be silently re-rounded. Same rule as
	   dotnet/tools/luacompat-oracle/dump.lua, and Pob.Tests reads them back with
	   double.Parse(..., NumberStyles.Float, InvariantCulture).
	3. Non-finite values and the sign of zero get explicit spellings ("inf", "-inf",
	   "nan", "-0") rather than whatever the platform printf does with them.

	Output is compact except for a two-space indent on the structural levels, which keeps
	`git diff` on a committed corpus readable without exploding the file size.
]]

local json = {}

local huge = math.huge
local s_format = string.format

--- Shortest %g precision that survives a tonumber round-trip. 17 always works for an
--- IEEE double; the search keeps the corpus small and, more importantly, stops LuaJIT
--- and Lua 5.1 from spelling the same double differently (their printf implementations
--- disagree on the 17th significant digit).
function json.num(x)
	if type(x) ~= "number" then
		error("json.num: not a number: " .. tostring(x))
	end
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
	-- Integers get spelled as integers. `%.1g` on 750 is "8e+02" and the round-trip search
	-- below would settle on "7.5e+02", which round-trips exactly but makes counts and node
	-- ids unreadable in a diff. 2^53 is the last integer a double represents uniquely, so
	-- above that the general search takes over.
	if x == math.floor(x) and x >= -9007199254740992 and x <= 9007199254740992 then
		return s_format("%d", x)
	end
	for precision = 1, 17 do
		local candidate = s_format("%." .. precision .. "g", x)
		if tonumber(candidate) == x then
			return candidate
		end
	end
	error("json.num: no round-tripping spelling for " .. tostring(x))
end

local ESCAPES = {
	['"'] = '\\"',
	["\\"] = "\\\\",
	["\b"] = "\\b",
	["\f"] = "\\f",
	["\n"] = "\\n",
	["\r"] = "\\r",
	["\t"] = "\\t",
}

--- JSON string literal. Bytes >= 0x20 other than `"` and `\` pass through unchanged, so
--- UTF-8 in mod text stays UTF-8 rather than becoming \u escapes that differ between
--- encoders. Bytes below 0x20 become \u00XX.
function json.str(s)
	s = tostring(s)
	s = s:gsub('[%c"\\]', function(c)
		local esc = ESCAPES[c]
		if esc then
			return esc
		end
		return s_format("\\u%04X", string.byte(c))
	end)
	return '"' .. s .. '"'
end

--- Sorted key list for a string-keyed table. Errors on a non-string key rather than
--- silently coercing, because a mixed-key table has no stable ordering.
function json.sortedKeys(t)
	local keys = {}
	for k in pairs(t) do
		if type(k) ~= "string" then
			error("json.sortedKeys: non-string key " .. tostring(k))
		end
		keys[#keys + 1] = k
	end
	table.sort(keys)
	return keys
end

--- Encode a value. Tables are encoded as arrays when `#t > 0` or when they carry
--- `__array = true`, otherwise as objects with sorted keys. Numbers become strings (see
--- the header); booleans and nil become JSON literals.
function json.encode(value, indent)
	indent = indent or ""
	local nextIndent = indent .. "  "
	local vt = type(value)

	if value == nil then
		return "null"
	elseif vt == "boolean" then
		return value and "true" or "false"
	elseif vt == "number" then
		return json.str(json.num(value))
	elseif vt == "string" then
		return json.str(value)
	elseif vt ~= "table" then
		error("json.encode: unsupported type " .. vt)
	end

	local isArray = value.__array == true or #value > 0
	if isArray then
		if #value == 0 then
			return "[]"
		end
		local parts = {}
		for i = 1, #value do
			parts[i] = nextIndent .. json.encode(value[i], nextIndent)
		end
		return "[\n" .. table.concat(parts, ",\n") .. "\n" .. indent .. "]"
	end

	local keys = {}
	for _, k in ipairs(json.sortedKeys(value)) do
		if k ~= "__array" then
			keys[#keys + 1] = k
		end
	end
	if #keys == 0 then
		return "{}"
	end
	local parts = {}
	for i, k in ipairs(keys) do
		parts[i] = nextIndent .. json.str(k) .. ": " .. json.encode(value[k], nextIndent)
	end
	return "{\n" .. table.concat(parts, ",\n") .. "\n" .. indent .. "}"
end

--- Force a table to encode as an array even when empty.
function json.array(t)
	t = t or {}
	t.__array = true
	return t
end

return json
