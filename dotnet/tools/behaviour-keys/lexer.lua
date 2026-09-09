-- Path of Building — behaviour-key enumeration
--
-- A minimal Lua 5.1 lexer. Enough of the grammar to tokenise `src/Data/**.lua`
-- exactly: comments (line and long-bracket), strings (quoted and long-bracket),
-- numbers, names, keywords and operators. Tokenising rather than pattern-matching
-- is what lets the enumerator tell a real `function` keyword from the word
-- "function" inside a flavour-text string (`src/Data/FlavourText.lua:2985`).
--
-- Runs on Lua 5.1 and LuaJIT 2.1. No external dependencies.

local M = {}

local KEYWORDS = {}
for word in ([[and break do else elseif end false for function if in local nil not
or repeat return then true until while]]):gmatch("%S+") do
	KEYWORDS[word] = true
end

M.KEYWORDS = KEYWORDS

-- Reads a long bracket ([[...]], [=[...]=], ...) starting at `i`.
-- Returns the index just past the closing bracket, or nil when `i` does not
-- start a long bracket.
local function readLongBracket(src, i)
	if src:sub(i, i) ~= "[" then
		return nil
	end
	local level = 0
	local j = i + 1
	while src:sub(j, j) == "=" do
		level = level + 1
		j = j + 1
	end
	if src:sub(j, j) ~= "[" then
		return nil
	end
	local close = "]" .. string.rep("=", level) .. "]"
	local closeStart, closeEnd = src:find(close, j + 1, true)
	if not closeStart then
		return nil, "unterminated long bracket"
	end
	return closeEnd + 1
end

local function countNewlines(src, from, to)
	local n = 0
	local i = from
	while true do
		local nl = src:find("\n", i, true)
		if not nl or nl >= to then
			break
		end
		n = n + 1
		i = nl + 1
	end
	return n
end

--- Tokenises `src`.
-- Returns an array of { type, value, pos, line } where type is one of
-- "name", "keyword", "number", "string", "op", "eof".
function M.tokenise(src)
	local tokens = {}
	local i = 1
	local line = 1
	local len = #src
	local function push(kind, value, pos)
		tokens[#tokens + 1] = { type = kind, value = value, pos = pos, line = line }
	end
	while i <= len do
		local c = src:sub(i, i)
		if c == "\n" then
			line = line + 1
			i = i + 1
		elseif c == " " or c == "\t" or c == "\r" or c == "\v" or c == "\f" then
			i = i + 1
		elseif src:sub(i, i + 1) == "--" then
			local afterLong = readLongBracket(src, i + 2)
			if afterLong then
				line = line + countNewlines(src, i, afterLong)
				i = afterLong
			else
				local nl = src:find("\n", i, true)
				i = nl or (len + 1)
			end
		elseif c:match("[%a_]") then
			local word = src:match("^[%a_][%w_]*", i)
			push(KEYWORDS[word] and "keyword" or "name", word, i)
			i = i + #word
		elseif c:match("%d") or (c == "." and src:sub(i + 1, i + 1):match("%d")) then
			local num = src:match("^0[xX]%x+", i)
				or src:match("^%d*%.?%d*[eE][%+%-]?%d+", i)
				or src:match("^%d*%.?%d+", i)
				or src:match("^%d+%.?", i)
			push("number", num, i)
			i = i + #num
		elseif c == "\"" or c == "'" then
			local start = i
			local j = i + 1
			while j <= len do
				local d = src:sub(j, j)
				if d == "\\" then
					if src:sub(j + 1, j + 1) == "\n" then
						line = line + 1
					end
					j = j + 2
				elseif d == c then
					j = j + 1
					break
				elseif d == "\n" then
					error(("unterminated string at line %d"):format(line))
				else
					j = j + 1
				end
			end
			push("string", src:sub(start, j - 1), start)
			i = j
		elseif c == "[" then
			local afterLong, err = readLongBracket(src, i)
			if err then
				error(err)
			end
			if afterLong then
				push("string", src:sub(i, afterLong - 1), i)
				line = line + countNewlines(src, i, afterLong)
				i = afterLong
			else
				push("op", "[", i)
				i = i + 1
			end
		else
			local op = src:match("^%.%.%.", i)
				or src:match("^%.%.", i)
				or src:match("^[=~<>]=", i)
				or src:match("^::", i)
				or c
			push("op", op, i)
			i = i + #op
		end
	end
	push("eof", "<eof>", len + 1)
	return tokens
end

return M
