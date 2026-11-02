"""
Runs the race mode's police chase (apps/new-modes/sc_race/mode.lua) in a stub world, without Assetto Corsa.

The stub is a round track in the XZ plane. Cars move along it at the speeds each scenario gives them (the player's,
the rival's and the cops', or one function of the car's index for a field of rivals), the cops as the mode's physics
calls allow (held, capped, put somewhere else). Everything CSP gives the mode is stubbed as far as
the mode uses it. What comes out is the result file the mode writes, which each scenario checks.

It checks the mode's own logic: when the patrol shows up, who is busted, who gets away, the roadblock, what the
result says. Not how CSP's AI drives, which only the game can show.

It runs the mode on LuaJIT, as CSP does (lupa's luajit21 runtime), and writes its JSON as CSP's encoder (rxi's
json.lua) does: an empty table is [], strings are escaped, a sparse array or a key that is not a string is an error.

    python -m pip install -r tools/sc_race_harness/requirements.txt
    python tools/sc_race_harness/test_chase.py
    python tools/sc_race_harness/test_chase.py --golden   (writes the C# contract fixture again, see golden_file)
    python tools/sc_race_harness/test_chase.py --allow-plain-lua   (no LuaJIT here: a quick look only, not CSP's Lua)
"""

import json
import os
import sys

# LuaJIT 2.1 or nothing: plain Lua (5.4 in lupa) is not the language CSP runs (integers, how numbers print, the
# libraries there are), so a pass there says little about the game. --allow-plain-lua runs anyway, for a quick look. The siblings
# (test_heat.py, test_strip.py) import this module, so the rule and the switch hold for them too
try:
    import lupa.luajit21 as lupa
except ImportError:
    if '--allow-plain-lua' not in sys.argv:
        sys.exit('lupa has no LuaJIT 2.1 runtime here (pip install -r tools/sc_race_harness/requirements.txt): '
                 'CSP runs the mode on LuaJIT. Pass --allow-plain-lua to run on plain Lua anyway')
    import lupa
    print('WARNING: lupa has no LuaJIT 2.1 runtime here: running on plain Lua, which is not what CSP runs')

HERE = os.path.dirname(os.path.abspath(__file__))
MODE = os.path.join(HERE, '..', '..', 'apps', 'new-modes', 'sc_race', 'mode.lua')

# The last run's start mark as "path=stamp" (the mode writes it once it has loaded whole), None when it wrote none
LAST_START_MARK = None
# What the mode writes, as the C# side's contract test reads it (RaceResultContractTests)
GOLDEN = os.path.join(HERE, '..', '..', 'tests', 'StreetCorsa.Tests', 'Fixtures', 'sc_race_result.json')
GOLDEN_CONTEXT_ID = '6f1c2d3e-4b5a-4c6d-8e7f-90a1b2c3d4e5'

