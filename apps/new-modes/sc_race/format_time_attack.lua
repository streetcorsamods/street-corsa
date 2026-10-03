--[[
  Street Corsa race mode: a time attack (race.ini [STREET_CORSA] MODULE=format_time_attack). The player and the rival
  run as ghosts (neither can touch the other): a warm-up lap from the grid, then LAPS flying laps each. The best lap
  wins: it is each racer's score in the result (participants[].score, milliseconds; lower wins), and the career
  decides on it again (RaceFormat win "score").

  The race is over once the player has run their laps and the rival has too, is out of the race, or has had
  RIVAL_WAIT_SECONDS more to run them. AC's session is given more laps than that (the format's sessionLaps), so it
  never ends the race itself.

  A format module of the race mode: see format_road.lua for the hooks.
]]

local F = {}
local SC = nil

-- How long the race waits for the rival to finish its laps once the player has, in seconds
local RIVAL_WAIT_SECONDS = 90

-- How far AC's own lap time may be from the mode's for the mode to take AC's (it times the line to the physics step)
local AC_TIME_SLACK_MS = 500

-- When the player finished their laps, on the race clock
local playerDoneAt = nil

-- Each racer's laps: count, the lap count AC had last frame; startedAt, the race clock when the timed lap began (nil
-- on the warm-up lap); times, the timed laps in ms; best
function F.newCar(data)
  data.attack = { count = 0, startedAt = nil, times = {}, best = nil }
end

local function done(data)
  return data.attack ~= nil and #data.attack.times >= SC.laps
end

-- A lap time as the stopwatch shows it: 1:23.456, with the game's decimal mark
local function lapTime(ms)
  if not ms then return SC.say('TA_NO_TIME', 'no time') end
  local seconds = ms / 1000
  local minutes = math.floor(seconds / 60)
  return minutes .. ':' .. SC.MSG.num('%06.3f', seconds - minutes * 60)
end

-- Ghosts: no car can touch another, so nobody is pushed off and a slow car is never in the way
function F.init(core)
  SC = core
  for i = 0, SC.count() - 1 do
    if not SC.isPolice(i) then
      local ok, err = pcall(physics.disableCarCollisions, i, true)
      if not ok then ac.log('[Street Corsa] Could not make car ' .. i .. ' a ghost: ' .. tostring(err)) end
    end
  end
  ac.log(string.format('[Street Corsa] Time attack: a warm-up lap and %d timed lap(s)', SC.laps))
end

-- A racer's race is run once they have their laps
function F.pastTheLine(data)
  return done(data)
end

