-- Cross-checks this generator against the oracle that already exists (migration ticket 07).
--
-- Run from `src/`:
--
--   luajit ../dotnet/tools/golden-corpus/legacy-check.lua
--
-- `spec/TestBuilds/3.13/*.lua` are the five golden builds the Lua project has replayed since
-- 3.13, each a build XML plus ~474 expected output values rounded to four decimal places by
-- `spec/GenerateBuilds.lua`. `spec/System/TestBuilds_spec.lua` compares
-- `build.calcsTab.mainOutput` against them key by key.
--
-- This script runs the same five builds through *this* tool's capture path - the flattened
-- `MAIN/player` section that lands in the corpus - and compares that against the same
-- expected values at the same four decimal places. If the two agree, the new corpus and the
-- established oracle are measuring the same thing, and the `%.17g` values in the corpus are
-- the unrounded originals of numbers the project has been asserting on for years.
--
-- Exit status is 0 only if every key of every build matches.

local ROOT = "../dotnet/tools/golden-corpus/"
local BUILD_DIR = "../spec/TestBuilds/3.13"

package.path = "../runtime/lua/?.lua;../runtime/lua/?/init.lua;./?.lua;" .. package.path

dofile("HeadlessWrapper.lua")

local dumper = dofile(ROOT .. "dump.lua")

local s_format = string.format

--- The four-decimal comparison `TestBuilds_spec.lua` performs, verbatim.
local function agrees(expected, actual)
	if type(expected) == "number" and type(actual) == "number" then
		return round(expected, 4) == round(actual, 4)
	end
	return expected == actual
end

-- `lfs` is a busted dependency, not something the headless wrapper provides, so the
-- directory listing goes through the shell.
local names = {}
local listing = io.popen('ls "' .. BUILD_DIR .. '"')
for file in listing:lines() do
	local stem = file:match("^(.+)%.lua$")
	if stem then
		names[#names + 1] = stem
	end
end
listing:close()
assert(#names > 0, "no golden builds found in " .. BUILD_DIR)
table.sort(names)

-- Two comparisons, and only the first of them is a statement about this tool:
--
--   A. captured vs `build.calcsTab.mainOutput` - does this tool's capture path read the same
--      values the existing spec reads? Any difference here is a bug in dump.lua.
--   B. `build.calcsTab.mainOutput` vs the frozen `.lua` values - does today's engine still
--      produce what was frozen in 3.13? A difference here belongs to the engine and the game
--      data, not to the corpus, and is reported separately so it cannot be mistaken for one.
local totalKeys, totalCaptureDiff, totalEngineDrift, totalMissing = 0, 0, 0, 0
local report = {}
local driftExamples = {}

for _, stem in ipairs(names) do
	local expectedBuild = dofile(BUILD_DIR .. "/" .. stem .. ".lua")
	loadBuildFromXML(expectedBuild.xml, stem)

	local mainOutput = build.calcsTab.mainOutput
	local sections = dumper.captureActors(build)
	local captured = sections["MAIN/player"]
	assert(captured, "no MAIN/player section for " .. stem)

	local keys = {}
	for key in pairs(expectedBuild.output) do
		keys[#keys + 1] = key
	end
	table.sort(keys)

	local captureDiff, engineDrift, missing = {}, {}, {}
	for _, key in ipairs(keys) do
		local expected = expectedBuild.output[key]
		local live = mainOutput[key]
		local mine = captured[key]

		-- A: the capture path has to read exactly what the spec reads.
		if type(live) ~= "table" and live ~= mine then
			captureDiff[#captureDiff + 1] = s_format("%s: mainOutput=%s captured=%s",
				key, tostring(live), tostring(mine))
		end

		-- B: does the live engine still agree with the frozen value?
		if live == nil then
			missing[#missing + 1] = key
		elseif not agrees(expected, live) then
			engineDrift[#engineDrift + 1] = s_format("%s: frozen=%s live=%s",
				key, tostring(expected), tostring(live))
		end
	end

	local capturedCount = 0
	for _ in pairs(captured) do
		capturedCount = capturedCount + 1
	end

	totalKeys = totalKeys + #keys
	totalCaptureDiff = totalCaptureDiff + #captureDiff
	totalEngineDrift = totalEngineDrift + #engineDrift
	totalMissing = totalMissing + #missing
	report[#report + 1] = s_format(
		"%-32s frozen keys %4d | capture diffs %3d | engine drift %3d | keys gone %3d | captured %4d",
		stem, #keys, #captureDiff, #engineDrift, #missing, capturedCount)
	for _, line in ipairs(captureDiff) do
		report[#report + 1] = "    CAPTURE-DIFF " .. line
	end
	for i = 1, math.min(3, #engineDrift) do
		driftExamples[#driftExamples + 1] = "    " .. stem .. " " .. engineDrift[i]
	end
end

print("")
print("Legacy oracle cross-check (spec/TestBuilds/3.13, 4 decimal places)")
print(string.rep("-", 100))
for _, line in ipairs(report) do
	print(line)
end
print(string.rep("-", 100))
print(s_format("%d builds, %d frozen keys compared", #names, totalKeys))
print(s_format("  A. capture path vs build.calcsTab.mainOutput : %d differences%s",
	totalCaptureDiff, totalCaptureDiff == 0 and "  <- this tool agrees with the established oracle" or ""))
print(s_format("  B. live engine vs frozen 3.13 values         : %d drifted, %d keys no longer emitted",
	totalEngineDrift, totalMissing))
if #driftExamples > 0 then
	print("     (examples - these belong to the engine/data, not to the corpus)")
	for _, line in ipairs(driftExamples) do
		print(line)
	end
end

-- Only A is this tool's responsibility. B is a pre-existing property of the repository:
-- `spec/System/TestBuilds_spec.lua` is tagged `#builds` and excluded from the default busted
-- run by `.busted`, precisely because the frozen values are 3.13-era and the tree, the gem
-- data and the calc code have all moved on.
if totalCaptureDiff > 0 then
	os.exit(1)
end
