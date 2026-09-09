--[[
	Deterministic pseudo-random source for the differential fuzzer (migration ticket 08).

	`math.random` is NOT used anywhere in this tool. LuaJIT and Lua 5.1 ship different
	generators (LuaJIT has its own Tausworthe PRNG; Lua 5.1 forwards to C `rand`), so a
	corpus seeded through `math.random` is not reproducible across interpreters -- or even
	across libc versions on the same interpreter. `dotnet/tools/luacompat-oracle/dump.lua`
	hit the same wall and solved it the same way.

	This is the minimal-standard Lehmer LCG (Park-Miller, multiplier 16807, modulus
	2^31-1). Every intermediate stays under 2^53, so each step is exact in IEEE double
	arithmetic and produces the identical stream on any conforming Lua.
]]

local rng = {}
rng.__index = rng

local MODULUS = 2147483647   -- 2^31 - 1
local MULTIPLIER = 16807

--- Create a generator. `seed` is normalised into 1 .. MODULUS-1; the LCG has a fixed
--- point at 0, so a zero seed would return a constant stream.
function rng.new(seed)
	local s = math.floor(tonumber(seed) or 1)
	s = s % MODULUS
	if s < 0 then
		s = s + MODULUS
	end
	if s == 0 then
		s = 1
	end
	return setmetatable({ state = s, draws = 0 }, rng)
end

--- Raw state advance. Returns the new integer state in 1 .. MODULUS-1.
function rng:step()
	self.state = (self.state * MULTIPLIER) % MODULUS
	self.draws = self.draws + 1
	return self.state
end

--- Uniform double in [0, 1).
function rng:float()
	return (self:step() - 1) / (MODULUS - 1)
end

--- Uniform integer in [lo, hi] inclusive. Uses integer arithmetic on the raw state so
--- the result never depends on double rounding of a scaled float.
function rng:int(lo, hi)
	if hi < lo then
		return lo
	end
	local span = hi - lo + 1
	return lo + (self:step() % span)
end

--- True with probability `p` (0..1). `p` is compared against a fixed-point form of the
--- state so the comparison is exact rather than dependent on float rounding.
function rng:chance(p)
	local threshold = math.floor(p * 1000000 + 0.5)
	return (self:step() % 1000000) < threshold
end

--- One element of an array, or nil when the array is empty.
function rng:pick(list)
	local n = #list
	if n == 0 then
		return nil
	end
	return list[self:int(1, n)]
end

--- A value in [lo, hi] quantised to `steps` equal buckets, so the emitted number always
--- has a short exact decimal spelling (used for `{range:x}` tags on item mod lines).
function rng:quantised(lo, hi, steps)
	local k = self:int(0, steps)
	return lo + (hi - lo) * k / steps
end

--- Derive an independent child seed. Used to give every generated build its own stream,
--- so build N's recipe does not shift when the number of draws in build N-1 changes.
function rng:childSeed()
	return self:step()
end

return rng