STUB = r'''
local world = ...
local L = world.length
local R = L / (2 * math.pi)

-- vectors -------------------------------------------------------------------
local V = {}
V.__index = V
local function isvec(v) return type(v) == 'table' and getmetatable(v) == V end
function vec3(x, y, z) return setmetatable({ x = x or 0, y = y or 0, z = z or 0 }, V) end
function vec2(x, y) return vec3(x, y, 0) end
function vec4(x, y, z, w) local v = vec3(x, y, z); v.w = w; return v end
function V:set(x, y, z)
  if isvec(x) then self.x, self.y, self.z = x.x, x.y, x.z else self.x, self.y, self.z = x, y, z end
  return self
end
function V:add(v) self.x, self.y, self.z = self.x + v.x, self.y + v.y, self.z + v.z; return self end
function V:sub(v) self.x, self.y, self.z = self.x - v.x, self.y - v.y, self.z - v.z; return self end
function V:scale(k) self.x, self.y, self.z = self.x * k, self.y * k, self.z * k; return self end
function V:addScaled(v, k) self.x, self.y, self.z = self.x + v.x * k, self.y + v.y * k, self.z + v.z * k; return self end
function V:length() return math.sqrt(self.x * self.x + self.y * self.y + self.z * self.z) end
function V:distance(v) return math.sqrt((self.x - v.x) ^ 2 + (self.y - v.y) ^ 2 + (self.z - v.z) ^ 2) end
function V:dot(v) return self.x * v.x + self.y * v.y + self.z * v.z end
function V:normalize() local l = self:length(); if l > 0 then self:scale(1 / l) end; return self end
V.__sub = function(a, b) return vec3(a.x - b.x, a.y - b.y, a.z - b.z) end
V.__add = function(a, b) return vec3(a.x + b.x, a.y + b.y, a.z + b.z) end
V.__unm = function(a) return vec3(-a.x, -a.y, -a.z) end
function rgb(r, g, b) return { r = r, g = g, b = b } end
function rgbm(r, g, b, m) return { r = r, g = g, b = b, m = m } end

-- the track -----------------------------------------------------------------
local function wrap(s) return s - math.floor(s / L) * L end
local function pointAt(s, lateral)
  -- A drag strip (world.straight): down the x axis, the lanes either side of it
  if world.straight then return vec3(s, 0, lateral or 0), vec3(1, 0, 0), vec3(0, 0, 1) end
  local a = wrap(s) / R
  local dir = vec3(-math.sin(a), 0, math.cos(a))
  local right = vec3(-dir.z, 0, dir.x)
  return vec3(R * math.cos(a), 0, R * math.sin(a)):addScaled(right, lateral or 0), dir, right
end
local function progressOf(v)
  if world.straight then return wrap(v.x) / L end
  local a = (math.atan2 or math.atan)(v.z, v.x)
  return wrap(a * R) / L
end

-- the cars ------------------------------------------------------------------
local sim = { time = 0, isSessionStarted = false, isInMainMenu = false, isPaused = false, carsCount = world.cars,
  trackLengthM = L, raceSessionType = 3 }
local cars, controls = {}, {}
for i = 0, world.cars - 1 do
  cars[i] = { index = i, total = world.straight and -1 or -10 * (i + 1), lane = world.straight and (i == 0 and -2 or 2) or 0, speedKmh = 0, lapCount = 0, damage = { [0] = 0, 0, 0, 0 }, engineLifeLeft = 1000,
    gearboxDamage = 0, wheels = {}, aabbSize = vec3(1.9, 1.4, 5), aabbCenter = vec3(0, 0.6, 0), fuel = 40,
    waterTemperature = 90, oilTemperature = 100, oilPressure = 4, previousLapTimeMs = 0, collidedWith = 0,
    -- Its place on the grid, AC's race position before the green (world.grid: {car index: place}, else by index)
    racePosition = world.grid and world.grid[i] or i + 1,
    position = vec3(), velocity = vec3(), look = vec3(), up = vec3(0, 1, 0), side = vec3(), splinePosition = 0, localAngularVelocity = vec3(),
    rpm = 900, rpmLimiter = 6000, gas = 0, acceleration = vec3(), maxFuel = 60, dirt = 0, restrictor = 0 }
  for w = 0, 3 do cars[i].wheels[w] = { isBlown = false, suspensionDamage = 0, tyreWear = 0, tyreVirtualKM = 0 } end
  controls[i] = { throttle = 1, stop = 0, top = 1e9, active = true, placed = 0 }
end
-- The race's own clock (seconds since the session started, negative before): what world.speed and world.slip are given
local raceSeconds = -3
local function refresh(car)
  local pos, dir, right = pointAt(car.total, car.lane)
  car.position:set(pos)
  car.look:set(dir)
  car.side:set(right)
  car.velocity:set(dir):scale(car.speedKmh / 3.6)
  -- Its yaw rate (rad/s about its up axis): round the loop at its speed, none down a strip
  car.localAngularVelocity:set(vec3(0, world.straight and 0 or car.speedKmh / 3.6 / R, 0))
  -- world.slip(i, raceSeconds): the car's angle to its path, in degrees (a drift); the velocity turned off the look by it
  if world.slip then
    local angle = math.rad(world.slip(car.index, raceSeconds) or 0)
    car.velocity:set(dir):scale(math.cos(angle)):addScaled(right, math.sin(angle)):scale(car.speedKmh / 3.6)
  end
  car.splinePosition = wrap(car.total) / L
  car.lapCount = math.max(0, math.floor(car.total / L))
end
for i = 0, world.cars - 1 do refresh(cars[i]) end

-- CSP -------------------------------------------------------------------------
local log = {}
local targets = {}
local timers = {}
local saved = {}
local quit = false
local placements = {}
local iniValues = world.ini

ac = {
  FolderID = { ACDocuments = 1, ScriptOrigin = 2 },
  LightType = { Regular = 1 },
  getSim = function() return sim end,
  getCar = function(i) return cars[i] end,
  -- The player, the rival (Rival 1, Rival 2... in a field) and the police after them
  getDriverName = function(i)
    if i == 0 then return 'Player' end
    if i <= world.rivals then return world.rivals == 1 and 'Rival' or 'Rival ' .. i end
    return 'Police'
  end,
  getPatchVersionCode = function() return world.oldCsp and 2000 or 3000 end,
  getCarName = function(i) return 'car_' .. i end,
  -- As CSP reads race.ini: a value is split at its commas; with no default the list comes back, with a string default
  -- only the first item (the trap that once dropped every cop but the first)
  INIConfig = { raceConfig = function() return { get = function(_, section, key, default)
    local v = iniValues[section .. '.' .. key]
    if v == nil then return default end
    local items = {}
    for part in tostring(v):gmatch('[^,]+') do items[#items + 1] = part end
    if default == nil then return items end
    return items[1] end } end },
  -- Each cop's target, as the mode announces it: the stub's cars block the cop behind it
  log = function(s)
    log[#log + 1] = s
    -- The greens the mode gives, on its race clock: the harness's drivers leave on them
    local g0 = s:match('greens at ([%d%.]+) and')
    if g0 then world.green0 = tonumber(g0) end
    local tree = s:match('tree on, green at ([%d%.]+) s')
    if tree then world.green0 = tonumber(tree) end
    local away = s:match('Green for car 1 at [%d%.]+ s, away at ([%d%.]+) s')
    if away then world.away1 = tonumber(away) end
    local cop, racer = s:match('Cop (%d+) after car (%d+)')
    if not cop then cop, racer = s:match('Cop (%d+) in its speed trap saw car (%d+)') end
    if cop then targets[tonumber(cop)] = tonumber(racer) end
  end,
  setMessage = function(t, d) log[#log + 1] = '[message] ' .. t .. ': ' .. tostring(d) end,
  setBodyDirt = function(i, v) cars[i].dirt = v end,
  setCarActive = function(i, a) controls[i].active = a end,
  getCarLeaderboardPosition = function(i) return cars[i].racePosition end,
  getTrackID = function() return 'stub_loop' end,
  getTrackLayout = function() return '' end,
  getFolder = function() return 'C:\\stub' end,
  -- The mode's collision callback: world.crashAt wrecks a car through it
  onCarCollision = function(_, callback) world.collision = callback end,
  -- The mode's jump callback: world.pitsAt puts a car back in the pits through it
  onCarJumped = function(_, callback) world.jumped = callback end,
  disableCarRecovery = function() end,
  tryToStart = function() return true end,
  setStartMessage = function() end,
  shutdownAssettoCorsa = function() quit = true end,
  worldCoordinateToTrackProgress = progressOf,
  trackProgressToWorldCoordinateTo = function(t, r) r:set(pointAt(t * L, 0)) end,
  getTrackAISplineSides = function() return vec2(world.sides or 6, world.sides or 6) end,
  getCameraPosition = function() return vec3() end,
  LightSource = function() return { dispose = function() end } end,
  AudioEvent = { fromFile = function() return { start = function() end, seek = function() end, stop = function() end,
    dispose = function() end, setPosition = function() end } end },
}
physics = {
  allowed = function() return world.physicsAllowed ~= false end,
  blockTeleportingToPits = function() return {} end,
  setCarBodyDamage = function() end,
  setCarEngineLife = function(i, v) cars[i].engineLifeLeft = v end,
  setCarFuel = function(i, v) cars[i].fuel = v end,
  setWaterTemperature = function(i, v) cars[i].waterTemperature = v end,
  setCarRestrictor = function(i, v) cars[i].restrictor = v; cars[i].peakRestrictor = math.max(cars[i].peakRestrictor or 0, v) end,
  setCarNoInput = function() end, lockUserControlsFor = function() end,
  -- The player's forced brakes, until world.brakesUntil (seconds of the stub's clock): the strip's drivers wait for them
  forceUserBrakesFor = function(t, v) world.brakesUntil = (t > 0 and (v or 1) > 0) and sim.time / 1000 + t or nil end,
  setAIThrottleLimit = function(i, v) controls[i].throttle = v end,
  setAIStopCounter = function(i, v) controls[i].stop = v end,
  setAITopSpeed = function(i, v) controls[i].top = v / 3.6 end,
  disableCarCollisions = function() end,
  setCarVelocity = function(i, v) cars[i].speedKmh = v:length() * 3.6 end,
  setCarPosition = function(i, pos, dir)
    local car = cars[i]
    -- world.placeOffset: how far past where it was asked the car lands (AC places the car's origin, not its position)
    car.total, car.speedKmh = pos.x + (world.placeOffset or 0), 0
    placements[#placements + 1] = { index = i, t = sim.time / 1000, progress = progressOf(pos) }
    refresh(car)
  end,
  engageGear = function() end,
  setAICarPosition = function(i, pos, facing)
    local car = cars[i]
    -- On the lap that puts it at most a quarter lap behind the player or up to three quarters ahead: the mode puts
    -- cops a few hundred metres behind a racer, or ahead (a roadblock, a trap anywhere round the lap)
    local s = progressOf(pos) * L
    car.total = s + math.ceil((cars[0].total - 0.25 * L - s) / L) * L
    controls[i].placed = controls[i].placed + 1
    placements[#placements + 1] = { index = i, t = sim.time / 1000, progress = progressOf(pos) }
    refresh(car)
  end,
  awakeCar = function() end, setEngineStallEnabled = function() end, setEngineRPM = function() end,
  setAIPitStopRequest = function() end,
  -- Said once a car, not every frame it is called
  preventAIFromRetiring = function(i)
    world.kept = world.kept or {}
    if not world.kept[i] then world.kept[i] = true; log[#log + 1] = 'kept from retiring: car ' .. tostring(i) end
  end,
  setAILevel = function() end, setAIAggression = function() end, setExtraAIGrip = function() end,
  setAISplineAbsoluteOffset = function() end,
  setGentleStop = function(i, v) controls[i].gentle = v ~= false end,
  setCarAutopilot = function() end,
  raycastTrack = function(pos) return pos.y end,
}
render = { circle = function() end }
-- What the mode draws: each text it puts on the screen goes to world.onText(seconds, text) (the HUD, the result card)
ui = { Font = { Title = 1, Main = 2, Monospace = 3 }, windowSize = function() return vec2(1920, 1080) end,
  drawRectFilled = function() end, drawRect = function() end, pushFont = function() end, popFont = function() end,
  drawTextClipped = function(text) if world.onText then world.onText(sim.time / 1000, text) end end }
-- CSP's JSON.stringify is rxi's json.lua encoder: this is its encode, as it is
JSON = {}
local BS = string.char(92) -- a backslash, spelled out: this Lua sits in a Python string
local escapes = { [BS] = BS .. BS, ['"'] = BS .. '"', [string.char(8)] = BS .. 'b', [string.char(12)] = BS .. 'f',
  [string.char(10)] = BS .. 'n', [string.char(13)] = BS .. 'r', [string.char(9)] = BS .. 't' }
local function escape(c) return escapes[c] or string.format(BS .. 'u%04x', c:byte()) end
local encode
local function encodeTable(val, stack)
  stack = stack or {}
  if stack[val] then error('circular reference') end
  stack[val] = true
  local res = {}
  if rawget(val, 1) ~= nil or next(val) == nil then
    -- An array: keys 1..n and nothing else
    local n = 0
    for k in pairs(val) do
      if type(k) ~= 'number' then error('invalid table: mixed or invalid key types') end
      n = n + 1
    end
    if n ~= #val then error('invalid table: sparse array') end
    for _, v in ipairs(val) do res[#res + 1] = encode(v, stack) end
    stack[val] = nil
    return '[' .. table.concat(res, ',') .. ']'
  end
  for k, v in pairs(val) do
    if type(k) ~= 'string' then error('invalid table: mixed or invalid key types') end
    res[#res + 1] = encode(k, stack) .. ':' .. encode(v, stack)
  end
  stack[val] = nil
  return '{' .. table.concat(res, ',') .. '}'
end
encode = function(val, stack)
  local t = type(val)
  if t == 'nil' then return 'null' end
  if t == 'boolean' then return tostring(val) end
  if t == 'string' then return '"' .. (val:gsub('[%c"' .. BS .. ']', escape)) .. '"' end
  if t == 'number' then
    if val ~= val or val <= -math.huge or val >= math.huge then error("unexpected number value '" .. tostring(val) .. "'") end
    return string.format('%.14g', val)
  end
  if t == 'table' then return encodeTable(val, stack) end
  error("unexpected type '" .. t .. "'")
end
function JSON.stringify(v) return encode(v) end
io.createDir = function() end
io.save = function(p, s) saved[p] = s; return true end
-- CSP's io.move fails onto an existing file unless failIfExists is false; world.oldCsp: a build that does not take
-- the third argument, so it always fails onto one
io.move = function(a, b, failIfExists)
  if world.oldCsp then failIfExists = nil end
  if saved[b] ~= nil and failIfExists ~= false then return false end
  saved[b] = saved[a]; saved[a] = nil; return true
end
io.deleteFile = function(p) saved[p] = nil end
os.preciseClock = os.clock
-- The mode seeds from the clock and a heap address, so each run would place its traps elsewhere and a test could
-- pass or fail by chance: the harness pins the seed, and every run is the same race
local randomseed = math.randomseed
math.randomseed = function() randomseed(1970) end
-- world.random: every roll of math.random() comes up this (a breakdown's chance a frame is then a fact, not a draw);
-- the draws with a range (a trap's spot) stay the seeded ones
local random = math.random
math.random = function(m, n)
  if world.random ~= nil and m == nil then return world.random end
  if m == nil then return random() elseif n == nil then return random(m) else return random(m, n) end
end
function setInterval(f, t) timers[#timers + 1] = { f = f, every = t, due = t }; return #timers end
function clearInterval(id) if timers[id] then timers[id].dead = true end end
function setTimeout(f, t) timers[#timers + 1] = { f = f, due = t, once = true }; return #timers end
script = {}

-- the run ---------------------------------------------------------------------
local function step(dt, t)
  sim.time = sim.time + dt * 1000
  world.simSeconds = sim.time / 1000
  raceSeconds = t - 3
  if not sim.isSessionStarted and t >= 3 then sim.isSessionStarted = true end
  if world.menuAt and t >= world.menuAt then sim.isInMainMenu = true end
  for i = 0, world.cars - 1 do
    local car, c = cars[i], controls[i]
    local want
    -- On the strip each car accelerates as its driver says (world.accel: m/s², 'hold' or 'stop'); an AI car with no
    -- throttle coasts, one held stops
    if world.straight then
      local speed = car.speedKmh / 3.6
      local a = sim.isSessionStarted and world.accel(i, t - 3, car) or 'hold'
      if i > 0 and c.stop > 0 then a = 'hold' elseif i > 0 and c.throttle <= 0 and type(a) == 'number' then a = -2 end
      c.stop = math.max(0, c.stop - dt)
      if a == 'hold' or a == 'stop' then speed = 0 else speed = math.max(0, math.min(60, speed + a * dt)) end
      -- An AI car held to a top speed brakes down to it
      if i > 0 and speed > c.top then speed = math.max(c.top, speed - 8 * dt) end
      want = speed
    elseif not sim.isSessionStarted then want = 0
    elseif world.speed then want = world.speed(i, t - 3)
    elseif i == 0 then want = world.playerSpeed(t)
    elseif i == 1 then want = world.rivalSpeed(t)
    else want = world.copSpeed(t, i) end
    if not world.straight then
      if i > 0 and (c.throttle <= 0 or c.stop > 0) then want = 0 end
      -- The player's brakes held on (a train start's hold) stop the car
      if i == 0 and world.holdBrakes and world.brakesUntil and sim.time / 1000 < world.brakesUntil then want = 0 end
      if i > 0 then want = math.min(want, c.top) end
      c.stop = math.max(0, c.stop - dt)
    end
    -- A car being stopped gently (the busted player, pulled over) stops
    if c.gentle then want = 0 end
    -- world.crashAt[i]: the race second car i hits something, stopped dead in a frame (a crash's g) as CSP reports
    -- the collision
    local crashAt = world.crashAt and world.crashAt[i]
    if crashAt and sim.isSessionStarted and t - 3 >= crashAt then
      want = 0
      if not car.wrecked then
        car.wrecked = true
        if world.collision then world.collision(i) end
      end
    end
    -- world.pitsAt[i]: the race second car i is put back in the pits (the player's Esc, AC's retirement): near the
    -- end of the lap it is on, the pit lane's spline, stopped there
    local pitsAt = world.pitsAt and world.pitsAt[i]
    if pitsAt and sim.isSessionStarted and t - 3 >= pitsAt then
      want = 0
      if not car.inPits then
        car.inPits = true
        car.total = math.floor(car.total / L) * L + 0.95 * L
        refresh(car)
        if world.jumped then world.jumped(i) end
      end
    end
    car.total = car.total + want * dt
    car.speedKmh = want * 3.6
    -- The engine: world.engine(i, raceSeconds, car) gives the throttle, the revs as a share of the limiter and the g the
    -- car pulls; without it a moving car cruises at 60% throttle and revs. world.fuelBurn: litres a second at full throttle
    if sim.isSessionStarted and world.engine then
      local gas, revs, g = world.engine(i, t - 3, car)
      car.gas, car.rpm, car.acceleration.x = gas, revs * car.rpmLimiter, g or 0
    else
      car.gas = want > 0 and 0.6 or 0
      car.rpm = want > 0 and 0.6 * car.rpmLimiter or 900
    end
    if world.fuelBurn and sim.isSessionStarted then car.fuel = math.max(0, car.fuel - world.fuelBurn * car.gas * dt) end
    -- The rear tyres' drive (world.axleForce(i, raceSeconds, car): N each, on the wheels CSP numbers 2 and 3), at a loaded
    -- radius of 0.3 m; without it the wheels have no fx, as a CSP without the field would leave them
    if world.axleForce and sim.isSessionStarted then
      for w = 2, 3 do
        car.wheels[w].fx = world.axleForce(i, t - 3, car)
        car.wheels[w].tyreLoadedRadius = 0.3
      end
    end
    refresh(car)
  end
  for _, timer in ipairs(timers) do
    if not timer.dead then
      timer.due = timer.due - dt
      if timer.due <= 0 then
        timer.f()
        if timer.once then timer.dead = true else timer.due = timer.every end
      end
    end
  end
end

return function(modeSource)
  local chunk = assert(load(modeSource, '=mode.lua'))
  chunk()
  local t, dt = 0, 0.05
  while t < world.maxSeconds and not quit do
    step(dt, t)
    if script.update then script.update(dt) end
    if world.onText and script.drawUI then script.drawUI() end
    t = t + dt
  end
  -- The result is the one .json the mode leaves (renamed from its .tmp); its start mark, the file under
  -- out\sc_race_started, goes back to the run as world.startMark (path=stamp)
  local result
  for p, v in pairs(saved) do
    if p:find('sc_race_started', 1, true) then world.startMark = p .. '=' .. v
    elseif p:sub(-5) == '.json' then result = v end
  end
  return result, table.concat(log, '\n'), t, #placements
end
'''


