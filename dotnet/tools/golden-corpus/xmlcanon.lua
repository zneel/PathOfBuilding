-- A deterministic replacement for `common.xml.ComposeXML` (migration ticket 07).
--
-- Why this exists: PoB's own composer writes attributes in `pairs(node.attrib)` order, and
-- several savers build their child lists by iterating a hash table
-- (`ConfigTab:Save` over inputs and placeholders, `ItemsTab:Save` over slots,
-- `PassiveSpec:Save` over `allocNodes`). LuaJIT does not promise a stable iteration order
-- across processes, and in practice it is not stable, so `build:SaveDB()` produces a
-- different byte sequence - and therefore a different corpus file - on every run for the
-- same build. The numbers are identical; the file is not.
--
-- A corpus that changes bytes on every regeneration cannot be reviewed in a diff and cannot
-- be checked for drift by hash, so the generator composes the XML itself:
--
--  * attributes are written in sorted order;
--  * the `<Spec nodes="...">` list is sorted numerically;
--  * `<URL>` under `<Spec>` is dropped - it is a re-encoding of the same node set whose byte
--    order follows the same unordered iteration, and `PassiveSpec:Load` prefers `nodes`;
--  * children are sorted only for element names whose order carries no meaning. Skill
--    groups, gems, items and spec order are all positional (`mainSocketGroup` is an index
--    into the skill list) and are left exactly as the savers produced them.
--
-- None of this changes what the file means: the result loads through the ordinary
-- `Build:LoadDB` path, which is how the generator produces the outputs it records.

local m = {}

local t_insert = table.insert
local t_concat = table.concat

-- Elements whose siblings are a set, not a sequence.
local UNORDERED = {
	Input = true,
	Placeholder = true,
	Slot = true,
	SocketIdURL = true,
	Socket = true,
	Spectre = true,
	Override = true,
	TradeSearchWeights = true,
}

-- Elements that are a cache of the build's own outputs rather than an input to it.
-- `Build:Load` never reads them back; keeping them would put the answer inside the question
-- and roughly double the corpus.
local DERIVED = {
	PlayerStat = true,
	MinionStat = true,
	FullDPSSkill = true,
	URL = true,
	-- `<Section collapsed=.. id=..>` under `<Calcs>` is which panels the Calcs tab had
	-- folded open. Fifty-one of them per build, none of which the engine reads.
	Section = true,
}

--- True for a child that carries no information: an item slot with nothing in it, which
--- `ItemsTab:NewItemSet` already initialises to exactly this state.
local function isEmptyChild(node)
	if node.elem ~= "Slot" then
		return false
	end
	local attrib = node.attrib or {}
	return attrib.itemId == "0" and attrib.active ~= "true"
		and (attrib.itemPbURL == nil or attrib.itemPbURL == "")
		and #node == 0
end

local ENTITIES = { ["<"] = "&lt;", [">"] = "&gt;", ["&"] = "&amp;", ["'"] = "&apos;", ['"'] = "&quot;" }

local function encodeContent(text)
	return (text:gsub("[<>&'\"]", ENTITIES))
end

local function sortedAttribNames(attrib)
	local names = {}
	for key, value in pairs(attrib) do
		if value and type(key) == "string" then
			names[#names + 1] = key
		end
	end
	table.sort(names)
	return names
end

--- Sorts a comma-separated numeric id list. Used for `<Spec nodes>`, which `PassiveSpec:Save`
--- builds by walking `allocNodes` as a hash table.
local function sortIdList(value)
	local ids = {}
	for id in value:gmatch("[^,]+") do
		ids[#ids + 1] = tonumber(id) or id
	end
	table.sort(ids, function(a, b)
		if type(a) ~= type(b) then
			return type(a) == "number"
		end
		return a < b
	end)
	local out = {}
	for i, id in ipairs(ids) do
		out[i] = tostring(id)
	end
	return t_concat(out, ",")
end

local composeNode

--- Serialises one node into `frag`. Returns nothing; errors are raised, not returned, since
--- the generator has no way to continue from a malformed tree.
composeNode = function(frag, node, level)
	local indent = string.rep("\t", level)
	t_insert(frag, indent)
	t_insert(frag, "<")
	t_insert(frag, node.elem)
	if node.attrib then
		for _, key in ipairs(sortedAttribNames(node.attrib)) do
			local value = node.attrib[key]
			assert(type(value) == "string",
				"attribute '" .. key .. "' of <" .. node.elem .. "> is not a string")
			if node.elem == "Spec" and (key == "nodes" or key == "extendedNodes") then
				value = sortIdList(value)
			end
			t_insert(frag, " ")
			t_insert(frag, key)
			t_insert(frag, '="')
			t_insert(frag, encodeContent(value))
			t_insert(frag, '"')
		end
	end

	-- Children, with the derived ones removed and the unordered ones sorted in place.
	local children = {}
	for _, child in ipairs(node) do
		if type(child) ~= "table" or (not DERIVED[child.elem] and not isEmptyChild(child)) then
			children[#children + 1] = child
		end
	end

	local unorderedIndices = {}
	for i, child in ipairs(children) do
		if type(child) == "table" and UNORDERED[child.elem] then
			unorderedIndices[#unorderedIndices + 1] = i
		end
	end
	if #unorderedIndices > 1 then
		local picked = {}
		for _, i in ipairs(unorderedIndices) do
			local child = children[i]
			local key = {}
			composeNode(key, child, 0)
			picked[#picked + 1] = { node = child, key = t_concat(key) }
		end
		table.sort(picked, function(a, b) return a.key < b.key end)
		for n, i in ipairs(unorderedIndices) do
			children[i] = picked[n].node
		end
	end

	if #children == 0 then
		t_insert(frag, "/>\n")
		return
	end

	t_insert(frag, ">\n")
	for _, child in ipairs(children) do
		if type(child) == "table" then
			composeNode(frag, child, level + 1)
		else
			t_insert(frag, string.rep("\t", level + 1))
			t_insert(frag, encodeContent(child))
			t_insert(frag, "\n")
		end
	end
	t_insert(frag, indent)
	t_insert(frag, "</")
	t_insert(frag, node.elem)
	t_insert(frag, ">\n")
end

--- Composes an XML node tree into canonical text.
function m.compose(rootNode)
	local frag = { '<?xml version="1.0" encoding="UTF-8"?>\n' }
	composeNode(frag, rootNode, 0)
	return t_concat(frag)
end

--- Serialises a build the way `Build:SaveDB` does, but deterministically.
-- The savers are the same objects PoB uses, so the file stays a normal PoB build file; only
-- the byte order of things that have no order is pinned down.
function m.saveBuild(b)
	local root = { elem = "PathOfBuilding" }

	local buildNode = { elem = "Build" }
	b:Save(buildNode)
	t_insert(root, buildNode)

	local elems = {}
	for elem in pairs(b.savers) do
		elems[#elems + 1] = elem
	end
	table.sort(elems)
	for _, elem in ipairs(elems) do
		local node = { elem = elem }
		b.savers[elem]:Save(node)
		t_insert(root, node)
	end

	return m.compose(root)
end

return m
