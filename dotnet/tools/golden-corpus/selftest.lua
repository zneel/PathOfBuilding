-- Proves the generator's engine-error detector actually fires (migration ticket 07).
--
-- Run from `src/`:
--
--   luajit ../dotnet/tools/golden-corpus/selftest.lua
--
-- The failure this guards against is silent. `launch:OnFrame` (src/Launch.lua:112) wraps the
-- whole calculation in `PCall` and does not rethrow, so a build that crashes mid-calculation
-- leaves `build.calcsTab.mainOutput` holding the PREVIOUS build's numbers while every call
-- the generator made returns normally. Written to disk, that is a golden file whose input is
-- one build and whose expected output is a different one - and every later ticket would be
-- verified against it.
--
-- This script injects a real calculation failure and checks four things:
--
--   1. the detector reports it, rather than the frame returning quietly;
--   2. the stale-output hazard is real - `mainOutput` really does still hold the previous
--      build's value at that moment, which is what would have been written out;
--   3. the detector clears `launch.promptMsg`, so the failure cannot leak into the next build;
--   4. the next build calculates normally afterwards.
--
-- Exit status is 0 only if all four hold.

local ROOT = "../dotnet/tools/golden-corpus/"

package.path = "../runtime/lua/?.lua;../runtime/lua/?/init.lua;./?.lua;" .. package.path

dofile("HeadlessWrapper.lua")

local chassis = dofile(ROOT .. "chassis.lua")

local s_format = string.format
local failures = {}

local function check(condition, message)
	if condition then
		print("  ok   " .. message)
	else
		print("  FAIL " .. message)
		failures[#failures + 1] = message
	end
end

--- Minimal but real build: class, tree, one weapon, one socket group.
local function makeBuild(skillName, weaponBase)
	newBuild()
	build.characterLevel = 92
	build.characterLevelAutoMode = false
	local shape = { classId = 4, ascendId = 1, budget = 40, ascendPoints = 4 }
	chassis.applyTree(build, shape, chassis.treeNodes(build, shape))
	chassis.resetItemIds()
	chassis.equip(build, chassis.makeRare(chassis.pickBase(weaponBase), "Selftest Weapon", {
		"Adds 32 to 58 Physical Damage",
		"145% increased Physical Damage",
	}, 20))
	local index = chassis.addSocketGroup(build, { gems = { { name = skillName } } })
	build.mainSocketGroup = index
	build.buildFlag = true
end

print("")
print("golden-corpus self test: engine-error detection")
print(string.rep("-", 78))

-- 1. A healthy build, to establish the value that a later failure would silently reuse.
chassis.resetErrorState()
makeBuild("Cleave", "Two Handed Axe")
chassis.frame("baseline build")
local baselineDps = build.calcsTab.mainOutput.TotalDPS
check(type(baselineDps) == "number" and baselineDps > 0,
	s_format("baseline build calculates (TotalDPS = %.4f)", baselineDps or 0))

-- 2. The same build, calculated correctly, to know what its answer key should say.
chassis.resetErrorState()
makeBuild("Molten Strike", "Two Handed Sword")
chassis.frame("reference build")
local referenceDps = build.calcsTab.mainOutput.TotalDPS
check(type(referenceDps) == "number" and referenceDps > 0,
	s_format("reference build calculates (TotalDPS = %.4f)", referenceDps or 0))

-- 3. The same build again, with a calculation failure injected. `CalcsTab:BuildOutput` is
--    where `calcs.buildOutput` is driven from, so failing it is a faithful stand-in for any
--    engine bug that surfaces there, and it travels through exactly the `PCall` in
--    `launch:OnFrame` a real one would.
chassis.resetErrorState()
makeBuild("Molten Strike", "Two Handed Sword")
build.calcsTab.BuildOutput = function()
	error("synthetic calculation failure injected by selftest.lua")
end

-- Deliberately the RAW call, with no detector, to show what an unguarded generator sees.
runCallback("OnFrame")
check(true, "runCallback returned normally even though the calculation threw")

local corruptedDps = build.calcsTab.mainOutput.TotalDPS
check(corruptedDps ~= referenceDps,
	s_format("mainOutput does NOT hold this build's real numbers (%.4f recorded vs %.4f correct) "
		.. "- this is what an unguarded generator would have written as the answer key",
		corruptedDps or 0, referenceDps or 0))
check(launch.promptMsg ~= nil,
	"the engine did record an error - in launch.promptMsg, where nothing was looking")

-- 4. The detector, run over exactly that state.
local ok, err = pcall(chassis.assertNoEngineError, "build with an injected failure")
check(not ok, "the detector raised on that state instead of letting it through")
check(ok == false and tostring(err):find("engine error swallowed by launch:OnFrame", 1, true) ~= nil,
	"the raised message names the swallowed engine error: " .. tostring(err):sub(1, 96))
check(launch.promptMsg == nil, "the detector cleared launch.promptMsg so it cannot leak forward")

-- 3. Recovery: the next build must calculate normally and produce its own numbers.
chassis.resetErrorState()
makeBuild("Ground Slam", "Two Handed Mace")
local recovered, recoveryErr = pcall(chassis.frame, "recovery build")
check(recovered, "the next build calculates normally after a failure: " .. tostring(recoveryErr))
local recoveredDps = build.calcsTab.mainOutput.TotalDPS
check(recovered and type(recoveredDps) == "number" and recoveredDps > 0 and recoveredDps ~= baselineDps,
	s_format("the recovered build produces its own TotalDPS (%.4f, baseline was %.4f)",
		recoveredDps or 0, baselineDps or 0))

print(string.rep("-", 78))
if #failures == 0 then
	print("all checks passed")
else
	print(s_format("%d check(s) failed", #failures))
	os.exit(1)
end