-- A lap crossed this frame: the warm-up lap starts the clock, a timed lap is taken (AC's time when it agrees)
local function crossed(carIndex, data, car, now)
  local attack = data.attack
  if not attack.startedAt then
    attack.startedAt = now
    if carIndex == 0 then
      pcall(ac.setMessage, SC.say('TA_TITLE', 'Time attack'),
        SC.say('TA_GO', 'The clock is running: {laps} flying laps, best one counts.', { laps = SC.laps }))
    end
    return
  end

  local ms = (now - attack.startedAt) * 1000
  local ac_ms = car.previousLapTimeMs
  if ac_ms and ac_ms > 0 and math.abs(ac_ms - ms) <= AC_TIME_SLACK_MS then ms = ac_ms end
  ms = SC.round(ms, 0)
  attack.times[#attack.times + 1] = ms
  if not attack.best or ms < attack.best then attack.best = ms end
  attack.startedAt = now
  ac.log(string.format('[Street Corsa] Car %d timed lap %d: %d ms (best %d)', carIndex, #attack.times, ms, attack.best))

  if carIndex == 0 then
    local values = { lap = #attack.times, laps = SC.laps, time = lapTime(ms), best = lapTime(attack.best) }
    pcall(ac.setMessage, SC.say('TA_LAP_TITLE', 'Lap {lap} of {laps}', values), SC.say('TA_LAP', '{time}   best {best}', values))
  end
end

-- Every frame after the green: each racer's laps, until they have them all
function F.update(previousClock, tick)
  local now = SC.clock()
  if not now then return end
  for carIndex = 0, SC.count() - 1 do
    local data = SC.carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and data.attack and car and not SC.isPolice(carIndex) then
      if car.lapCount > data.attack.count then
        data.attack.count = car.lapCount
        if not done(data) and not data.crashed and not data.brokeDown then crossed(carIndex, data, car, now) end
      end
    end
  end
end

-- Whether the player's best beats the rival's (a rival without one is beaten)
local function playerBeats(player, rival)
  if not player.best then return false end
  if not rival or not rival.best then return true end
  return player.best < rival.best
end

-- The player has their laps: the race is over once the rival has theirs, is out, or has had long enough
function F.checkFinish()
  local player, rival = SC.carData[0], SC.carData[1]
  if not player or not done(player) then return end

  local now = SC.clock() or 0
  if not playerDoneAt then
    playerDoneAt = now
    if rival and not done(rival) and not rival.crashed and not rival.brokeDown then
      pcall(ac.setMessage, SC.say('TA_TITLE', 'Time attack'),
        SC.say('TA_WAIT', '{driver} is still on the clock.', { driver = rival.driverName }))
    end
  end

  local rivalOver = not rival or done(rival) or rival.crashed or rival.brokeDown or rival.disqualified
  if not rivalOver and now - playerDoneAt < RIVAL_WAIT_SECONDS then return end

  local won = playerBeats(player.attack, rival and rival.attack)
  ac.log(string.format('[Street Corsa] Time attack over: player %s, rival %s: %s', tostring(player.attack.best),
    tostring(rival and rival.attack and rival.attack.best), won and 'WON' or 'LOST'))
  SC.endSession('FINISHED', won)
end

-- Under the race over card: both best laps
function F.resultNote(won)
  local player, rival = SC.carData[0], SC.carData[1]
  if not player or not player.attack then return '' end
  return '\n' .. SC.say('TA_RESULT', 'Your best lap {time}, {driver} {rival}', {
    time = lapTime(player.attack.best),
    driver = rival and rival.driverName or '',
    rival = lapTime(rival and rival.attack and rival.attack.best)
  })
end

-- The places go by the best laps, whoever led AC's race
function F.finalPosition(carIndex, data)
  local player, rival = SC.carData[0], SC.carData[1]
  if not player or not player.attack then return nil end
  local playerFirst = playerBeats(player.attack, rival and rival.attack)
  return (carIndex == 0) == playerFirst and 1 or 2
end

-- The score is the best lap, in ms; the laps go with it
function F.participant(data, entry)
  if not data.attack then return end
  entry.score = data.attack.best
  if #data.attack.times > 0 then entry.lap_times_ms = data.attack.times end
end

local DRAW = { from = vec2(), to = vec2(), ALIGN = vec2(0, 0) }

-- The stopwatch, top centre: the lap, the time running, the last lap, the best, and the rival's best
function F.drawUI(size)
  local player, rival = SC.carData[0], SC.carData[1]
  local now = SC.clock()
  if not player or not player.attack or not now then return end
  local attack = player.attack
  local say = SC.say

  local lines = {}
  if done(player) then
    lines[#lines + 1] = say('TA_HUD_DONE', 'LAPS DONE')
  elseif not attack.startedAt then
    lines[#lines + 1] = say('TA_HUD_WARM_UP', 'WARM-UP LAP')
  else
    lines[#lines + 1] = say('TA_HUD_LAP', 'LAP {lap}/{laps}', { lap = #attack.times + 1, laps = SC.laps })
    lines[#lines + 1] = string.format('%-6s %s', say('TA_HUD_TIME', 'TIME'), lapTime((now - attack.startedAt) * 1000))
  end
  lines[#lines + 1] = string.format('%-6s %s', say('TA_HUD_LAST', 'LAST'), attack.times[#attack.times] and lapTime(attack.times[#attack.times]) or '-')
  lines[#lines + 1] = string.format('%-6s %s', say('TA_HUD_BEST', 'BEST'), attack.best and lapTime(attack.best) or '-')
  if rival and rival.attack then
    lines[#lines + 1] = string.format('%-6s %s', say('TA_HUD_RIVAL', 'RIVAL'), rival.attack.best and lapTime(rival.attack.best) or '-')
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