def run(name, cars, ini, player, rival, cop, max_seconds=400, length=2000.0, sides=6.0, strip=None, menu_at=None,
        place_offset=0.0, engine=None, fuel_burn=None, physics_allowed=True, axle_force=None, random=None, slip=None,
        modules=None, old_csp=False, speed=None, crash_at=None, on_text=None, hold_brakes=False, grid=None, pits_at=None):
    """strip: a drag strip instead of the loop, strip(world, i, raceSeconds, car) giving each car's acceleration;
    place_offset: metres past the asked spot that setCarPosition leaves a car; engine(i, raceSeconds): each car's
    throttle, revs (a share of the limiter) and g; fuel_burn: litres a second at full throttle; physics_allowed: False
    for a mode that may not touch the physics; axle_force(i, raceSeconds): the N each rear tyre drives with (none: the
    wheels have no fx at all); random: what every plain math.random() comes up; slip(i, raceSeconds): each car's angle to
    its path in degrees (a drift); modules: a folder of a mod's format modules, found by require too; old_csp: a CSP
    build older than io.move's third argument; speed(i, raceSeconds): every car's speed by its index, in place of
    player, rival and cop (a field); crash_at: {car index: race second} the car crashes; on_text(seconds, text): each
    text the mode draws (its HUD, the result card); hold_brakes: the player's car stops while the mode holds its
    brakes on (a train start); grid: {car index: place} each car's place on the grid (AC's race position), else by index;
    pits_at: {car index: race second} the car is put back in the pits"""
    lua = lupa.LuaRuntime(unpack_returned_tuples=True)
    world = lua.table_from({
        'length': length, 'cars': cars, 'maxSeconds': max_seconds, 'sides': sides,
        'ini': lua.table_from(ini),
        'playerSpeed': player, 'rivalSpeed': rival, 'copSpeed': cop,
        'straight': strip is not None, 'menuAt': menu_at, 'placeOffset': place_offset, 'fuelBurn': fuel_burn,
        'physicsAllowed': physics_allowed, 'random': random, 'oldCsp': old_csp,
        # The rivals the race has (race.ini RIVALS, one when not told), for the drivers' names
        'rivals': int(ini.get('STREET_CORSA.RIVALS', 1 if cars > 1 else 0)), 'holdBrakes': hold_brakes,
    })
    if speed is not None:
        world['speed'] = lambda i, t: speed(i, t)
    if crash_at is not None:
        world['crashAt'] = lua.table_from(crash_at)
    if grid is not None:
        world['grid'] = lua.table_from(grid)
    if pits_at is not None:
        world['pitsAt'] = lua.table_from(pits_at)
    if on_text is not None:
        world['onText'] = lambda seconds, text: on_text(seconds, text)
    if slip is not None:
        world['slip'] = lambda i, t: slip(i, t)
    if engine is not None:
        world['engine'] = lambda i, t, car: engine(i, t)
    if axle_force is not None:
        world['axleForce'] = lambda i, t, car: axle_force(i, t)
    if strip is not None:
        world['accel'] = lambda i, t, car: strip(world, i, t, car)
    # The format modules sit next to the mode and are loaded with require, as CSP finds them in the mode's folder; a
    # module that does not load fails the run (the mode would quietly run a road race)
    lua.globals().package.path = os.path.dirname(os.path.abspath(MODE)).replace('\\', '/') + '/?.lua'
    if modules:
        lua.globals().package.path += ';' + modules.replace('\\', '/') + '/?.lua'
    runner = lua.execute(STUB, world)
    with open(MODE, encoding='utf-8') as f:
        source = f.read()
    result, log, seconds, placements = runner(source)
    global LAST_START_MARK
    LAST_START_MARK = world['startMark']
    if 'did not load' in log:
        print(log)
        raise AssertionError(f'{name}: the race format module did not load')
    if result is None:
        print(log)
        raise AssertionError(f'{name}: no result written after {seconds:.0f} s')
    return json.loads(result), log, seconds, placements


