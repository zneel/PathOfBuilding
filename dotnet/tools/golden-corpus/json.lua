-- Deterministic JSON writer for the golden output corpus (migration ticket 07).
--
-- Not a general-purpose JSON library: it exists to make byte-identical files across
-- runs, which is the corpus's only interesting property besides the numbers themselves.
--
-- Two rules are load-bearing:
--
--  * Object keys are emitted in a fixed sort order, never `pairs` order. LuaJIT's table
--    iteration order is an implementation detail and reordering the file would make every
--    regeneration a spurious diff.
--  * Numbers are formatted losslessly. Integral values within 2^53 print as integers;
--    everything else prints with `%.17g`, which is the shortest format guaranteed to
--    round-trip an IEEE-754 double. The existing `spec/GenerateBuilds.lua` rounds to 4
--    decimal places, which throws away the information the C# port will eventually need in
--    order to tighten from `1e-9` relative to bit-exact. Rounding at generation time cannot
--    be undone, so this writer never rounds.
--
-- Non-finite doubles have no JSON spelling. They are written as the sentinel strings
-- "__inf", "__-inf" and "__nan"; `GoldenCorpus.cs` maps them back. A real string value
-- colliding with one of those is not possible in practice - the engine's string outputs are
-- skill and item names.

local m = {}

local s_format = string.format
local s_byte = string.byte
local t_concat = table.concat
local m_floor = math.floor
local m_huge = math.huge

local MAX_EXACT_INTEGER = 9007199254740992 -- 2^53

--- Formats a Lua number as a JSON token, losslessly.
function m.number(v)
	if v ~= v then
		return '"__nan"'
	elseif v == m_huge then
		return '"__inf"'
	elseif v == -m_huge then
		return '"__-inf"'
	elseif v == m_floor(v) and v < MAX_EXACT_INTEGER and v > -MAX_EXACT_INTEGER then
		-- %.0f rather than %d: %d on a non-integral double is a LuaJIT error, and this
		-- branch is the only one that is guaranteed integral. Keeps the sign of -0.
		return s_format("%.0f", v)
	end
	return s_format("%.17g", v)
end

local escapes = {
	['"'] = '\\"',
	['\\'] = '\\\\',
	['\b'] = '\\b',
	['\f'] = '\\f',
	['\n'] = '\\n',
	['\r'] = '\\r',
	['\t'] = '\\t',
}

--- Quotes a Lua byte string as a JSON string.
-- Bytes >= 0x20 pass through untouched, so UTF-8 input (build XML) stays UTF-8 output.
function m.string(v)
	local out = v:gsub('[%c"\\]', function(c)
		return escapes[c] or s_format("\\u%04x", s_byte(c))
	end)
	return '"' .. out .. '"'
end

--- Sorts mixed number/string keys into one stable order: numbers first, ascending,
--- then strings, byte-lexicographic.
local function keyLess(a, b)
	local ta, tb = type(a), type(b)
	if ta ~= tb then
		return ta == "number"
	end
	return a < b
end

--- Returns the keys of `tbl` in canonical order.
function m.sortedKeys(tbl)
	local keys = {}
	for k in pairs(tbl) do
		keys[#keys + 1] = k
	end
	table.sort(keys, keyLess)
	return keys
end

local writeValue

--- Writes an array of already-encoded chunks.
local function writeArray(buf, arr, indent)
	if #arr == 0 then
		buf[#buf + 1] = "[]"
		return
	end
	local inner = indent .. "  "
	buf[#buf + 1] = "[\n"
	for i = 1, #arr do
		buf[#buf + 1] = inner
		writeValue(buf, arr[i], inner)
		buf[#buf + 1] = i < #arr and ",\n" or "\n"
	end
	buf[#buf + 1] = indent .. "]"
end

--- Writes a table as a JSON object with sorted keys.
-- `tbl.__order` (a list of keys) overrides the sort for the few places where a
-- hand-authored reading order is clearer than alphabetical; keys not named there follow,
-- sorted.
local function writeObject(buf, tbl, indent)
	local keys
	if tbl.__order then
		keys = {}
		local named = {}
		for _, k in ipairs(tbl.__order) do
			if tbl[k] ~= nil then
				keys[#keys + 1] = k
				named[k] = true
			end
		end
		for _, k in ipairs(m.sortedKeys(tbl)) do
			if not named[k] and k ~= "__order" then
				keys[#keys + 1] = k
			end
		end
	else
		keys = m.sortedKeys(tbl)
	end
	if #keys == 0 then
		buf[#buf + 1] = "{}"
		return
	end
	local inner = indent .. "  "
	buf[#buf + 1] = "{\n"
	for i = 1, #keys do
		local k = keys[i]
		buf[#buf + 1] = inner
		buf[#buf + 1] = m.string(tostring(k))
		buf[#buf + 1] = ": "
		writeValue(buf, tbl[k], inner)
		buf[#buf + 1] = i < #keys and ",\n" or "\n"
	end
	buf[#buf + 1] = indent .. "}"
end

writeValue = function(buf, v, indent)
	local t = type(v)
	if v == nil then
		buf[#buf + 1] = "null"
	elseif t == "number" then
		buf[#buf + 1] = m.number(v)
	elseif t == "boolean" then
		buf[#buf + 1] = v and "true" or "false"
	elseif t == "string" then
		buf[#buf + 1] = m.string(v)
	elseif t == "table" then
		if v.__array then
			writeArray(buf, v.__array, indent)
		elseif #v > 0 then
			writeArray(buf, v, indent)
		else
			writeObject(buf, v, indent)
		end
	else
		error("golden-corpus/json: cannot encode a " .. t)
	end
end

--- Encodes a value as pretty-printed JSON text with a trailing newline.
function m.encode(v)
	local buf = {}
	writeValue(buf, v, "")
	buf[#buf + 1] = "\n"
	return t_concat(buf)
end

--- Marks a Lua list so it is written as a JSON array even when empty.
function m.array(list)
	return { __array = list or {} }
end

return m
