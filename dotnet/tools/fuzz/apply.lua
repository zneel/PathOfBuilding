--[[
	Engine bridge for the differential fuzzer (migration ticket 08).

	Applies a recipe from `plan.lua` to the live Lua engine through src/HeadlessWrapper.lua
	and reads the resulting outputs back. This is the ONLY file in the tool that touches the
	engine; everything else is pure Lua over checked-in data, which is what lets the recipe
	generator run under an interpreter the engine itself cannot boot on.

	LuaJIT only. Stock Lua 5.1 cannot boot the engine here:
	  * src/Launch.lua:18 calls `jit.opt.start`, and
	  * src/Modules/Common.lua:19 indexes the `bit` library, which LuaJIT provides built in
	    and which stock 5.1 only has via the LuaBitOp rock (not installed).
	See README.md for what that means for cross-interpreter reproduction.

	Every engine call is wrapped. A recipe that makes the engine throw is a FINDING, not a
	crash: it is recorded against the build and the run continues.
]]

local apply = {}

local booted = false

--- Boot the engine. Must be called with the process working directory set to `src/`,
--- because src/HeadlessWrapper.lua and src/Launch.lua `dofile` their siblings by relative
--- path.
function apply.boot(repoRoot)
	if booted then
		return
	end
	if not io.open("HeadlessWrapper.lua", "r") then
		error("fuzz/apply: run this from the src/ directory -- HeadlessWrapper.lua dofiles its siblings relatively")
	end
	package.path = table.concat({
		repoRoot .. "/runtime/lua/?.lua",
		repoRoot .. "/runtime/lua/?/init.lua",
		"./?.lua",
		package.path,
	}, ";")
	package.cpath = "/usr/local/lib/lua/5.1/?.so;" .. package.cpath
	dofile("HeadlessWrapper.lua")
	if type(_G.build) ~= "table" then
		error("fuzz/apply: HeadlessWrapper did not expose `build`")
	end
	booted = true
end

--- Strip PoB's inline colour escapes (`^1`, `^xRRGGBB`) so a captured message is plain
--- text in the corpus.
local function stripColours(text)
	text = tostring(text)
	text = text:gsub("%^%x%x%x%x%x%x", "")
	text = text:gsub("%^%d", "")
	return (text:gsub("%s+", " "):gsub("^%s+", ""):gsub("%s+$", ""))
end

--- Read and clear the engine's error prompt.
---
--- This is the part that makes the fuzzer actually see failures. `launch:OnFrame`
--- (src/Launch.lua:112) runs the whole calculation inside `PCall` and, on error, does NOT
--- rethrow: it funnels the message into `launch:ShowErrMsg` -> `self.promptMsg` and
--- carries on. A fuzzer that only wrapped its own calls in `pcall` would therefore report
--- a clean run over a build that blew up, and would then read a STALE `mainOutput` as if
--- it were the answer. Worse, that same handler calls `main:SetMode("LIST")` when a build
--- crashes on its first calculation, which detaches the build tab entirely.
---
--- So: after every callback, take the prompt. A message here is a finding.
local function takePrompt()
	local launch = _G.__mainObject__
	if not launch then
		return nil
	end
	local message = launch.promptMsg
	if not message then
		return nil
	end
	launch.promptMsg = nil
	launch.promptFunc = nil
	return stripColours(message)
end

