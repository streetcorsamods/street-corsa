--[[
  Touge Nights: a drift battle (formats.json "drift", module drift: the game puts it into the race mode as
  format_touge_nights-drift.lua). The player and the rival run the course as ghosts, one after the other's line, over
  the race's laps (one on a pass, run from top to bottom). Points come from sliding: each moment the car is sideways
  past MIN_ANGLE at more than MIN_KMH earns its angle times its speed, times the combo's multiplier, which grows the
  longer the slide is held from one corner into the next. A combo is banked once the car is straight for COMBO_GAP
  seconds; a spin (past SPIN_ANGLE) or a touch loses the combo running. The score is every combo banked (higher wins:
  formats.json says win score, better higher).

  The AI can't drift: AC's AI drives the grip line. So the rival is judged as a ghost: wherever it corners hard enough,
  it is scored as if it slid at an angle its skill gives it (AI_LEVEL 0.85 to 1: GHOST_ANGLE_LOW to GHOST_ANGLE_HIGH),
  through the same multiplier, give or take a little each corner. A good rival makes the most of every corner of the
  pass; the player has to slide them all, and well, to beat one.

  The race is over once the player has run the laps and the rival has too, is out, or has had RIVAL_WAIT_SECONDS
  more; a rival still on the course then is scored on what it ran, stretched to the whole course.

  A format module of the race mode: see format_road.lua (in apps/new-modes/sc_race) for the hooks.
]]

local F = {}
local SC = nil

F.scored = true

-- Sideways past this many degrees, the car is drifting
local MIN_ANGLE = 12
-- Past this many it has spun: the combo running is lost
local SPIN_ANGLE = 110
-- Slower than this, no points
local MIN_KMH = 25
-- Seconds straight before a combo is banked
local COMBO_GAP = 1.5
-- The multiplier: up by MULT_STEP every MULT_SECONDS of drifting, at most MULT_MOST
local MULT_STEP, MULT_SECONDS, MULT_MOST = 0.25, 1.5, 5
-- A point for each km/h held at 45 degrees for a second; the angle counts up to ANGLE_MOST
local ANGLE_MOST = 60
-- How long the race waits for the rival once the player is done, in seconds
local RIVAL_WAIT_SECONDS = 60
-- The ghost's cornering: a corner is where its sideways pull passes this, in m/s^2
local GHOST_CORNER_G = 3.5
-- The angle a ghost is scored at, from the least skilled rival to the best
local GHOST_ANGLE_LOW, GHOST_ANGLE_HIGH = 18, 42
-- How far a ghost's corner may come out from its skill's, either way
local GHOST_SPREAD = 0.15

local playerDoneAt = nil

-- Each racer's drift: count the laps AC had last frame; laps done; score banked; combo running (points, seconds,
-- multiplier, seconds straight); angle now
function F.newCar(data)
  data.drift = { count = 0, laps = 0, score = 0, combo = 0, held = 0, mult = 1, straight = 0, angle = 0,
                 skill = nil, corner = nil, best = 0 }
end

local function done(data)
  return data.drift ~= nil and data.drift.laps >= SC.laps
end

-- The rival's skill, 0 to 1: AI_LEVEL of its car in race.ini (CSP gives it on the car too)
local function skillOf(carIndex, car)
  local level = car and car.aiLevel
  if type(level) ~= 'number' or level <= 0 then
    pcall(function() level = tonumber(ac.INIConfig.raceConfig():get('CAR_' .. carIndex, 'AI_LEVEL', '95')) end)
  end
  level = tonumber(level) or 95
  if level > 1 then level = level / 100 end
  return math.max(0, math.min(1, (level - 0.85) / 0.15))
end

function F.init(core)
  SC = core
  for i = 0, SC.count() - 1 do
    if not SC.isPolice(i) then
      local ok, err = pcall(physics.disableCarCollisions, i, true)
      if not ok then ac.log('[Street Corsa] Could not make car ' .. i .. ' a ghost: ' .. tostring(err)) end
    end
  end
  ac.log(string.format('[Street Corsa] Drift battle over %d lap(s)', SC.laps))
end

function F.pastTheLine(data)
  return done(data)
end

