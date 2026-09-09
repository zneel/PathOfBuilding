--[[
	Differential-fuzz corpus generator (migration ticket 08).

	Generates randomised Path of Building builds from a seed, runs them through the Lua
	engine, and writes a deterministic JSON corpus of their outputs. When a C# calc engine
	exists (tickets 22-27) the same recipes get replayed against it and the two output sets
	get diffed; until then the corpus is a reproducible record of what the reference engine
	does with inputs nobody wrote by hand, and the failure notes it collects are findings
	in their own right.

	Usage
	-----
	Recipes only (no engine; runs under luajit AND lua5.1, from the repository root):

		lua5.1 dotnet/tools/fuzz/generate.lua --plan-only --seed 20260909 --count 200
		luajit dotnet/tools/fuzz/generate.lua --plan-only --seed 20260909 --count 200

	Full corpus (boots the engine; LuaJIT only, and MUST run from src/):

		cd src && luajit ../dotnet/tools/fuzz/generate.lua --seed 20260909 --count 50

	Options
	-------
		--seed N        RNG seed (default 20260909)
		--count N       number of builds (default 25)
		--out PATH      output file (default under dotnet/tools/fuzz/corpus/)
		--plan-only     emit recipes without running the engine
		--quiet         suppress the progress line

	Determinism
	-----------
	Same seed, same count, same checked-in data => byte-identical output. See rng.lua for
	why `math.random` is never used, json.lua for why keys are sorted and doubles are
	strings, and pools.lua for why the input pools are loaded off disk rather than out of
	the running engine.
]]

--------------------------------------------------------------------------------------
-- Paths
--------------------------------------------------------------------------------------

local scriptPath = arg and arg[0] or "dotnet/tools/fuzz/generate.lua"
local scriptDir = scriptPath:match("^(.*)[/\\][^/\\]*$") or "."
local repoRoot = scriptDir .. "/../../.."

local rng = dofile(scriptDir .. "/rng.lua")
local json = dofile(scriptDir .. "/json.lua")
local pools = dofile(scriptDir .. "/pools.lua")
local plan = dofile(scriptDir .. "/plan.lua")

--------------------------------------------------------------------------------------
-- Arguments
--------------------------------------------------------------------------------------

local options = {
	seed = 20260909,
	count = 25,
	out = nil,
	planOnly = false,
	quiet = false,
}

local i = 1
while arg and arg[i] do
	local a = arg[i]
	if a == "--plan-only" then
		options.planOnly = true
	elseif a == "--quiet" then
		options.quiet = true
	elseif a == "--seed" then
		i = i + 1
		options.seed = tonumber(arg[i]) or error("--seed needs a number")
	elseif a == "--count" then
		i = i + 1
		options.count = tonumber(arg[i]) or error("--count needs a number")
	elseif a == "--out" then
		i = i + 1
		options.out = arg[i] or error("--out needs a path")
	else
		error("unknown option: " .. tostring(a))
	end
	i = i + 1
end

if not options.out then
	options.out = repoRoot .. "/dotnet/tools/fuzz/corpus/"
		.. (options.planOnly and "fuzz-plan.json" or "fuzz-corpus.json")
end

local function log(fmt, ...)
	if not options.quiet then
		io.stderr:write(string.format(fmt, ...) .. "\n")
	end
end

--------------------------------------------------------------------------------------
-- Generate
--------------------------------------------------------------------------------------

log("fuzz: loading pools from %s", repoRoot)
local loaded = pools.load(repoRoot)
log("fuzz: pools nodes=%d gems=%d/%d bases=%d mods=%d config=%d tree=%s",
	loaded.counts.nodes, loaded.counts.activeGems, loaded.counts.supportGems,
	loaded.counts.bases, loaded.counts.mods, loaded.counts.configVars, loaded.treeVersion)

plan.init(loaded, rng)
local recipes = plan.generate(options.seed, options.count)
log("fuzz: generated %d recipes from seed %d", #recipes, options.seed)

--------------------------------------------------------------------------------------
-- Serialise a recipe
--------------------------------------------------------------------------------------

local function encodeRecipe(recipe)
	local nodes = json.array({})
	for k, id in ipairs(recipe.nodes) do
		nodes[k] = id
	end

	local items = json.array({})
	for k, item in ipairs(recipe.items) do
		items[k] = { group = item.group, base = item.base, flavour = item.flavour, raw = item.raw }
	end

	local groups = json.array({})
	for k, text in ipairs(recipe.socketGroups) do
		groups[k] = text
	end

	local config = json.array({})
	for k, entry in ipairs(recipe.config) do
		config[k] = { var = entry.var, kind = entry.kind, value = entry.value }
	end

	return {
		index = recipe.index,
		seed = recipe.seed,
		classId = recipe.classId,
		className = recipe.className,
		ascendClassId = recipe.ascendClassId,
		level = recipe.level,
		nodes = nodes,
		items = items,
		socketGroups = groups,
		config = config,
		customMods = recipe.customMods,
	}
end

--------------------------------------------------------------------------------------
-- Run
--------------------------------------------------------------------------------------

local builds = json.array({})
local summary = {
	ok = 0,
	partial = 0,
	error = 0,
	notes = 0,
}

if options.planOnly then
	for k, recipe in ipairs(recipes) do
		builds[k] = { recipe = encodeRecipe(recipe) }
	end
else
	local apply = dofile(scriptDir .. "/apply.lua")
	apply.boot(repoRoot)
	log("fuzz: engine booted")

	for k, recipe in ipairs(recipes) do
		local result = apply.run(recipe)
		summary[result.status] = (summary[result.status] or 0) + 1
		summary.notes = summary.notes + #result.notes

		local notes = json.array({})
		for n, note in ipairs(result.notes) do
			notes[n] = note
		end

		local entry = {
			recipe = encodeRecipe(recipe),
			status = result.status,
			notes = notes,
			stats = result.stats,
		}
		if result.outputs then
			entry.outputs = result.outputs
			local nonScalar = json.array({})
			for n, key in ipairs(result.nonScalarOutputs) do
				nonScalar[n] = key
			end
			entry.nonScalarOutputs = nonScalar
		end
		builds[k] = entry

		log("fuzz: [%d/%d] %s  nodes=%d/%d items=%d/%d outputs=%s notes=%d",
			k, #recipes, result.status,
			result.stats.nodesAllocated, result.stats.nodesRequested,
			result.stats.itemsAdded, result.stats.itemsRequested,
			tostring(result.stats.outputKeys or 0), #result.notes)
	end
end

--------------------------------------------------------------------------------------
-- Emit
--------------------------------------------------------------------------------------

local document = {
	format = 1,
	kind = options.planOnly and "plan" or "corpus",
	seed = options.seed,
	count = options.count,
	treeVersion = loaded.treeVersion,
	generator = "dotnet/tools/fuzz/generate.lua",
	poolCounts = loaded.counts,
	builds = builds,
}
if not options.planOnly then
	document.summary = {
		ok = summary.ok,
		partial = summary.partial,
		error = summary["error"],
		notes = summary.notes,
	}
end

local text = json.encode(document) .. "\n"

local file, err = io.open(options.out, "wb")
if not file then
	error("fuzz: cannot write " .. options.out .. ": " .. tostring(err))
end
file:write(text)
file:close()

log("fuzz: wrote %s (%d bytes)", options.out, #text)
if not options.planOnly then
	log("fuzz: ok=%d partial=%d error=%d notes=%d",
		summary.ok, summary.partial, summary["error"], summary.notes)
end
