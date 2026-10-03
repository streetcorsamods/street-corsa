--[[
  Street Corsa race mode: a road race (race.ini [STREET_CORSA] MODULE=format_road): the player against one rival over
  the race's laps (LAPS), the police maybe after them both. The first over the line wins; the player always gets to
  finish.

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

  SC, the core: carData (by car index; driverName, carName, lapsCompleted, bestLapTimeMs, crashed, brokeDown,
  disqualified, falseStart...), count() the cars, isPolice(index), clock() seconds since the green (nil before),
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

return F