def check(name, condition, log):
    if not condition:
        print(log)
        raise AssertionError(name)


POLICE_INI = {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.POLICE': '2,3', 'STREET_CORSA.POLICE_SPOT': '0.1'}


def overtaken_and_busted():
    # The cops are faster: the player's gets past him, and he is pulled over
    result, log, _, _ = run('overtaken', 4, POLICE_INI, lambda t: 30, lambda t: 20, lambda t, i: 45)
    pursuit = result['pursuit']
    check('the patrol showed up', pursuit['started'], log)
    check('a cop got past the player', 'got past car 0' in log, log)
    check('the player is busted', pursuit['player'] == 'BUSTED', log)
    check('before the line the race ends BUSTED', result['session']['end_reason'] == 'BUSTED', log)
    check('one cop for each racer', 'Cop 2 after car 0' in log and 'Cop 3 after car 1' in log, log)
    check('the police are not participants', len(result['participants']) == 2, log)
    check('both cops were read from POLICE=2,3', pursuit['police'] == 2, log)


def escaped_after_the_roadblocks():
    # Slow cops: they fall back, try their roadblock once each, chase again once dodged, and are left behind for good
    result, log, _, _ = run('escaped', 4, POLICE_INI, lambda t: 30, lambda t: 20, lambda t, i: 12)
    pursuit = result['pursuit']
    check('the player got away', pursuit['player'] == 'ESCAPED', log)
    check('the race was won at the line', result['session']['end_reason'] == 'FINISHED', log)
    check('the player finished first', result['participants'][0]['performance']['final_position'] == 1, log)
    check('a roadblock went up', 'sets up a roadblock' in log, log)
    check('a dodged roadblock chases again', 'got past its roadblock, after it' in log, log)
    check('nobody was busted by a roadblock ahead of them', 'got past car' not in log, log)


def rival_overtaken_the_player_gets_away():
    # The rival's cop is fast and gets past him; the player's is slow
    result, log, _, _ = run('rival busted', 4, POLICE_INI, lambda t: 30, lambda t: 20,
                            lambda t, i: 40 if i == 3 else 12)
    pursuit = result['pursuit']
    check('the rival is busted', pursuit['rival'] == 'BUSTED', log)
    check('by being overtaken', 'got past car 1' in log, log)
    check('the player got away', pursuit['player'] == 'ESCAPED', log)
    check('and won at the line', result['session']['end_reason'] == 'FINISHED', log)


def the_line_is_home():
    # A cop as fast as the player, on his tail the whole way: he crosses the line, and he made it
    result, log, _, _ = run('the line', 4, POLICE_INI, lambda t: 30, lambda t: 20, lambda t, i: 30)
    pursuit = result['pursuit']
    check('the race ends at the line', result['session']['end_reason'] == 'FINISHED', log)
    check('a player chased over the line got away', pursuit['player'] == 'ESCAPED', log)
    check('and won', result['participants'][0]['performance']['final_position'] == 1, log)


def no_police_no_pursuit():
    result, log, _, _ = run('no police', 2, {'STREET_CORSA.RACE_TYPE': 'ROAD'}, lambda t: 30, lambda t: 20, lambda t, i: 0)
    check('no pursuit', 'pursuit' not in result, log)
    check('won', result['participants'][0]['performance']['final_position'] == 1, log)


def the_mode_marks_that_it_started_with_the_races_id():
    # The launcher voids a race with no result and no mark (the mode never loaded), so the mark is the race's own
    ini = {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.CONTEXT_ID': GOLDEN_CONTEXT_ID}
    _, log, _, _ = run('start mark', 2, ini, lambda t: 30, lambda t: 20, lambda t, i: 0)
    check(f'the mark is under out/sc_race_started, named by the context id: {LAST_START_MARK}',
          LAST_START_MARK is not None and LAST_START_MARK.split('=')[0].replace('/', chr(92)).endswith(
              chr(92).join(['out', 'sc_race_started', GOLDEN_CONTEXT_ID])), log)
    check('the mark names the mode and the CSP build', 'sc_race ' in LAST_START_MARK and 'CSP build' in LAST_START_MARK, log)


def no_context_id_no_mark():
    run('no mark', 2, {'STREET_CORSA.RACE_TYPE': 'ROAD'}, lambda t: 30, lambda t: 20, lambda t, i: 0)
    check('a race without a context id leaves no mark', LAST_START_MARK is None, '')


TRAPS_INI = {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.POLICE': '2,3', 'STREET_CORSA.POLICE_SPOT': '0.1',
             'STREET_CORSA.POLICE_MODE': 'TRAPS'}


def trap_sees_the_player_who_is_then_busted():
    # An 8 km loop: straight enough for traps. The trap cop is faster and gets past him
    result, log, _, _ = run('trap busted', 4, TRAPS_INI, lambda t: 30, lambda t: 20, lambda t, i: 45,
                            max_seconds=700, length=8000.0)
    pursuit = result['pursuit']
    check('the cops were parked in traps', log.count('parked in a speed trap') == 2, log)
    check('a trap saw the player', 'saw car 0 go by' in log, log)
    check('and got past him', 'got past car 0' in log, log)
    check('the player is busted', pursuit['player'] == 'BUSTED', log)


def each_racer_gets_a_trap_of_his_own():
    # Slow cops. The player goes past the first trap first; the rival, behind him, finds it taken and gets the second
    result, log, _, _ = run('a trap each', 4, TRAPS_INI, lambda t: 30, lambda t: 27, lambda t, i: 10,
                            max_seconds=700, length=8000.0)
    check('a trap saw the player', 'saw car 0 go by' in log, log)
    check('the other trap saw the rival', 'saw car 1 go by' in log, log)
    check('two different cops', log.count('in its speed trap saw car') == 2, log)


def only_the_rival_is_seen_and_the_player_finishes_clean():
    # One trap. The rival is faster, goes past it first and stops later on (7.7 km, past the last spot a trap can
    # take); the player never has a cop after him
    ini = dict(TRAPS_INI, **{'STREET_CORSA.POLICE': '2'})
    result, log, _, _ = run('rival seen', 3, ini, lambda t: 25, lambda t: 35 if t < 220 else 0, lambda t, i: 45,
                            max_seconds=700, length=8000.0)
    pursuit = result['pursuit']
    check('the trap saw the rival', 'saw car 1 go by' in log, log)
    check('the rival is busted', pursuit['rival'] == 'BUSTED', log)
    check("the player's chase was never on", pursuit.get('player') is None, log)
    check('the player finished the race and won it', result['session']['end_reason'] == 'FINISHED'
          and result['participants'][0]['performance']['final_position'] == 1, log)


def no_room_for_traps_means_a_patrol():
    result, log, _, _ = run('no room', 4, TRAPS_INI, lambda t: 30, lambda t: 20, lambda t, i: 12, sides=2.0)
    check('no trap spot found', 'No spot for a speed trap' in log, log)
    check('the patrol came instead', 'after car 0 from 200 m back' in log, log)
    check('and the chase ran', result['pursuit']['started'], log)


def paths(value, prefix=''):
    """Every key the file has, as a path (participants[].crash.crashed): its shape, whatever the values"""
    if isinstance(value, dict):
        found = {prefix} if prefix else set()
        for key, item in value.items():
            found |= paths(item, f'{prefix}.{key}' if prefix else key)
        return found
    if isinstance(value, list):
        found = {prefix + '[]'}
        for item in value:
            found |= paths(item, prefix + '[]')
        return found
    return {prefix}


def golden_file(write=False):
    # The C# side reads this file as the mode's contract (RaceResultContractTests): a road race with the police,
    # won at the line after getting away. The file must have the shape the mode writes now; --golden writes it again.
    # With a rear axle, as the career always gives one: the condition's axle fields are part of the shape
    ini = dict(POLICE_INI, **{'STREET_CORSA.CONTEXT_ID': GOLDEN_CONTEXT_ID, 'STREET_CORSA.FORMAT': 'circuit',
                              'STREET_CORSA.MODULE': 'format_road', 'STREET_CORSA.LAPS': '1', 'STREET_CORSA.CAR_0_AXLE': '2500'})
    result, log, _, _ = run('golden', 4, ini, lambda t: 30, lambda t: 20, lambda t, i: 12)
    if write:
        with open(GOLDEN, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(result, f, indent=2, sort_keys=True)
            f.write('\n')
        print(f'wrote {os.path.normpath(GOLDEN)}')
    with open(GOLDEN, encoding='utf-8') as f:
        golden = json.load(f)
    missing, extra = paths(result) - paths(golden), paths(golden) - paths(result)
    check(f'the golden file has the shape the mode writes (run with --golden to write it again); '
          f'written but not in it: {sorted(missing)}, in it but not written: {sorted(extra)}', not missing and not extra, log)
    check('every wheel has its place', [w['wheel'] for w in result['participants'][0]['condition']['wheels']] == [0, 1, 2, 3], log)


if __name__ == '__main__':
    if '--golden' in sys.argv:
        golden_file(write=True)
    for test in (golden_file, overtaken_and_busted, escaped_after_the_roadblocks, rival_overtaken_the_player_gets_away, the_line_is_home, no_police_no_pursuit,
                 trap_sees_the_player_who_is_then_busted, each_racer_gets_a_trap_of_his_own,
                 only_the_rival_is_seen_and_the_player_finishes_clean, no_room_for_traps_means_a_patrol,
                 the_mode_marks_that_it_started_with_the_races_id, no_context_id_no_mark):
        test()
        print(f'ok  {test.__name__}')
    sys.exit(0)