--- Run one engine step, collecting the error instead of propagating it. Both failure
--- channels are checked: a raw Lua error out of the call, and an error the engine
--- swallowed into its prompt.
local function step(notes, label, fn)
	takePrompt()
	local ok, err = pcall(fn)
	if not ok then
		notes[#notes + 1] = label .. ": " .. tostring(err)
	end
	local prompt = takePrompt()
	if prompt then
		notes[#notes + 1] = label .. " [engine prompt]: " .. prompt
		return false
	end
	return ok
end

--- Scalar outputs only, with keys sorted.
---
--- `mainOutput` also carries tables (`SkillDPS`, `ReqIntItem`) and occasionally functions.
--- Those are structured breakdowns, not comparable numbers, and flattening them would bake
--- this tool's flattening convention into the corpus that the C# engine later has to
--- match. They are counted and named so nothing is silently dropped.
local function collectOutputs(output)
	local scalars = {}
	local skipped = {}
	for key, value in pairs(output or {}) do
		local t = type(value)
		if t == "number" or t == "boolean" or t == "string" then
			scalars[key] = value
		else
			skipped[#skipped + 1] = key
		end
	end
	table.sort(skipped)
	return scalars, skipped
end

--- Apply one recipe and read back its outputs.
---
--- Returns a result table: `status` ("ok" | "error"), `notes` (per-step failures, in
--- application order), `stats` (how much of the recipe the engine actually accepted), and
--- `outputs` (scalar mainOutput keys) when the calculation completed.
function apply.run(recipe)
	local notes = {}
	local stats = {
		nodesRequested = #recipe.nodes,
		nodesAllocated = 0,
		nodesMissing = 0,
		nodesUnreachable = 0,
		itemsRequested = #recipe.items,
		itemsAdded = 0,
		socketGroups = #recipe.socketGroups,
		configApplied = 0,
	}

	local fatal = not step(notes, "newBuild", function()
		newBuild()
	end)

	if not fatal then
		step(notes, "SelectClass", function()
			build.spec:SelectClass(recipe.classId)
			if recipe.ascendClassId > 0 then
				build.spec:SelectAscendClass(recipe.ascendClassId)
			end
		end)

		for _, id in ipairs(recipe.nodes) do
			local node = build.spec.nodes[id]
			if not node then
				-- The tree file lists nodes the engine does not expose (jewel expansion
				-- proxies, filtered-out alternates). Counted, not an error.
				stats.nodesMissing = stats.nodesMissing + 1
			elseif node.alloc then
				-- Already pulled in as part of an earlier node's path.
				stats.nodesAllocated = stats.nodesAllocated + 1
			elseif not node.path then
				stats.nodesUnreachable = stats.nodesUnreachable + 1
			else
				local ok = step(notes, "AllocNode " .. tostring(id), function()
					build.spec:AllocNode(node)
				end)
				if ok then
					stats.nodesAllocated = stats.nodesAllocated + 1
				end
			end
		end

		step(notes, "AddUndoState", function()
			build.spec:AddUndoState()
			build.buildFlag = true
		end)

		step(notes, "characterLevel", function()
			build.characterLevelAutoMode = false
			build.characterLevel = recipe.level
		end)

		for i, group in ipairs(recipe.socketGroups) do
			step(notes, "PasteSocketGroup " .. i, function()
				build.skillsTab:PasteSocketGroup(group)
			end)
		end

		for i, item in ipairs(recipe.items) do
			local ok = step(notes, "item " .. i .. " (" .. item.base .. ")", function()
				build.itemsTab:CreateDisplayItemFromRaw(item.raw)
				build.itemsTab:AddDisplayItem()
			end)
			if ok then
				stats.itemsAdded = stats.itemsAdded + 1
			end
		end

		for _, entry in ipairs(recipe.config) do
			local ok = step(notes, "config " .. entry.var, function()
				build.configTab.input[entry.var] = entry.value
			end)
			if ok then
				stats.configApplied = stats.configApplied + 1
			end
		end

		step(notes, "customMods", function()
			build.configTab.input.customMods = recipe.customMods
			build.configTab:BuildModList()
		end)
	end

	local calculated = false
	if not fatal then
		calculated = step(notes, "OnFrame", function()
			build.buildFlag = true
			runCallback("OnFrame")
		end)
		-- src/Launch.lua:115 sends a build that crashes on its first calculation back to the
		-- build list. `main:SetMode` is deferred (src/Modules/Main.lua:507 only sets
		-- `newMode`), so check the pending switch as well as the current mode -- otherwise
		-- the departure is invisible until a frame that this tool never runs. Reading
		-- mainOutput after either would report the previous build's numbers.
		local mainObj = __mainObject__.main
		if calculated and (mainObj.mode ~= "BUILD" or (mainObj.newMode and mainObj.newMode ~= "BUILD")) then
			notes[#notes + 1] = string.format("OnFrame: engine left BUILD mode (mode=%s, newMode=%s)",
				tostring(mainObj.mode), tostring(mainObj.newMode))
			calculated = false
		end
	end

	if not calculated then
		return {
			status = "error",
			notes = notes,
			stats = stats,
		}
	end

	local scalars, skipped = collectOutputs(build.calcsTab.mainOutput)
	local count = 0
	for _ in pairs(scalars) do
		count = count + 1
	end
	stats.outputKeys = count
	stats.nonScalarOutputKeys = #skipped

	return {
		status = (#notes == 0) and "ok" or "partial",
		notes = notes,
		stats = stats,
		outputs = scalars,
		nonScalarOutputs = skipped,
	}
end

return apply
