-- Path of Building — behaviour-key enumeration
--
-- A tiny deterministic JSON writer. Objects are emitted as ordered arrays of
-- {key, value} pairs, never as Lua hash tables, because `pairs()` order is not
-- stable and behaviour-keys.json has to be byte-identical across runs.
--
-- Strings are escaped conservatively: every character outside a small ASCII
-- safe set becomes \uXXXX. That keeps the emitted file free of the characters
-- MSBuild treats specially ('%', '$', '@', apostrophe, backtick), which is what
-- lets the completeness check in Pob.Data.csproj read this file with a plain
-- regex and no escaping games. Item names such as "Overlord's" therefore appear
-- as "Overlord's" — still valid JSON, read back correctly by any parser.

local M = {}

local SAFE = {}
for c in ("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
	.. " !#()*+,-./:;<=>?[]^_{|}~"):gmatch(".") do
	SAFE[c] = true
end

--- Characters that must never appear literally in the emitted file.
M.MSBUILD_HOSTILE = "%$@'`"

function M.escape(s)
	local out = {}
	for i = 1, #s do
		local c = s:sub(i, i)
		if SAFE[c] then
			out[#out + 1] = c
		else
			out[#out + 1] = string.format("\\u%04x", s:byte(i))
		end
	end
	return table.concat(out)
end

local encodeValue

local function encodeScalar(v)
	local t = type(v)
	if t == "string" then
		return "\"" .. M.escape(v) .. "\""
	elseif t == "number" then
		if v == math.floor(v) then
			return string.format("%d", v)
		end
		return tostring(v)
	elseif t == "boolean" then
		return tostring(v)
	elseif t == "nil" then
		return "null"
	end
	return nil
end

--- Marks a Lua array as an ordered JSON object: { {"key", value}, ... }
function M.object(pairsList)
	return { __object = true, entries = pairsList }
end

--- Marks a Lua array as a JSON array.
function M.array(items)
	return { __array = true, items = items }
end

encodeValue = function(v, indent, out)
	local scalar = encodeScalar(v)
	if scalar and type(v) ~= "table" then
		out[#out + 1] = scalar
		return
	end
	if type(v) ~= "table" then
		error("unencodable value of type " .. type(v))
	end
	local pad = string.rep("  ", indent)
	local padInner = string.rep("  ", indent + 1)
	if v.__object then
		if #v.entries == 0 then
			out[#out + 1] = "{}"
			return
		end
		out[#out + 1] = "{\n"
		for i, entry in ipairs(v.entries) do
			out[#out + 1] = padInner .. "\"" .. M.escape(entry[1]) .. "\": "
			encodeValue(entry[2], indent + 1, out)
			out[#out + 1] = (i < #v.entries) and ",\n" or "\n"
		end
		out[#out + 1] = pad .. "}"
	elseif v.__array then
		if #v.items == 0 then
			out[#out + 1] = "[]"
			return
		end
		out[#out + 1] = "[\n"
		for i, item in ipairs(v.items) do
			out[#out + 1] = padInner
			encodeValue(item, indent + 1, out)
			out[#out + 1] = (i < #v.items) and ",\n" or "\n"
		end
		out[#out + 1] = pad .. "]"
	else
		error("tables must be wrapped with json.object() or json.array()")
	end
end

function M.encode(value)
	local out = {}
	encodeValue(value, 0, out)
	out[#out + 1] = "\n"
	local text = table.concat(out)
	for i = 1, #M.MSBUILD_HOSTILE do
		local c = M.MSBUILD_HOSTILE:sub(i, i)
		if text:find(c, 1, true) then
			error(("emitted JSON contains %q, which the MSBuild completeness check cannot read"):format(c))
		end
	end
	return text
end

return M