-- The car's angle to where it is going, in degrees (0 straight, 90 sideways, past 90 going backwards)
local function slipAngle(car)
  local speed = car.velocity:length()
  if speed < 0.5 then return 0 end
  local forward = car.velocity:dot(car.look) / speed
  local sideways = math.abs(car.velocity:dot(car.side)) / speed
  return math.deg(math.atan2(sideways, forward))
end

-- The combo running, banked (or lost)
local function bank(drift, lost, carIndex)
  if drift.combo > 0 then
    if lost then
      if carIndex == 0 then
        pcall(ac.setMessage, SC.say('DRIFT_TITLE', 'Drift'), SC.say('DRIFT_LOST', 'Combo lost: {points} points gone.', { points = math.floor(drift.combo) }))
      end
    else
      drift.score = drift.score + drift.combo
      if drift.combo > drift.best then drift.best = drift.combo end
    end
  end
  drift.combo, drift.held, drift.mult, drift.straight = 0, 0, 1, 0
end

-- A moment of sliding at angle and speed: points, the multiplier growing while it is held
local function slide(drift, angle, kmh, tick)
  drift.held = drift.held + tick
  drift.straight = 0
  drift.mult = math.min(MULT_MOST, 1 + math.floor(drift.held / MULT_SECONDS) * MULT_STEP)
  drift.combo = drift.combo + tick * kmh * (math.min(angle, ANGLE_MOST) / 45) * drift.mult
end

-- The player: scored on the car's own angle
local function scorePlayer(drift, car, tick)
  local angle = slipAngle(car)
  drift.angle = angle
  if angle > SPIN_ANGLE then
    bank(drift, true, 0)
  elseif angle >= MIN_ANGLE and car.speedKmh >= MIN_KMH then
    slide(drift, angle, car.speedKmh, tick)
  else
    drift.straight = drift.straight + tick
    if drift.straight >= COMBO_GAP then bank(drift, false, 0) end
  end
end

-- A ghost: scored at its skill's angle wherever it corners hard. Its sideways pull is its yaw rate (the car's own turn
-- about its up axis, so a crest or a dip is no corner, and no frame-to-frame noise) times its speed
local function scoreGhost(carIndex, drift, car, tick)
  if drift.skill == nil then drift.skill = skillOf(carIndex, car) end

  local yaw = car.localAngularVelocity and math.abs(car.localAngularVelocity.y) or 0
  local pull = yaw * car.velocity:length()
  if pull >= GHOST_CORNER_G and car.speedKmh >= MIN_KMH then
    -- A corner of its own: its angle drawn once as it turns in
    if not drift.corner then
      local spread = 1 + (math.random() * 2 - 1) * GHOST_SPREAD
      drift.corner = (GHOST_ANGLE_LOW + (GHOST_ANGLE_HIGH - GHOST_ANGLE_LOW) * drift.skill) * spread
    end
    drift.angle = drift.corner
    slide(drift, drift.corner, car.speedKmh, tick)
  else
    drift.corner = nil
    drift.angle = 0
    drift.straight = drift.straight + tick
    if drift.straight >= COMBO_GAP then bank(drift, false, carIndex) end
  end
end

function F.update(previousClock, tick)
  if not SC.clock() then return end
  for carIndex = 0, SC.count() - 1 do
    local data = SC.carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and data.drift and car and not SC.isPolice(carIndex) and not done(data) and not data.crashed and not data.brokeDown then
      local drift = data.drift
      if carIndex == 0 then scorePlayer(drift, car, tick) else scoreGhost(carIndex, drift, car, tick) end
      if car.lapCount > drift.count then
        drift.count = car.lapCount
        drift.laps = drift.laps + 1
        if done(data) then bank(drift, false, carIndex) end
      end
    end
  end
end

-- A touch loses the combo running
function F.onContact(carIndex)
  local data = SC.carData[carIndex]
  if data and data.drift and not done(data) then bank(data.drift, true, carIndex) end
end

