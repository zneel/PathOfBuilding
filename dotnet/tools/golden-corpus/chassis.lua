-- Turns a declarative build spec into a real, calculated Path of Building build.
--
-- Deliberately not a copy of `spec/GenerateBuilds.lua`: that script only reads `.xml` files
-- off disk, so the corpus it can produce is exactly as large as the set of builds someone
-- checked in by hand (five, all from 3.13). This module constructs builds from the data the
-- engine already has - the passive tree, the gem list, the item bases - so the corpus size
-- is a function of the matrix in `matrix.lua` rather than of anyone's patience.
--
-- Every build is built through the live object API (class selection, tree allocation, item
-- equipping, socket groups, config) and then serialised with `build:SaveDB()`. The saved XML
-- is what lands in the golden file, and it is reloaded from that XML before the outputs are
-- captured, so the recorded input provably produces the recorded output through the normal
-- load path - which is the path the C# port will have to reproduce.

local m = {}

local s_format = string.format

-- ---------------------------------------------------------------------------------------
-- Error detection
-- ---------------------------------------------------------------------------------------

-- `launch:OnFrame` (src/Launch.lua:112) runs the entire calculation inside `PCall` and does
-- NOT rethrow: on error it stashes the message in `launch.promptMsg`, draws a popup, and
-- carries on. In a GUI that is correct - the user gets a dialog instead of a dead window. In
-- a generator it is a silent-corruption trap: `runCallback("OnFrame")` returns normally,
-- `build.calcsTab.mainOutput` still holds *the previous build's* numbers, and those get
-- written out as this build's answer key. No exception, no warning, plausible values,
-- permanently wrong oracle.
--
-- Worse, `Launch.lua:115` ejects a build that crashes on its first calculation back to the
-- build list with a deferred `main:SetMode("LIST")`, so the very next frame is no longer
-- calculating a build at all.
--
-- So: clear the prompt before every build, and after every frame treat a non-nil
-- `promptMsg`, a pending mode switch, or a mode that is no longer BUILD as a failed build.

--- Clears any error state left behind by a previous build, so a failure cannot leak forward
--- and be attributed to the next one.
function m.resetErrorState()
	launch.promptMsg = nil
	launch.promptCol = nil
	launch.promptFunc = nil
	main.newMode = nil
	main.newModeArgs = nil
end

--- Runs one frame and raises whatever `launch:OnFrame` swallowed.
-- `context` names what was being done, so a failure in the manifest says which step broke.
function m.frame(context)
	runCallback("OnFrame")
	m.assertNoEngineError(context)
end

--- Raises if the engine reported an error during the frames run since the last reset.
-- Split out from `frame` because `loadBuildFromXML` runs its own frame internally.
function m.assertNoEngineError(context)
	local promptMsg = launch.promptMsg
	if promptMsg then
		m.resetErrorState()
		-- The prompt text carries the whole dismissal blurb; keep only the first lines.
		local firstLine = tostring(promptMsg):gsub("\n.*", "")
		error(context .. ": engine error swallowed by launch:OnFrame: " .. firstLine, 0)
	end
	if main.newMode and main.newMode ~= "BUILD" then
		local ejected = tostring(main.newMode)
		m.resetErrorState()
		error(context .. ": build was ejected to mode " .. ejected .. " (crashed on first calculation)", 0)
	end
	if main.mode ~= "BUILD" then
		local mode = tostring(main.mode)
		m.resetErrorState()
		error(context .. ": application is in mode " .. mode .. ", not BUILD", 0)
	end
end

-- ---------------------------------------------------------------------------------------
-- Item bases
-- ---------------------------------------------------------------------------------------

local baseCache = {}

--- Picks one canonical item base of a given type, deterministically.
-- Highest level requirement wins (the endgame base, which is what a real build wears), ties
-- broken by name so the choice never depends on `pairs` order. Influenced and league-locked
-- bases are skipped: their implicits would silently become part of every build's numbers.
function m.pickBase(typeName, subTypePattern)
	local key = typeName .. "|" .. (subTypePattern or "")
	if baseCache[key] then
		return baseCache[key]
	end
	local best, bestLevel
	for name, base in pairs(data.itemBases) do
		if base.type == typeName
			and not base.influence
			and not base.hidden
			and not name:match("Talisman")
			and (not subTypePattern or (base.subType and base.subType:match(subTypePattern)))
		then
			local level = (base.req and base.req.level) or 0
			if not bestLevel or level > bestLevel or (level == bestLevel and name < best) then
				best, bestLevel = name, level
			end
		end
	end
	assert(best, "no item base of type " .. typeName .. " " .. tostring(subTypePattern))
	baseCache[key] = best
	return best
