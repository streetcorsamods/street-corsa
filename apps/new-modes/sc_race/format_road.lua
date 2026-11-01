--[[
  Street Corsa race mode: a road race (race.ini [STREET_CORSA] MODULE=format_road): the player against one rival or a
  field of them (RIVALS) over the race's laps (LAPS), the police maybe after them. The first over the line wins; the
  player always gets to finish. In a field the player sees their place and how far the cars either side of them are.

  The template for a race format of a mod's own. The race mode (mode.lua) runs everything every race has: the start,
  false starts, crashes, breakdowns, the engine's heat and oil, the police, the result file and the result card. A
  format module is a Lua file next to it (format_<name>.lua; a mod ships it as formats/<name>.lua and the game puts it
  here) that returns a table of hooks, every one of them optional:

    init(SC)                     the race is set up: SC is the core (below)
    newCar(data)                 a car's data is made: add the format's own fields
    checksFalseStart()           false: the mode does not judge the start (a test-and-tune gives its own green)
    update(previousClock, tick)  every frame after the green: the race clock was previousClock a tick (seconds) ago
    checkFinish()                every frame, last: end the race here (SC.endSession('FINISHED', won))
    pastTheLine(data)            the car's race is run: crashes, breakdowns and the heat no longer count for it.
                                 Without it, a car is past the line once it has done the race's laps
    onContact(carIndex)          the car touched something (CSP's collision callback), after the green
    onJumped(carIndex, data)     AC put the player's car somewhere: true when the format took care of it (else a
                                 jump start before 20 m, the car out of the race after)
    onMenu()                     the player went back to the pits menu during the race
    beforeEnd(reason)            the session is ending, before the result is written
    endMessage(reason, won)      the result card of a race that ran its course: title, message, and true to hold the
                                 player's car; nil for the plain race over (with resultNote under it)
    resultNote(won)              a line or more under the race over card ('\n...')
    finalPosition(index, data)   the car's place in the result; nil leaves it to the mode (the order at the line)
    participant(data, entry)     add the format's own fields to the car's entry in the result (entry.score, for a
                                 format won on score; see docs/modding.md, formats)
    drawUI(size)                 draw on the screen while the race runs (ui.*)

  And one field: scored = true for a format won on score, not by finishing (no "finish the race and it is yours" when
  the rival breaks down). A hook that fails is told in the log once and the race goes on as if the format had none.
  A format with a field gives every racer a place of its own in finalPosition (1 to rivals() + 1), or none and the
  mode's order (standings) stands.

  SC, the core: carData (by car index; driverName, carName, lapsCompleted, bestLapTimeMs, crashed, brokeDown,
  disqualified, falseStart...), count() the cars, isPolice(index), rivals() how many rivals (cars 1..rivals()),
  isRacer(index), out(data) the car is out of the race (wrecked, broken down, disqualified, busted), rivalsOut() every
  rival is, progress(index) laps and the share of the lap a racer has done, standings() the racers' indices in their
  order (over the line, then running by progress, then out), placeOf(index), trackLength() in metres, clock() seconds
  since the green (nil before),
  duration() seconds of the session, laps the race's laps, raceType() DRAG, ROAD or TUNE, format the format's id,
  say(key, english, values) the race's words in the player's language (race.<key> in the mods' text/race.json),
  MSG (num, speed, perMph, speedName), readNumbers(key, count) numbers from race.ini [STREET_CORSA], round(value,
  places), endSession(reason, won), finishAtTheLine(), holdAI(index), holdPlayer(seconds), writeNow(reason) to write
  the result mid-session, isActive(), ended(), TELEPORT_GRACE_SECONDS, COLOR (BOX, BORDER, TEXT).
]]

local F = {}
local SC = nil

function F.init(core)
  SC = core
end

-- The race is run once the car has done its laps
function F.pastTheLine(data)
  return data.lapsCompleted >= SC.laps
end

-- The race is over when the player crosses the line, whoever got there first
function F.checkFinish()
  SC.finishAtTheLine()
end

local DRAW = { from = vec2(), to = vec2(), ALIGN = vec2(0, 0) }

-- How long the car behind takes to cover the road to the car ahead at its speed now, in seconds (a crawl counts as
-- MIN_GAP_SPEED m/s, so a gap at the green is not hours)
local MIN_GAP_SPEED = 10
local function gapSeconds(ahead, behind)
  local metres = (SC.progress(ahead) - SC.progress(behind)) * SC.trackLength()
  local car = ac.getCar(behind)
  return math.max(0, metres) / math.max(MIN_GAP_SPEED, car and car.speedKmh / 3.6 or 0)
end

-- A field, top centre: the player's place (P2/6) and the gap to the cars either side, along the road. One rival has
-- nothing to show: the race is one on one.
function F.drawUI(size)
  if SC.rivals() < 2 or not SC.clock() then return end
  local standings = SC.standings()
  local place = nil
  for position, index in ipairs(standings) do
    if index == 0 then place = position end
  end
  if not place then return end
  local say = SC.say
  local lines = { string.format('%-7s P%d/%d', say('ROAD_HUD_POSITION', 'POS'), place, #standings) }
  local ahead, behind = standings[place - 1], standings[place + 1]
  local function gapLine(key, english, other, seconds)
    local data = SC.carData[other]
    if not data or SC.out(data) then return end
    lines[#lines + 1] = string.format('%-7s %s  %s', say(key, english), say('ROAD_HUD_GAP', '{seconds} s',
      { seconds = SC.MSG.num('%.1f', seconds) }), data.driverName)
  end
  if ahead then gapLine('ROAD_HUD_AHEAD', 'AHEAD', ahead, gapSeconds(ahead, 0)) end
  if behind then gapLine('ROAD_HUD_BEHIND', 'BEHIND', behind, gapSeconds(0, behind)) end

  local width, height = 300, 20 + #lines * 22
  local left = (size.x - width) / 2
  ui.drawRectFilled(DRAW.from:set(left, 20), DRAW.to:set(left + width, 20 + height), SC.COLOR.BOX, 8)
  ui.pushFont(ui.Font.Monospace)
  ui.drawTextClipped(table.concat(lines, '\n'), DRAW.from:set(left + 14, 30), DRAW.to:set(left + width - 14, 20 + height - 6),
    SC.COLOR.TEXT, DRAW.ALIGN)
  ui.popFont()
end

return F