-- What a racer scored: banked, and for one still on the course, stretched to the whole of it
local function scoreOf(data, carIndex)
  if not data or not data.drift then return nil end
  local drift = data.drift
  local score = drift.score + drift.combo
  if not done(data) and not data.crashed and not data.brokeDown and not data.disqualified then
    local car = ac.getCar(carIndex)
    local run = drift.laps + (car and car.splinePosition or 0)
    if run > 0.2 then score = score * SC.laps / run end
  end
  return math.floor(score)
end

local function playerWins()
  local player, rival = scoreOf(SC.carData[0], 0) or 0, scoreOf(SC.carData[1], 1)
  return rival == nil or player > rival
end

local function finish()
  local won = playerWins()
  ac.log(string.format('[Street Corsa] Drift battle over: player %s, rival %s: %s', tostring(scoreOf(SC.carData[0], 0)),
    tostring(scoreOf(SC.carData[1], 1)), won and 'WON' or 'LOST'))
  SC.endSession('FINISHED', won)
end

local function finishInThePits()
  local player = SC.carData[0]
  if not player or not done(player) then return false end
  if SC.isActive() then finish() end
  return true
end

function F.onJumped(carIndex)
  return carIndex == 0 and finishInThePits()
end

function F.onMenu()
  finishInThePits()
end

function F.checkFinish()
  local player, rival = SC.carData[0], SC.carData[1]
  if not player or not done(player) then return end
  local now = SC.clock() or 0
  if not playerDoneAt then
    playerDoneAt = now
    if rival and not done(rival) and not rival.crashed and not rival.brokeDown then
      pcall(ac.setMessage, SC.say('DRIFT_TITLE', 'Drift'), SC.say('DRIFT_WAIT', '{driver} is still on the course.', { driver = rival.driverName }))
    end
  end
  local rivalOver = not rival or done(rival) or rival.crashed or rival.brokeDown or rival.disqualified
  if not rivalOver and now - playerDoneAt < RIVAL_WAIT_SECONDS then return end
  finish()
end

function F.resultNote(won)
  local rival = SC.carData[1]
  return '\n' .. SC.say('DRIFT_RESULT', 'You scored {score}, {driver} {rival}', {
    score = scoreOf(SC.carData[0], 0) or 0,
    driver = rival and rival.driverName or '',
    rival = scoreOf(rival, 1) or 0
  })
end

function F.finalPosition(carIndex)
  if not SC.carData[0] or not SC.carData[0].drift then return nil end
  return (carIndex == 0) == playerWins() and 1 or 2
end

-- The score in the result: points, higher wins; the best combo goes with it
function F.participant(data, entry)
  if not data.drift then return end
  local index = data == SC.carData[0] and 0 or 1
  entry.score = scoreOf(data, index)
  entry.best_combo = math.floor(data.drift.best)
end

local DRAW = { from = vec2(), to = vec2(), ALIGN = vec2(0, 0) }

-- The score board, top centre: the score, the combo running and its multiplier, the angle, the rival's score
function F.drawUI(size)
  local player, rival = SC.carData[0], SC.carData[1]
  if not player or not player.drift or not SC.clock() then return end
  local drift, say = player.drift, SC.say
  local lines = {
    string.format('%-6s %d', say('DRIFT_HUD_SCORE', 'SCORE'), math.floor(drift.score)),
    string.format('%-6s %d  x%.2f', say('DRIFT_HUD_COMBO', 'COMBO'), math.floor(drift.combo), drift.mult),
    string.format('%-6s %d', say('DRIFT_HUD_ANGLE', 'ANGLE'), math.floor(drift.angle + 0.5)),
  }
  if rival and rival.drift then
    lines[#lines + 1] = string.format('%-6s %d', say('DRIFT_HUD_RIVAL', 'RIVAL'), math.floor(rival.drift.score + rival.drift.combo))
  end
  local width, height = 240, 20 + #lines * 22
  local left = (size.x - width) / 2
  ui.drawRectFilled(DRAW.from:set(left, 20), DRAW.to:set(left + width, 20 + height), SC.COLOR.BOX, 8)
  ui.pushFont(ui.Font.Monospace)
  ui.drawTextClipped(table.concat(lines, '\n'), DRAW.from:set(left + 14, 30), DRAW.to:set(left + width - 14, 20 + height - 6),
    SC.COLOR.TEXT, DRAW.ALIGN)
  ui.popFont()
end

return F
