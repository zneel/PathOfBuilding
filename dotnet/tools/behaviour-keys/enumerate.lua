#!/usr/bin/env lua5.1
-- Path of Building — behaviour-key enumeration (migration ticket 04)
--
-- Walks src/Data/**.lua, finds every Lua `function` value, classifies it, and
-- writes the authoritative behaviour-key contract to dotnet/Pob.Data/behaviour-keys.json.
--
--   lua5.1 dotnet/tools/behaviour-keys/enumerate.lua            # rewrite the JSON
--   lua5.1 dotnet/tools/behaviour-keys/enumerate.lua --check    # fail if it would change
--   lua5.1 dotnet/tools/behaviour-keys/enumerate.lua --report   # human summary on stdout
--
-- Re-run it after every league data drop. The output is deterministic and
-- diffable: new behaviours show up as added "key" entries, and the completeness
-- check in dotnet/Pob.Data/Pob.Data.csproj then fails the build until each new
-- key is triaged into the registry roster.
--
-- Runs on lua5.1 and luajit 2.1. No external dependencies.

local scriptDir = (arg[0] or ""):match("^(.*)[/\\][^/\\]*$") or "."
package.path = scriptDir .. "/?.lua;" .. package.path

local scan = require("scan")
local hash = require("hash")
local json = require("json")

--------------------------------------------------------------------------------
-- Configuration
--------------------------------------------------------------------------------

-- Files whose function values are *not* runtime behaviour. This is the only
-- policy in the script; everything else is derived from the source. Each entry
-- has to justify itself, because anything listed here is code the C# port will
-- never run.
local FILE_DISPOSITION = {
	["src/Data/Uniques/Special/Generated.lua"] = {
		disposition = "transcode-time",
		reason = "Pure string synthesis of legacy unique variants. Ticket 03 runs this file "
			.. "once during transcoding and emits the expanded uniques as data, so none of its "
			.. "function values become behaviour keys.",
	},
}

-- How each hook is called, taken from the engine call sites rather than from the
-- data files, because the data files only show the parameters a given body
-- happens to use.
local HOOKS = {
	["initialFunc"] = {
		delegate = "SkillBehaviour",
		callSite = "src/Modules/CalcOffence.lua:516 via runSkillFunc",
		signature = "(activeSkill, output, breakdown)",
	},
	["preSkillTypeFunc"] = {
		delegate = "SkillBehaviour",
		callSite = "src/Modules/CalcOffence.lua:1054 via runSkillFunc",
		signature = "(activeSkill, output, breakdown)",
	},
	["preDamageFunc"] = {
		delegate = "SkillBehaviour",
		callSite = "src/Modules/CalcOffence.lua:1908 via runSkillFunc",
		signature = "(activeSkill, output, breakdown)",
	},
	["postCritFunc"] = {
		delegate = "SkillBehaviour",
		callSite = "src/Modules/CalcOffence.lua:3315 via runSkillFunc",
		signature = "(activeSkill, output, breakdown)",
	},
	["explosiveArrowFunc"] = {
		delegate = "ExplosiveArrowBehaviour",
		callSite = "src/Modules/CalcOffence.lua:3038",
		signature = "(activeSkill, output, globalOutput, globalBreakdown, env)",
	},
	["apply"] = {
		delegate = "none - declarative",
		callSite = "src/Modules/ConfigOptions.lua:122-126 (mapAffixDropDownFunction)",
		signature = "(val, [rollRange,] mapModEffect, [values,] modList, enemyModList)",
	},
	["legacyMod"] = {
		delegate = "none - transcode-time",
		callSite = "src/Data/Uniques/Special/Generated.lua, at data load",
		signature = "(currentMod)",
	},
}

-- Hook name -> the stem used when building a key. Derived mechanically (drop a
-- trailing "Func", capitalise), listed explicitly so a new hook is a visible
-- diff rather than a silent naming decision.
local HOOK_STEM = {
	initialFunc = "Initial",
	preSkillTypeFunc = "PreSkillType",
	preDamageFunc = "PreDamage",
	postCritFunc = "PostCrit",
	explosiveArrowFunc = "ExplosiveArrow",
}

-- Semantic names for behaviour groups whose job is unambiguous from the body.
-- Keyed by body hash so that if the body ever changes the override lapses and
-- the group falls back to the deterministic name (the script reports unused
-- overrides). Everything not listed here keeps its derived <Owner><Hook> name.
local SEMANTIC_NAMES = require("names")

--------------------------------------------------------------------------------
-- Helpers
--------------------------------------------------------------------------------

local function byteLess(a, b)
	-- Locale-independent ordering; Lua's `<` on strings goes through strcoll.
	local minLen = math.min(#a, #b)
	for i = 1, minLen do
		local ca, cb = a:byte(i), b:byte(i)
		if ca ~= cb then
			return ca < cb
		end
	end
	return #a < #b
end

local function sortedKeys(t)
	local keys = {}
	for k in pairs(t) do
		keys[#keys + 1] = k
	end
	table.sort(keys, byteLess)
	return keys
end

local function readFile(path)
	local f = assert(io.open(path, "rb"), "cannot open " .. path)
	local content = f:read("*a")
	f:close()
	return content
end

local function listLuaFiles(root)
	local pipe = assert(io.popen("find " .. root .. "/src/Data -type f -name '*.lua' 2>/dev/null"))
	local files = {}
	for line in pipe:lines() do
		files[#files + 1] = line
	end
	pipe:close()
	table.sort(files, byteLess)
	return files
end

local function pascal(s)
	local out = {}
	for word in tostring(s):gmatch("[%a%d]+") do
		out[#out + 1] = word:sub(1, 1):upper() .. word:sub(2)
	end
	local joined = table.concat(out)
	if joined:match("^%d") then
		joined = "N" .. joined
	end
	return joined
end

--------------------------------------------------------------------------------
-- Scan
--------------------------------------------------------------------------------

local root = "."
local mode = "write"
for i = 1, #arg do
	local a = arg[i]
	if a == "--check" then
		mode = "check"
	elseif a == "--report" then
		mode = "report"
	elseif a == "--root" then
		root = arg[i + 1]
	end
end
if root == "." then
	root = scriptDir .. "/../../.."
end

local files = listLuaFiles(root)
if #files == 0 then
	error("no Lua files found under " .. root .. "/src/Data")
end

local allSites = {}
local fileTokens = {}
for _, path in ipairs(files) do
	local rel = path:gsub("^" .. root:gsub("([^%w])", "%%%1") .. "/?", "")
	local src = readFile(path)
	local sites, tokens = scan.scanFile(src, rel)
	fileTokens[rel] = { tokens = tokens, src = src }
	-- Sites arrive in source order, so a simple stack gives each one its
	-- enclosing function: a `local function hitChance` declared inside a
	-- preDamageFunc body is a helper of that behaviour, not a behaviour.
	local stack = {}
	for _, site in ipairs(sites) do
		while #stack > 0 and stack[#stack].endPos < site.pos do
			table.remove(stack)
		end
		site.enclosing = stack[#stack]
		stack[#stack + 1] = site
		site.src = src
		allSites[#allSites + 1] = site
	end
end

-- Self-check: every extracted extent must be a syntactically complete Lua
-- function. If the block matcher ever slips, this fails loudly instead of
-- emitting a plausible-looking but wrong key list.
local loadstring = loadstring or load
for _, site in ipairs(allSites) do
	local body = site.src:sub(site.pos, site.endPos)
	local chunk = site.context == "named-declaration" and body or ("return " .. body)
	local ok, err = loadstring(chunk, "@" .. site.file .. ":" .. site.line)
	if not ok then
		error(("extraction failed at %s:%d: %s"):format(site.file, site.line, tostring(err)))
	end
end

--------------------------------------------------------------------------------
-- Classification
--------------------------------------------------------------------------------

local function dispositionOf(file)
	local entry = FILE_DISPOSITION[file]
	return entry and entry.disposition or "runtime"
end

local CATEGORY = {}

for _, site in ipairs(allSites) do
	local category
	if site.context == "return" and site.functionDepth == 0 then
		category = "di-wrapper"
	elseif site.enclosing and site.enclosing.context ~= "return" then
		category = "nested-helper"
	elseif site.context == "immediately-invoked" then
		category = "chunk-wrapper"
	elseif site.context == "named-declaration" then
		category = "named-declaration"
	elseif site.context == "call-argument" then
		category = "call-argument"
	elseif site.context == "assignment" and #site.tablePath > 0 then
		category = "data-field"
	elseif site.context == "assignment" then
		category = site.isLocal and "local-value" or "global-value"
	else
		category = "unclassified"
	end
	site.category = category
	site.disposition = dispositionOf(site.file)
	site.owner = site.tablePath[#site.tablePath]
	site.ownerLeaf = site.owner and (site.owner:match("([^%.]+)$") or site.owner) or nil
	site.hook = site.binding
	CATEGORY[category] = (CATEGORY[category] or 0) + 1
end

-- A behaviour is a function value stored in a data table, in a file whose
-- contents survive to runtime, under a hook the engine actually calls.
local behaviourSites = {}
local declarativeSites = {}
local transcodeSites = {}
for _, site in ipairs(allSites) do
	if site.category == "data-field" then
		if site.disposition ~= "runtime" then
			transcodeSites[#transcodeSites + 1] = site
		elseif site.hook == "apply" then
			declarativeSites[#declarativeSites + 1] = site
		else
			behaviourSites[#behaviourSites + 1] = site
		end
	end
end

for _, site in ipairs(behaviourSites) do
	if not HOOKS[site.hook] then
		error(("%s:%d binds an unknown hook %q. Add it to HOOKS in enumerate.lua and "
			.. "give it a delegate in Pob.Data before regenerating."):format(site.file, site.line, tostring(site.hook)))
	end
end

--------------------------------------------------------------------------------
-- Grouping and naming
--------------------------------------------------------------------------------

for _, site in ipairs(behaviourSites) do
	site.bodyHash = hash.fnv1a64(site.canonical)
end

local groups, groupOrder = {}, {}
for _, site in ipairs(behaviourSites) do
	local id = site.hook .. "/" .. site.bodyHash
	local group = groups[id]
	if not group then
		group = { hook = site.hook, bodyHash = site.bodyHash, sites = {} }
		groups[id] = group
		groupOrder[#groupOrder + 1] = id
	end
	group.sites[#group.sites + 1] = site
end

local usedOverrides = {}
local takenKeys = {}
for _, id in ipairs(groupOrder) do
	local group = groups[id]
	table.sort(group.sites, function(a, b)
		if a.file ~= b.file then
			return byteLess(a.file, b.file)
		end
		return a.line < b.line
	end)
	local stem = HOOK_STEM[group.hook] or pascal(group.hook)
	-- Deterministic name: the lexicographically first owner in the group, so the
	-- name does not depend on file order or on which duplicate was written first.
	local owners = {}
	for _, site in ipairs(group.sites) do
		owners[#owners + 1] = site.ownerLeaf or "Unowned"
	end
	table.sort(owners, byteLess)
	local derived = pascal(owners[1]) .. stem
	local semantic = SEMANTIC_NAMES[group.bodyHash]
	if semantic then
		usedOverrides[group.bodyHash] = true
	end
	local key = semantic or derived
	group.derivedFrom = semantic and "semantic" or "owner+hook"
	if takenKeys[key] then
		local n = 2
		while takenKeys[key .. n] do
			n = n + 1
		end
		key = key .. n
		group.derivedFrom = group.derivedFrom .. "+disambiguated"
	end
	takenKeys[key] = true
	group.key = key
end

for bodyHash in pairs(SEMANTIC_NAMES) do
	if not usedOverrides[bodyHash] then
		io.stderr:write(("warning: semantic name override %s in names.lua matches no behaviour "
			.. "body; the body it named has changed or gone. Re-check it.\n"):format(bodyHash))
	end
end

table.sort(groupOrder, function(a, b)
	return byteLess(groups[a].key, groups[b].key)
end)

--------------------------------------------------------------------------------
-- ModMap.apply: declarative extraction
--------------------------------------------------------------------------------

-- Every `apply` body is meant to be a flat sequence of
-- `<target>:NewMod(name, type, value, source, ...)` calls. This lifts them into
-- data and reports precisely which entries do not fit that shape.
local function extractNewMods(site)
	local tokens = fileTokens[site.file].tokens
	local src = fileTokens[site.file].src
	local mods, leftovers = {}, {}
	-- Skip the parameter list.
	local i = site.tokenStart + 1
	if tokens[i] and tokens[i].value == "(" then
		while tokens[i] and tokens[i].value ~= ")" do
			i = i + 1
		end
		i = i + 1
	end
	while i < site.tokenEnd do
		local t = tokens[i]
		if t.type == "name" and tokens[i + 1] and tokens[i + 1].value == ":"
			and tokens[i + 2] and tokens[i + 2].value == "NewMod"
			and tokens[i + 3] and tokens[i + 3].value == "(" then
			local target = t.value
			local depth = 0
			local argStart = i + 4
			local args, current = {}, argStart
			local j = i + 3
			while j < site.tokenEnd do
				local v = tokens[j].value
				if v == "(" or v == "{" or v == "[" then
					depth = depth + 1
				elseif v == ")" or v == "}" or v == "]" then
					depth = depth - 1
					if depth == 0 then
						if j > current then
							args[#args + 1] = src:sub(tokens[current].pos, tokens[j - 1].pos + #tokens[j - 1].value - 1)
						end
						break
					end
				elseif v == "," and depth == 1 then
					args[#args + 1] = src:sub(tokens[current].pos, tokens[j - 1].pos + #tokens[j - 1].value - 1)
					current = j + 1
				end
				j = j + 1
			end
			local function unquote(s)
				if not s then return nil end
				s = s:gsub("^%s+", ""):gsub("%s+$", "")
				local inner = s:match('^"(.*)"$')
				return inner or s
			end
			-- The shape of the value expression with the concrete indices and literals
			-- removed. The 41 apply bodies use only a handful of shapes, which is what
			-- makes them expressible as data rather than as an expression evaluator.
			local function valueShape(expr)
				local shape = expr:gsub("values%[val%]", "V")
				shape = shape:gsub("V%b[]", "V"):gsub("V%b[]", "V")
				shape = shape:gsub("%d+", "N")
				return shape
			end
			mods[#mods + 1] = {
				line = t.line,
				target = target,
				name = unquote(args[1]),
				modType = unquote(args[2]),
				valueExpr = (args[3] or ""):gsub("^%s+", ""):gsub("%s+$", ""),
				valueShape = valueShape((args[3] or ""):gsub("^%s+", ""):gsub("%s+$", "")),
				source = unquote(args[4]),
				extraArgs = { unpack(args, 5) },
			}
			i = j + 1
		elseif t.type == "keyword" and (t.value == "end" or t.value == "then" or t.value == "else") then
			i = i + 1
		else
			leftovers[#leftovers + 1] = { line = t.line, text = t.value }
			i = i + 1
		end
	end
	return mods, leftovers
end

-- The affix's own `type` field, a sibling of `apply` in the same table. It decides
-- the argument list the config layer calls apply with (check/list/count, see
-- mapAffixDropDownFunction), so the declarative rows are unusable without it.
local function affixType(site)
	local tokens = fileTokens[site.file].tokens
	local depth = 0
	for i = site.tokenStart - 1, 1, -1 do
		local v = tokens[i].value
		if v == "}" then
			depth = depth + 1
		elseif v == "{" then
			if depth == 0 then
				return nil
			end
			depth = depth - 1
		elseif depth == 0 and tokens[i].type == "name" and v == "type"
			and tokens[i + 1] and tokens[i + 1].value == "="
			and tokens[i + 2] and tokens[i + 2].type == "string" then
			return tokens[i + 2].value:sub(2, -2)
		end
	end
	return nil
end

local declarative = {}
for _, site in ipairs(declarativeSites) do
	local mods, leftovers = extractNewMods(site)
	local nonDeclarative = nil
	if #leftovers > 0 then
		-- Quote the offending source line, so the report says what does not fit
		-- rather than only that something does not.
		local firstLine = leftovers[1].line
		local n, offending = 1, ""
		for l in site.src:gmatch("([^\n]*)\n?") do
			if n == firstLine then
				offending = l:gsub("^%s+", ""):gsub("%s+$", "")
				break
			end
			n = n + 1
		end
		nonDeclarative = ("line %d: %s"):format(firstLine, offending)
	end
	declarative[#declarative + 1] = {
		site = site,
		mods = mods,
		affixType = affixType(site),
		nonDeclarative = nonDeclarative,
	}
end
table.sort(declarative, function(a, b)
	return byteLess(a.site.ownerLeaf or "", b.site.ownerLeaf or "")
end)

--------------------------------------------------------------------------------
-- Report
--------------------------------------------------------------------------------

local hookCounts = {}
for _, site in ipairs(allSites) do
	if site.category == "data-field" then
		hookCounts[site.hook] = (hookCounts[site.hook] or 0) + 1
	end
end

if mode == "report" then
	print("function values found: " .. #allSites)
	print("")
	print("by category:")
	for _, k in ipairs(sortedKeys(CATEGORY)) do
		print(("  %-20s %d"):format(k, CATEGORY[k]))
	end
	print("")
	print("data-table function values by hook:")
	for _, k in ipairs(sortedKeys(hookCounts)) do
		print(("  %-20s %d"):format(k, hookCounts[k]))
	end
	print("")
	local perHookSites, perHookKeys = {}, {}
	for _, site in ipairs(behaviourSites) do
		perHookSites[site.hook] = (perHookSites[site.hook] or 0) + 1
	end
	for _, id in ipairs(groupOrder) do
		perHookKeys[groups[id].hook] = (perHookKeys[groups[id].hook] or 0) + 1
	end
	print("dedup per hook (sites -> distinct bodies):")
	for _, k in ipairs(sortedKeys(perHookSites)) do
		print(("  %-20s %3d -> %3d"):format(k, perHookSites[k], perHookKeys[k]))
	end
	print("")
	print(("behaviour sites: %d  -> distinct keys: %d"):format(#behaviourSites, #groupOrder))
	print(("declarative (ModMap.apply) entries: %d"):format(#declarativeSites))
	print(("transcode-time function values: %d"):format(#transcodeSites))
	print("")
	print("largest duplicate groups:")
	local byCount = {}
	for _, id in ipairs(groupOrder) do
		byCount[#byCount + 1] = groups[id]
	end
	table.sort(byCount, function(a, b)
		if #a.sites ~= #b.sites then
			return #a.sites > #b.sites
		end
		return byteLess(a.key, b.key)
	end)
	for i = 1, math.min(12, #byCount) do
		local g = byCount[i]
		print(("  %-44s %2d sites  %s"):format(g.key, #g.sites, g.sites[1].firstLine:sub(1, 60)))
	end
	print("")
	print("non-declarative ModMap.apply bodies:")
	for _, entry in ipairs(declarative) do
		if entry.nonDeclarative then
			print(("  %-24s %s"):format(entry.site.ownerLeaf, entry.nonDeclarative))
		end
	end
	for _, site in ipairs(allSites) do
		if site.category == "unclassified" then
			print(("  UNCLASSIFIED %s:%d"):format(site.file, site.line))
		end
	end
	os.exit(0)
end

--------------------------------------------------------------------------------
-- Emit
--------------------------------------------------------------------------------

local O, A = json.object, json.array

local function hookSignatureJson()
	local entries = {}
	for _, name in ipairs(sortedKeys(HOOKS)) do
		local h = HOOKS[name]
		entries[#entries + 1] = { name, O({
			{ "delegate", h.delegate },
			{ "signature", h.signature },
			{ "callSite", h.callSite },
			{ "sites", hookCounts[name] or 0 },
		}) }
	end
	return O(entries)
end

local behaviourJson = {}
for _, id in ipairs(groupOrder) do
	local group = groups[id]
	local siteEntries = {}
	for _, site in ipairs(group.sites) do
		siteEntries[#siteEntries + 1] = O({
			{ "file", site.file },
			{ "line", site.line },
			{ "endLine", site.endLine },
			{ "owner", site.owner },
		})
	end
	behaviourJson[#behaviourJson + 1] = O({
		{ "key", group.key },
		{ "hook", group.hook },
		{ "delegate", HOOKS[group.hook].delegate },
		{ "nameSource", group.derivedFrom },
		{ "bodyHash", group.bodyHash },
		{ "bodyLines", group.sites[1].endLine - group.sites[1].line + 1 },
		{ "siteCount", #group.sites },
		{ "sites", A(siteEntries) },
	})
end

local declarativeJson = {}
local shapeCounts = {}
for _, entry in ipairs(declarative) do
	local modEntries = {}
	for _, mod in ipairs(entry.mods) do
		shapeCounts[mod.valueShape] = (shapeCounts[mod.valueShape] or 0) + 1
		local extra = {}
		for _, e in ipairs(mod.extraArgs) do
			extra[#extra + 1] = (e:gsub("^%s+", ""):gsub("%s+$", ""))
		end
		modEntries[#modEntries + 1] = O({
			{ "target", mod.target },
			{ "name", mod.name },
			{ "type", mod.modType },
			{ "valueExpr", mod.valueExpr },
			{ "valueShape", mod.valueShape },
			{ "source", mod.source },
			{ "extraArgs", A(extra) },
		})
	end
	local fields = {
		{ "id", entry.site.ownerLeaf },
		{ "file", entry.site.file },
		{ "line", entry.site.line },
		{ "type", entry.affixType or "" },
		{ "declarative", entry.nonDeclarative == nil },
		{ "mods", A(modEntries) },
	}
	if entry.nonDeclarative then
		fields[#fields + 1] = { "notDeclarativeBecause", entry.nonDeclarative }
	end
	declarativeJson[#declarativeJson + 1] = O(fields)
end

local excludedGroups = {}
local excludedByCategory = {}
for _, site in ipairs(allSites) do
	if site.category ~= "data-field" then
		local bucket = excludedByCategory[site.category]
		if not bucket then
			bucket = {}
			excludedByCategory[site.category] = bucket
		end
		bucket[#bucket + 1] = site
	end
end
local CATEGORY_REASON = {
	["di-wrapper"] = "File-level dependency-injection wrapper: the chunk returns a function so "
		.. "the loader can inject mod/flag/skill helpers. Carries no behaviour; ticket 03 "
		.. "evaluates it during transcoding.",
	["chunk-wrapper"] = "Immediately-invoked chunk wrapper used to stay under the Lua 200-local "
		.. "and constant limits in a generated file. No behaviour.",
	["named-declaration"] = "An ordinary named function in a data file. Ported as ordinary C# "
		.. "code by the ticket that owns the file, not through the behaviour registry.",
	["call-argument"] = "An anonymous callback passed straight to a library call (gsub, "
		.. "table.sort). It never reaches a data table, so nothing can reference it by key.",
	["local-value"] = "A file-local helper closure. Ported as ordinary C# code.",
	["nested-helper"] = "Declared inside another function body, so it is part of that "
		.. "function and is ported along with it. It is not separately addressable by key.",
	["global-value"] = "A closure assigned to a global. Ported as ordinary C# code.",
}
for _, category in ipairs(sortedKeys(excludedByCategory)) do
	local sites = excludedByCategory[category]
	table.sort(sites, function(a, b)
		if a.file ~= b.file then
			return byteLess(a.file, b.file)
		end
		return a.line < b.line
	end)
	local siteEntries = {}
	for _, site in ipairs(sites) do
		siteEntries[#siteEntries + 1] = O({
			{ "file", site.file },
			{ "line", site.line },
			{ "binding", site.binding or "" },
		})
	end
	excludedGroups[#excludedGroups + 1] = O({
		{ "category", category },
		{ "count", #sites },
		{ "reason", CATEGORY_REASON[category] or "" },
		{ "sites", A(siteEntries) },
	})
end

-- Everything in a transcode-time file, not only the data-table fields: the whole
-- file runs once during transcoding, so none of it is runtime code.
transcodeSites = {}
for _, site in ipairs(allSites) do
	if site.disposition ~= "runtime" then
		transcodeSites[#transcodeSites + 1] = site
	end
end

local transcodeJson = {}
table.sort(transcodeSites, function(a, b)
	if a.file ~= b.file then
		return byteLess(a.file, b.file)
	end
	return a.line < b.line
end)
for _, site in ipairs(transcodeSites) do
	transcodeJson[#transcodeJson + 1] = O({
		{ "id", site.binding or site.context },
		{ "file", site.file },
		{ "line", site.line },
		{ "category", site.category },
		{ "owner", site.owner or "" },
	})
end

local dispositionJson = {}
for _, file in ipairs(sortedKeys(FILE_DISPOSITION)) do
	local entry = FILE_DISPOSITION[file]
	dispositionJson[#dispositionJson + 1] = O({
		{ "file", file },
		{ "disposition", entry.disposition },
		{ "reason", entry.reason },
	})
end

local document = O({
	{ "generator", "dotnet/tools/behaviour-keys/enumerate.lua" },
	{ "source", "src/Data" },
	{ "readMe", "Generated - do not hand-edit. Re-run the generator after a data update. "
		.. "Every entry under behaviours[] needs a matching line in "
		.. "Pob.Data/Behaviours/BehaviourRoster.cs; the MSBuild target in Pob.Data.csproj "
		.. "fails the build otherwise. Only behaviours[] entries carry a field named key, "
		.. "which is what that target greps for." },
	{ "summary", O({
		{ "luaFilesScanned", #files },
		{ "functionValues", #allSites },
		{ "behaviourSites", #behaviourSites },
		{ "behaviourKeys", #groupOrder },
		{ "declarativeEntries", #declarativeSites },
		{ "transcodeTimeFunctions", #transcodeSites },
	}) },
	{ "hooks", hookSignatureJson() },
	{ "behaviours", A(behaviourJson) },
	{ "declarative", O({
		{ "note", "ModMap.AffixData[*].apply lifted to data: a sequence of NewMod calls with "
			.. "no control flow. Ticket 03 emits these as rows and no code is generated for them. "
			.. "Three fields beyond the {name, type, value, source} shape the ticket sketched are "
			.. "load-bearing: target, because 18 of the mods go to the player mod list and not to "
			.. "the enemy; extraArgs, which carries mod tags such as ModFlag.Attack or a Condition "
			.. "tag table verbatim; and valueShape. valueShape is the value expression with indices "
			.. "and literals removed - see valueShapes below. Every value in the file is one of a "
			.. "handful of shapes (a flag, a literal, a table lookup times mapModEffect, or a roll "
			.. "interpolated between two values), so the config layer needs a small closed set of "
			.. "value kinds rather than a Lua expression evaluator." },
		{ "callSignature", HOOKS["apply"].signature },
		{ "valueShapes", (function()
			local entries = {}
			for _, shape in ipairs(sortedKeys(shapeCounts)) do
				entries[#entries + 1] = { shape, shapeCounts[shape] }
			end
			return O(entries)
		end)() },
		{ "entries", A(declarativeJson) },
	}) },
	{ "transcodeTime", O({
		{ "note", "Function values in files that ticket 03 executes once at transcode time. "
			.. "They must not be emitted as behaviour keys." },
		{ "files", A(dispositionJson) },
		{ "functions", A(transcodeJson) },
	}) },
	{ "excluded", A(excludedGroups) },
})

local text = json.encode(document)
local outPath = root .. "/dotnet/Pob.Data/behaviour-keys.json"

if mode == "check" then
	local existing = readFile(outPath)
	if existing ~= text then
		io.stderr:write("behaviour-keys.json is out of date; re-run enumerate.lua\n")
		os.exit(1)
	end
	print("behaviour-keys.json is up to date")
	os.exit(0)
end

local out = assert(io.open(outPath, "wb"))
out:write(text)
out:close()
print(("wrote %s: %d behaviour keys over %d sites, %d declarative entries")
	:format(outPath, #groupOrder, #behaviourSites, #declarativeSites))
