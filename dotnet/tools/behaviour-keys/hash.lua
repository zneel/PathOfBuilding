-- Path of Building — behaviour-key enumeration
--
-- FNV-1a 64 in pure Lua 5.1 arithmetic (no `bit` library, so the enumerator runs
-- identically under lua5.1 and luajit). The hash is a *grouping id*, not a
-- security primitive: its only job is to make byte-identical behaviour bodies
-- collide so duplicates are visibly grouped in behaviour-keys.json.
--
-- 64-bit values are held as four 16-bit limbs, little-endian, so every
-- intermediate product stays well inside the 2^53 exactly-representable range.

local M = {}

local xor8Cache = {}

local function xor8(a, b)
	local key = a * 256 + b
	local cached = xor8Cache[key]
	if cached then
		return cached
	end
	local result = 0
	local bitValue = 1
	local x, y = a, b
	for _ = 1, 8 do
		local xb = x % 2
		local yb = y % 2
		if xb ~= yb then
			result = result + bitValue
		end
		x = (x - xb) / 2
		y = (y - yb) / 2
		bitValue = bitValue * 2
	end
	xor8Cache[key] = result
	return result
end

-- 0x100000001b3
local PRIME = { 0x01b3, 0x0000, 0x0100, 0x0000 }

local function mulPrime(h)
	local a0, a1, a2, a3 = h[1], h[2], h[3], h[4]
	local b0, b1, b2, b3 = PRIME[1], PRIME[2], PRIME[3], PRIME[4]
	local r0 = a0 * b0
	local r1 = a0 * b1 + a1 * b0
	local r2 = a0 * b2 + a1 * b1 + a2 * b0
	local r3 = a0 * b3 + a1 * b2 + a2 * b1 + a3 * b0
	local carry = math.floor(r0 / 65536)
	h[1] = r0 % 65536
	r1 = r1 + carry
	carry = math.floor(r1 / 65536)
	h[2] = r1 % 65536
	r2 = r2 + carry
	carry = math.floor(r2 / 65536)
	h[3] = r2 % 65536
	r3 = r3 + carry
	h[4] = r3 % 65536
end

--- FNV-1a 64 of `s`, formatted as 16 lowercase hex digits.
function M.fnv1a64(s)
	-- 0xcbf29ce484222325
	local h = { 0x2325, 0x8422, 0x9ce4, 0xcbf2 }
	for i = 1, #s do
		local byte = s:byte(i)
		local low = h[1] % 256
		h[1] = (h[1] - low) + xor8(low, byte)
		mulPrime(h)
	end
	return string.format("%04x%04x%04x%04x", h[4], h[3], h[2], h[1])
end

--- Self-check against the published FNV-1a 64 test vectors. Called at load so a
--- broken port can never silently produce a plausible-looking key list.
function M.selfTest()
	local vectors = {
		[""] = "cbf29ce484222325",
		["a"] = "af63dc4c8601ec8c",
		["foobar"] = "85944171f73967e8",
	}
	for input, expected in pairs(vectors) do
		local actual = M.fnv1a64(input)
		if actual ~= expected then
			error(("fnv1a64(%q) = %s, expected %s"):format(input, actual, expected))
		end
	end
end

M.selfTest()

return M
