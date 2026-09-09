-- Path of Building — behaviour-key enumeration
--
-- Walks the token stream of one Lua data file and reports every `function`
-- value in it, together with the table path it is bound to, the syntactic
-- context it appears in, and its exact source extent.
--
-- The classification is what separates the ~35 file-level dependency-injection
-- wrappers (`return function(itemBases)`) from real behaviour: a DI wrapper is a
-- function *returned by the chunk*, a behaviour is a function *stored in a data
-- table*. Nothing here is keyed off a hardcoded file list.

local lexer = require("lexer")

local M = {}

-- Tokens that terminate an lvalue run.
local SEPARATORS = {
	[","] = true, [";"] = true, ["{"] = true, ["}"] = true, ["="] = true,
	["("] = true, [")"] = true, ["["] = false, ["]"] = false,
}

local BLOCK_OPENERS = { ["function"] = true, ["if"] = true, ["do"] = true }

local function isSignificantSeparator(tok)
	if tok.type == "op" then
		return SEPARATORS[tok.value] == true
	end
	if tok.type == "keyword" then
		return tok.value ~= "nil" and tok.value ~= "true" and tok.value ~= "false"
			and tok.value ~= "not"
	end
	return false
end

-- Renders an lvalue token run as a dotted path segment:
--   skills [ "Arc" ]     -> skills.Arc
--   [ "of Balance" ]     -> of Balance
--   preDamageFunc        -> preDamageFunc
local function renderLValue(tokens, from, to)
	local parts = {}
	local i = from
	while i <= to do
		local tok = tokens[i]
		if tok.type == "name" then
			parts[#parts + 1] = tok.value
			i = i + 1
		elseif tok.type == "op" and tok.value == "." then
			i = i + 1
		elseif tok.type == "op" and tok.value == "[" then
			local inner = tokens[i + 1]
			if inner and (inner.type == "string" or inner.type == "number")
				and tokens[i + 2] and tokens[i + 2].value == "]" then
				local text = inner.value
				if inner.type == "string" then
					text = text:sub(2, -2)
				end
				parts[#parts + 1] = text
				i = i + 3
			else
				-- Computed key; keep it verbatim so it is visible in the output.
				local raw = {}
				local depth = 0
				repeat
					local t = tokens[i]
					if t.value == "[" then depth = depth + 1 end
					if t.value == "]" then depth = depth - 1 end
					raw[#raw + 1] = t.value
					i = i + 1
				until depth == 0 or i > to
				parts[#parts + 1] = table.concat(raw)
			end
		else
			parts[#parts + 1] = tok.value
			i = i + 1
		end
	end
	return table.concat(parts, ".")
end

-- Finds the `end` that closes the `function` token at index `from`.
local function findFunctionEnd(tokens, from)
	local depth = 0
	local i = from
	while i <= #tokens do
		local tok = tokens[i]
		if tok.type == "keyword" then
			local v = tok.value
			if BLOCK_OPENERS[v] then
				depth = depth + 1
			elseif v == "end" then
				depth = depth - 1
				if depth == 0 then
					return i
				end
			elseif v == "repeat" then
				-- `repeat ... until` closes without `end`; skip to its `until`.
				local inner = 0
				local j = i + 1
				while j <= #tokens do
					local t = tokens[j]
					if t.type == "keyword" then
						if BLOCK_OPENERS[t.value] or t.value == "repeat" then
							inner = inner + 1
						elseif t.value == "end" then
							inner = inner - 1
						elseif t.value == "until" and inner == 0 then
							break
						end
					end
					j = j + 1
				end
				i = j
			end
		end
		i = i + 1
	end
	error(("unterminated function starting at line %d"):format(tokens[from].line))
end

-- Renders a token range as a canonical, whitespace- and comment-insensitive
-- string. This is what gets hashed, so two bodies that differ only in
-- indentation or commentary group together.
local function canonicalise(tokens, from, to)
	local parts = {}
	for i = from, to do
		parts[#parts + 1] = tokens[i].value
	end
	return table.concat(parts, " ")
end

-- Walks back from a call-argument function to the name of the callee.
local function calleeName(tokens, functionIndex)
	local depth = 0
	local i = functionIndex - 1
	while i >= 1 do
		local v = tokens[i].value
		if v == ")" or v == "]" or v == "}" then
			depth = depth + 1
		elseif v == "(" or v == "[" or v == "{" then
			if depth == 0 and v == "(" then
				local from = i - 1
				while from >= 1 and (tokens[from].type == "name"
					or (tokens[from].type == "op" and (tokens[from].value == "." or tokens[from].value == ":"))) do
					from = from - 1
				end
				if from + 1 <= i - 1 then
					local parts = {}
					for j = from + 1, i - 1 do
						parts[#parts + 1] = tokens[j].value
					end
					return table.concat(parts)
				end
				return nil
			end
			depth = depth - 1
		end
		i = i - 1
	end
	return nil
end

--- Scans one file.
-- @param src      file contents
-- @param relPath  repository-relative path, used in the report only
-- @return array of function-site records
function M.scanFile(src, relPath)
	local tokens = lexer.tokenise(src)
	local sites = {}

	-- Scope stack. The bottom frame is the chunk; a frame is pushed for every
	-- table constructor (contributing a path segment) and every function body
	-- (contributing none).
	local scopes = { { kind = "chunk", arrayIndex = 0 } }
	local function top()
		return scopes[#scopes]
	end
	local function currentPath()
		local parts = {}
		for _, scope in ipairs(scopes) do
			if scope.segment and scope.segment ~= "" then
				parts[#parts + 1] = scope.segment
			end
		end
		return parts
	end
	local function functionDepth()
		local n = 0
		for _, scope in ipairs(scopes) do
			if scope.kind == "function" then
				n = n + 1
			end
		end
		return n
	end

	local lvalueStart = 1
	local pendingKey, pendingIsLocal = nil, false
	local i = 1

	while i <= #tokens do
		local tok = tokens[i]
		local prev = tokens[i - 1]

		if tok.type == "keyword" and tok.value == "function" then
			local nextTok = tokens[i + 1]
			local named = nextTok and nextTok.type == "name"
			local endIndex = findFunctionEnd(tokens, i)
			local site = {
				file = relPath,
				line = tok.line,
				tokenStart = i,
				tokenEnd = endIndex,
				pos = tok.pos,
				endPos = tokens[endIndex].pos + #tokens[endIndex].value - 1,
				endLine = tokens[endIndex].line,
				functionDepth = functionDepth(),
				tablePath = currentPath(),
				canonical = canonicalise(tokens, i, endIndex),
				firstLine = (src:sub(tok.pos):match("^[^\n]*") or ""):gsub("^%s+", ""),
			}

			if named then
				local nameFrom = i + 1
				local nameTo = nameFrom
				while tokens[nameTo + 1] and tokens[nameTo + 1].type == "op"
					and (tokens[nameTo + 1].value == "." or tokens[nameTo + 1].value == ":") do
					nameTo = nameTo + 2
				end
				site.context = "named-declaration"
				site.binding = renderLValue(tokens, nameFrom, nameTo)
				site.isLocal = prev and prev.value == "local" or false
			elseif prev and prev.type == "op" and prev.value == "=" then
				site.context = "assignment"
				site.binding = pendingKey
				site.isLocal = pendingIsLocal
			elseif prev and prev.type == "keyword" and prev.value == "return" then
				site.context = "return"
			elseif prev and prev.type == "op" and (prev.value == "(" or prev.value == ",") then
				site.context = "call-argument"
				site.binding = calleeName(tokens, i)
			else
				site.context = "other"
			end

			-- An immediately-invoked function expression: `(function() ... end)()`.
			local afterEnd = tokens[endIndex + 1]
			if site.context == "call-argument" and prev.value == "("
				and afterEnd and afterEnd.value == ")"
				and tokens[endIndex + 2] and tokens[endIndex + 2].value == "("
				and tokens[endIndex + 3] and tokens[endIndex + 3].value == ")" then
				site.context = "immediately-invoked"
				site.binding = nil
			end

			-- Parameter names, for the hook signature report.
			if tokens[i + (named and 2 or 1)] and tokens[i + (named and 2 or 1)].value == "(" then
				local params = {}
				local j = i + (named and 2 or 1) + 1
				while tokens[j] and tokens[j].value ~= ")" do
					if tokens[j].type == "name" or tokens[j].value == "..." then
						params[#params + 1] = tokens[j].value
					end
					j = j + 1
				end
				site.params = params
			end

			sites[#sites + 1] = site

			-- Enter the function body; its inner tokens are walked normally so
			-- nested functions are reported too.
			scopes[#scopes + 1] = { kind = "function", arrayIndex = 0 }
			pendingKey, pendingIsLocal = nil, false
			lvalueStart = i + 1
			i = i + 1
		elseif tok.type == "op" and tok.value == "=" then
			local from = lvalueStart
			pendingKey = renderLValue(tokens, from, i - 1)
			pendingIsLocal = from >= 2 and tokens[from - 1].value == "local" or false
			lvalueStart = i + 1
			i = i + 1
		elseif tok.type == "op" and tok.value == "{" then
			local segment = pendingKey
			if not segment then
				local frame = top()
				if prev and (prev.value == "return" or prev.value == "(" or prev.value == ",") then
					segment = nil
				elseif frame.kind == "table" then
					frame.arrayIndex = frame.arrayIndex + 1
					segment = "[" .. frame.arrayIndex .. "]"
				end
			end
			scopes[#scopes + 1] = { kind = "table", segment = segment, arrayIndex = 0 }
			pendingKey, pendingIsLocal = nil, false
			lvalueStart = i + 1
			i = i + 1
		elseif tok.type == "op" and tok.value == "}" then
			if #scopes > 1 then
				table.remove(scopes)
			end
			pendingKey, pendingIsLocal = nil, false
			lvalueStart = i + 1
			i = i + 1
		elseif tok.type == "keyword" and (tok.value == "if" or tok.value == "do" or tok.value == "repeat") then
			scopes[#scopes + 1] = { kind = tok.value == "repeat" and "repeat" or "block", arrayIndex = 0 }
			pendingKey, pendingIsLocal = nil, false
			lvalueStart = i + 1
			i = i + 1
		elseif tok.type == "keyword" and (tok.value == "end" or tok.value == "until") then
			local wanted = tok.value == "until" and "repeat" or nil
			for k = #scopes, 2, -1 do
				local kind = scopes[k].kind
				local match = wanted and kind == wanted
					or (not wanted and (kind == "function" or kind == "block"))
				if match then
					for _ = #scopes, k, -1 do
						table.remove(scopes)
					end
					break
				end
			end
			pendingKey, pendingIsLocal = nil, false
			lvalueStart = i + 1
			i = i + 1
		else
			if isSignificantSeparator(tok) then
				if tok.type == "op" and tok.value == "," and top().kind == "table" and not pendingKey then
					top().arrayIndex = top().arrayIndex + 1
				end
				pendingKey, pendingIsLocal = nil, false
				lvalueStart = i + 1
			end
			i = i + 1
		end
	end

	return sites, tokens
end

return M