end

-- ---------------------------------------------------------------------------------------
-- Item construction
-- ---------------------------------------------------------------------------------------

local itemSerial = 0

--- Deterministic stand-in for the random 64-hex id PoB assigns to new items.
-- Randomness here would make the saved XML - and therefore the whole corpus - differ
-- between runs, which is the one property the corpus is not allowed to lose.
local function nextUniqueId()
	itemSerial = itemSerial + 1
	return string.rep("0", 56) .. s_format("%08x", itemSerial)
end

--- Resets the item id counter. Called per build so ids depend only on the build's own
--- item list, not on how many builds ran before it.
function m.resetItemIds()
	itemSerial = 0
end

--- Builds the raw item text for a rare item and returns it plus the parsed Item.
-- `unparsed` collects any mod line the engine failed to understand: a mod that silently
-- does nothing would make the golden numbers quietly wrong, so it is reported, not ignored.
function m.makeRare(baseName, title, mods, quality, unparsed)
	local lines = {
		"Rarity: RARE",
		title,
		baseName,
		"Unique ID: " .. nextUniqueId(),
		"Item Level: 86",
		"Quality: " .. tostring(quality or 20),
		"LevelReq: 68",
		"Implicits: 0",
	}
	for _, mod in ipairs(mods) do
		lines[#lines + 1] = mod
	end
	local raw = table.concat(lines, "\n") .. "\n"
	local item = new("Item"):Item(raw)
	if not item.base then
		error("unrecognised item base '" .. baseName .. "'")
	end
	if unparsed then
		for _, modLine in ipairs(item.explicitModLines or {}) do
			if modLine.extra then
				unparsed[#unparsed + 1] = baseName .. ": " .. modLine.line
			end
		end
	end
	return item
end

--- Equips an item into its primary slot of item set 1.
function m.equip(b, item)
	b.itemsTab:AddItem(item, true)
	b.itemsTab:EquipItemInSet(item, 1)
end

--- Equips an item into a named slot.
-- `EquipItemInSet` only reaches the second weapon slot when SHIFT is held, and headless
-- `IsKeyDown` is always false - so dual wielding needs this rather than a second call to
-- `equip`, which would put the off-hand weapon straight back into "Weapon 1".
function m.equipInSlot(b, item, slotName)
	b.itemsTab:AddItem(item, true)
	local slot = b.itemsTab.slots[slotName]
	assert(slot, "no such item slot: " .. slotName)
	slot:SetSelItemId(item.id)
	b.itemsTab:PopulateSlots()
	b.buildFlag = true
end

-- ---------------------------------------------------------------------------------------
-- Passive tree
-- ---------------------------------------------------------------------------------------

-- Greedy tree allocation is expensive (every AllocNode rebuilds the whole dependency
-- graph), and the matrix reuses the same chassis dozens of times, so the resulting node
-- list is cached per shape and replayed with ImportFromNodeList, which rebuilds once.
local treeCache = {}

local function cheapestUnallocated(spec, predicate)
	local best, bestKey
	for id, node in pairs(spec.nodes) do
		if not node.alloc and node.path and predicate(node) then
			-- (path length, id) - a total order, so the result never depends on
			-- iteration order.
			local key = #node.path * 1000000 + id
			if not bestKey or key < bestKey then
				best, bestKey = node, key
			end
		end
	end
	return best
end

local function isPlainNotable(node)
	return node.type == "Notable"
		and not node.ascendancyName
		and not node.isBlighted
		and not node.isProxy
		and not node.expansionJewel
		and not node.conquered
end

local function isAscendancyNotable(node, ascendancyName)
	return node.type == "Notable" and node.ascendancyName == ascendancyName and not node.isMultipleChoiceOption
end

--- Computes (and caches) the allocated node list for a tree shape.
-- `shape` is `{ classId, ascendId, keystones = { "name", ... }, budget = n, ascendPoints = n }`.
function m.treeNodes(b, shape)
	local key = s_format("%d|%d|%d|%d|%s", shape.classId, shape.ascendId, shape.budget or 0,
		shape.ascendPoints or 0, table.concat(shape.keystones or {}, ","))
	if treeCache[key] then
		return treeCache[key]
	end

	local spec = b.spec
	spec:ResetNodes()
	spec:SelectClass(shape.classId)
	spec:SelectAscendClass(shape.ascendId)

	-- Named keystones first: they are the point of the build, and pathing to them also
	-- picks up whatever is on the way.
	for _, keystoneName in ipairs(shape.keystones or {}) do
		local target
		for id, node in pairs(spec.nodes) do
			if node.type == "Keystone" and node.dn == keystoneName and not node.ascendancyName then
				if not target or id < target.id then
					target = node
				end
			end
		end
		if target and not target.alloc then
			spec:AllocNode(target)
		end
	end

	local ascendancyName = spec.curAscendClass and spec.curAscendClass.name
	for _ = 1, (shape.ascendPoints or 8) do
		local node = cheapestUnallocated(spec, function(n)
			return isAscendancyNotable(n, ascendancyName)
		end)
		if not node then
			break
		end
		spec:AllocNode(node)
	end

	local budget = shape.budget or 90
	local guard = 0
	while select(1, spec:CountAllocNodes()) < budget and guard < 200 do
		guard = guard + 1
		local node = cheapestUnallocated(spec, isPlainNotable)
		if not node then
			break
		end
		spec:AllocNode(node)
	end

	local nodes = {}
	for id in pairs(spec.allocNodes) do
		nodes[#nodes + 1] = id
	end
	table.sort(nodes)
	treeCache[key] = nodes
	return nodes
end

--- Applies a cached node list to the build's spec.
function m.applyTree(b, shape, nodes)
	b.spec:ImportFromNodeList(nil, shape.classId, shape.ascendId, 0, nodes, {}, {}, nil)
	b.spec:AddUndoState()
	b.buildFlag = true
end

-- ---------------------------------------------------------------------------------------
-- Skills
-- ---------------------------------------------------------------------------------------

--- Resolves a gem by name and appends it to a socket group's gem list.
function m.addGem(b, gemList, nameSpec, level, quality, extra)
	local err, gemData = b.skillsTab:FindSkillGem(nameSpec)
	if err then
		return err
	end
	local gem = {
		nameSpec = gemData.name,
		gemId = gemData.id,
		skillId = gemData.grantedEffectId,
		level = level or math.min(20, gemData.naturalMaxLevel or 20),
		quality = quality or 20,
		enabled = true,
		enableGlobal1 = true,
		enableGlobal2 = false,
		count = 1,
	}
	for k, v in pairs(extra or {}) do
		gem[k] = v
	end
	gemList[#gemList + 1] = gem
	return nil
end

--- Adds a socket group. `group` is `{ gems = { {name=,level=,quality=,...}, ... }, slot=, includeInFullDPS= }`.
function m.addSocketGroup(b, group)
	local gemList = {}
	local errors = {}
	for _, gemSpec in ipairs(group.gems) do
		local err = m.addGem(b, gemList, gemSpec.name or gemSpec, gemSpec.level, gemSpec.quality, gemSpec.extra)
		if err then
			errors[#errors + 1] = err
		end
	end
	if #gemList == 0 then
		return nil, errors
	end
	local socketGroup = {
		enabled = true,
		label = group.label or "",
		slot = group.slot,
		gemList = gemList,
		mainActiveSkill = group.mainActiveSkill or 1,
		mainActiveSkillCalcs = group.mainActiveSkillCalcs or group.mainActiveSkill or 1,
		includeInFullDPS = group.includeInFullDPS ~= false,
	}
	b.skillsTab:ProcessSocketGroup(socketGroup)
	b.skillsTab.socketGroupList[#b.skillsTab.socketGroupList + 1] = socketGroup
	return #b.skillsTab.socketGroupList, errors
end

return m
