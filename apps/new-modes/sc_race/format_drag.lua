--[[
  Street Corsa race mode: the drag strip (race.ini [STREET_CORSA] MODULE=format_drag)

  - A drag race: whoever hits the other car out of their own lane is disqualified (crossing lanes alone is fine:
    nobody judges lanes on the street). AC's own start lights give the green. Each car gets a timeslip.
  - A bracket race (race.ini [STREET_CORSA] DIAL_IN=player,rival): each lane gets its own tree, the slower dial-in
    first by the difference, and the race is to the quarter mile. Leaving before your green is a red light (a false
    start), and running quicker than your dial-in is a breakout, which loses unless the other car broke out by more.
    The rival takes the stripe: near the end it lifts when its pace would beat its dial-in.
  - Test-and-tune (RACE_TYPE=TUNE): the player alone on the strip, pass after pass, each with its own tree and
    timeslip. After a pass the car is put back on the line. The session ends after TUNE_PASSES passes, a crash or a
    breakdown, or when the player goes to the pits; the result is written after every pass, so closing AC keeps them.

  A format module of the race mode (mode.lua): the mode loads it, hands it the core (SC) in init, and calls its hooks.
  See format_road.lua for the hooks every module may have.
]]

local F = {}
local SC = nil

-- A drag strip's marks, in metres from the line. The trap speed is the average over the last 66 ft before a mark,
-- as the strips time it.
local FEET = 0.3048
local STAGE_METRES = 0.2
local TRAP_METRES = 66 * FEET
local MARKS = {
  { key = 'sixty_ft_s', metres = 60 * FEET },
  { key = 'three_thirty_ft_s', metres = 330 * FEET },
  { key = 'eighth_trap', metres = 660 * FEET - TRAP_METRES, hidden = true },
  { key = 'eighth_mile_s', metres = 660 * FEET, trap = 'eighth_trap', speed = 'eighth_mile_mph' },
  { key = 'thousand_ft_s', metres = 1000 * FEET },
  { key = 'quarter_trap', metres = 1320 * FEET - TRAP_METRES, hidden = true },
  { key = 'quarter_mile_s', metres = 1320 * FEET, trap = 'quarter_trap', speed = 'quarter_mile_mph' }
}

-- The strip's own rules:
-- QUARTER: the quarter mile, in metres
-- TREE_STEP, TREE_AMBERS: the tree, a sportsman's: three ambers half a second apart, the green half a second after
--   the last
-- BRACKET_LEAD, BRACKET_WAIT: a bracket race's slower lane's tree starts this long after AC's start, and the race is
--   over once both cars have crossed the quarter, one of them is out, or this long after the player crossed it
-- STRIPE_FROM: the rival takes the stripe from this share of the quarter on
-- TUNE_PASSES: the passes a test-and-tune has unless race.ini says otherwise; TUNE_SLIP: how long the slip shows before
--   the car is put back on the line; TUNE_STAGE: how long it is held there before the tree comes on. A pass is over
--   once the car is past the quarter and down to TUNE_DONE_KMH, has stood still for TUNE_STOPPED seconds after
--   leaving, or after TUNE_PASS_SECONDS.
local STRIP = {
  QUARTER = 1320 * FEET,
  TREE_STEP = 0.5, TREE_AMBERS = 3,
  BRACKET_LEAD = 2.0, BRACKET_WAIT = 15,
  STRIPE_FROM = 0.5,
  TUNE_PASSES = 6, TUNE_SLIP = 6, TUNE_STAGE = 3, TUNE_DONE_KMH = 20, TUNE_STOPPED = 3, TUNE_PASS_SECONDS = 60
}

-- A bracket race, nil in any other: dialIn by car index; decided, playerWon once it is; playerCrossedAt, the race
-- clock when the player crossed the quarter; rivalReaction, rivalMargin: how the rival leaves and takes the stripe
local bracket = nil

-- Test-and-tune, nil in any other race: passes (the slips so far), maxPasses; state WAIT (for AC's start), STAGE
-- (held on the line), TREE, RUN (the pass), SLIP (the slip shows) or OVER (the session ended); since, the race clock
-- when the state began; stoppedFor, seconds standing still in a pass; teleportedAt, the session time the car was last
-- put back
local tune = nil

-- The lanes: the sideways axis of the strip, from the player's car on the line. Each car's own lane is where it
-- stood (data.laneStart).
local stripSide = nil

-- ...and the axis down the strip, for the timeslips
local stripForward = nil

-- Each car's run down the strip: the race clock when the car got its green (AC's start but in a bracket race and on a
-- test-and-tune pass) and when it left the line, how far down the strip it was last frame, and the time at each mark.
-- stagedAt: where a test-and-tune pass starts, metres past laneStart (the car is not always put back exactly on it).
-- released: a bracket race's rival is held until its green. laneStart: where it stood on the line.
function F.newCar(data)
  data.laneStart = nil
  data.greenAt = 0
  data.leftAt = nil
  data.stagedAt = 0
  data.lastMetres = 0
  data.slip = {}
  data.released = true
end

-- A car's quarter-mile crossing on the race clock and its elapsed time, or nil before it crossed
local function quarterOf(data)
  local et = data.slip.quarter_mile_s
  if not et or not data.leftAt then return nil end
  return data.leftAt + et, et
end

-- The same as the slip has it (timeslipOf), to the millisecond: green, plus reaction, plus elapsed time. A bracket
-- race is decided on these, the numbers the career decides it on again (BracketRules.Decide, which adds them up in
-- the same order), so the two never disagree over a car a fraction of a millisecond either side of its dial-in.
local function slipQuarterOf(data)
  local _, et = quarterOf(data)
  if not et then return nil end
  local round = SC.round
  local green = data.greenAt ~= 0 and round(data.greenAt, 3) or 0
  local elapsed = round(et, 3)
  return green + round(data.leftAt - data.greenAt, 3) + elapsed, elapsed
end

-- The timeslip as the result has it; nil for a car that never left the line. The reaction is from the car's own
-- green; green_s says when that was on the race clock, where it was not AC's start.
local function timeslipOf(data)
  if not data.leftAt then return nil end
  local round = SC.round
  local slip = { reaction_s = round(data.leftAt - data.greenAt, 3) }
  if data.greenAt ~= 0 then slip.green_s = round(data.greenAt, 3) end
  if data.redLight then slip.red_light = true end
  for _, mark in ipairs(MARKS) do
    if not mark.hidden then slip[mark.key] = round(data.slip[mark.key], 3) end
    if mark.speed then slip[mark.speed] = round(data.slip[mark.speed], 2) end
  end
  return slip
end

-- The best pass of a test-and-tune so far (the quickest quarter), or nil
local function bestPass()
  local best = nil
  for _, pass in ipairs(tune and tune.passes or {}) do
    if pass.quarter_mile_s and (not best or pass.quarter_mile_s < best.quarter_mile_s) then best = pass end
  end
  return best
end

-- A test-and-tune pass that is over, added to the passes; false when the car never left the line
local function recordPass(data)
  local slip = timeslipOf(data)
  if not slip then return false end
  slip.pass = #tune.passes + 1
  tune.passes[#tune.passes + 1] = slip
  tune.last = slip
  ac.log(string.format('[Street Corsa] Pass %d: R/T %s, 1/4 %s', slip.pass, tostring(slip.reaction_s), tostring(slip.quarter_mile_s)))
  return true
end

-- A bracket race, from race.ini [STREET_CORSA] DIAL_IN=player,rival: each lane's green on the race clock, the slower
-- dial-in's tree first by the difference, and the rival held until its own. BRACKET_RIVAL=reaction,margin: how long
-- the rival takes to leave after its green, and how far over its dial-in it aims when it takes the stripe.
local function readBracket()
  local carData = SC.carData
  local dials = SC.readNumbers('DIAL_IN', 2)
  if not dials or SC.count() < 2 then return end
  local player, rival = dials[1], dials[2]
  if player <= 0 or rival <= 0 then return end
  local slower = math.max(player, rival)
  local lead = STRIP.BRACKET_LEAD + STRIP.TREE_AMBERS * STRIP.TREE_STEP
  local rivalWay = SC.readNumbers('BRACKET_RIVAL', 2)
  bracket = { dialIn = { [0] = player, [1] = rival }, decided = false, playerWon = nil, playerCrossedAt = nil,
    rivalReaction = rivalWay and math.max(0, math.min(1, rivalWay[1])) or 0.15,
    rivalMargin = rivalWay and math.max(0, math.min(0.5, rivalWay[2])) or 0.05 }
  carData[0].greenAt = lead + slower - player
  carData[1].greenAt = lead + slower - rival
  carData[1].released = false
  ac.log(string.format('[Street Corsa] Bracket race: dial-ins %.2f and %.2f, greens at %.2f and %.2f s, the rival leaves %.2f s after its own',
    player, rival, carData[0].greenAt, carData[1].greenAt, bracket.rivalReaction))
end

-- Test-and-tune, from race.ini [STREET_CORSA] TUNE_PASSES: how many passes the session has
local function readTune()
  local passes = SC.readNumbers('TUNE_PASSES', 1)
  local count = passes and math.floor(passes[1]) or STRIP.TUNE_PASSES
  tune = { passes = {}, maxPasses = math.max(1, math.min(20, count)), state = 'WAIT', since = 0, stoppedFor = 0,
    teleportedAt = nil }
  ac.log(string.format('[Street Corsa] Test-and-tune: up to %d passes', tune.maxPasses))
end

-- At the start: the lanes from where the cars stand, then the bracket or the test-and-tune
function F.init(core)
  SC = core
  local sim = ac.getSim()
  local player = ac.getCar(0)
  if player and not sim.isSessionStarted then
    -- Each car's lane is the line it stands on
    for i = 0, SC.count() - 1 do
      local car = ac.getCar(i)
      if car then SC.carData[i].laneStart = vec3():set(car.position) end
    end
    local look = vec3(player.look.x, 0, player.look.z):normalize()
    stripSide = vec3(-look.z, 0, look.x)
    stripForward = look
  end
  if not stripForward then return end
  if SC.raceType() == 'TUNE' then
    local ok, err = pcall(readTune)
    if not ok then ac.log('[Street Corsa] Could not read the test-and-tune: ' .. tostring(err)) end
  else
    local ok, err = pcall(readBracket)
    if not ok then ac.log('[Street Corsa] Could not read the bracket: ' .. tostring(err)) end
  end
end

-- A test-and-tune has no false start before AC's green: the mode gives its own
function F.checksFalseStart() return tune == nil end

-- Past the line, where nothing counts any more: AC's line, the quarter in a bracket race, never on a test-and-tune
function F.pastTheLine(data)
  if tune then return false end
  if bracket then return quarterOf(data) ~= nil end
  return data.lapsCompleted >= SC.laps
end

-- When a car went past a mark between the last frame and this one, on the race clock; nil when it did not
local function crossed(mark, data, metres, previousClock, tick)
  if data.lastMetres >= mark or metres < mark then return nil end
  local share = (mark - data.lastMetres) / math.max(metres - data.lastMetres, 1e-6)
  return previousClock + share * tick
end

-- Where a car is from its lane start, made once: this runs for every car every frame
local _fromStart = vec3()

-- The timeslips: each car's time at every mark down the strip, from the moment it left the line. The moment a mark is
-- passed is found between two frames, from where the car was on each side of it. The race clock runs from AC's start;
-- previousClock is where it stood a tick ago. A car that leaves before its own green (a bracket race, a test-and-tune
-- pass) has red-lit.
local function updateTimeslips(previousClock, tick)
  if not stripForward or not ac.getSim().isSessionStarted then return end
  if tick <= 0 then return end

  local carData = SC.carData
  for carIndex = 0, SC.count() - 1 do
    local data = carData[carIndex]
    local car = ac.getCar(carIndex)
    if data and car and data.laneStart then
      local metres = _fromStart:set(car.position):sub(data.laneStart):dot(stripForward) - data.stagedAt
      local timing = not tune or tune.state == 'TREE' or tune.state == 'RUN'

      if timing and not data.leftAt then
        data.leftAt = crossed(STAGE_METRES, data, metres, previousClock, tick)
        if data.leftAt and data.leftAt < data.greenAt then
          data.redLight = true
          ac.log(string.format('[Street Corsa] Car %d RED LIGHT: left %.3f s before its green', carIndex, data.greenAt - data.leftAt))
        end
      end
      if timing and data.leftAt then
        for _, mark in ipairs(MARKS) do
          if data.slip[mark.key] == nil then
            local at = crossed(mark.metres, data, metres, previousClock, tick)
            if at then
              data.slip[mark.key] = at - data.leftAt
              if mark.trap and data.slip[mark.trap] then
                local seconds = data.slip[mark.key] - data.slip[mark.trap]
                -- m/s to mph
                if seconds > 0 then data.slip[mark.speed] = TRAP_METRES / seconds * 2.2369363 end
              end
            end
          end
        end
      end

      data.lastMetres = metres
    end
  end
end

-- Whether the player wins a bracket race, from each car's quarter (at: crossed on the race clock, et) and dial-in;
-- the rival's quarter is nil when it never got there. A breakout loses, unless the other car broke out by more; with
-- neither, the first car to the quarter wins. The career decides the same way from the slips (BracketRules).
local function bracketPlayerWins(playerAt, playerEt, rivalAt, rivalEt)
  local playerOver = playerEt < bracket.dialIn[0]
  if not rivalAt then return not playerOver end
  local rivalOver = rivalEt < bracket.dialIn[1]
  if playerOver and rivalOver then return bracket.dialIn[0] - playerEt < bracket.dialIn[1] - rivalEt end
  if playerOver ~= rivalOver then return rivalOver end
  return playerAt < rivalAt
end

-- Every frame of a bracket race: the rival held until its green and let go a moment after it; near the end it lifts
-- when its pace would beat its dial-in; a player who left before their green has red-lit; the race is over once
-- both cars have crossed the quarter, the rival is out, or a while after the player crossed it.
local function updateBracket()
  if not bracket then return end
  local player, rival = SC.carData[0], SC.carData[1]
  if not player or not rival then return end
  local raceClock = SC.clock()
  if not ac.getSim().isSessionStarted or not raceClock then
    if not rival.released then SC.holdAI(1) end
    return
  end

  if player.redLight then
    player.falseStart = true
    SC.endSession('FALSE_START')
    return
  end

  -- AC's own lights go green for both lanes at its start: the player's green is their own tree's (CSP has no way to
  -- hide AC's lights)
  if not bracket.told then
    bracket.told = true
    pcall(ac.setMessage, SC.say('BRACKET_TITLE', 'Bracket race'), SC.say('BRACKET_WAIT', 'Not yet! Wait for your own tree, on the right.'))
  end

  if not rival.released then
    if raceClock >= rival.greenAt + bracket.rivalReaction and not rival.crashed and not rival.brokeDown then
      rival.released = true
      pcall(physics.setAIThrottleLimit, 1, 1)
      pcall(physics.setAIStopCounter, 1, 0)
      pcall(physics.awakeCar, 1)
      pcall(physics.engageGear, 1, 1)
      ac.log(string.format('[Street Corsa] Green for car 1 at %.2f s, away at %.2f s', rival.greenAt, raceClock))
    else
      SC.holdAI(1)
    end
  elseif rival.leftAt and not quarterOf(rival) and not rival.crashed and not rival.brokeDown and not rival.disqualified then
    -- Taking the stripe: from part way down, a car going faster than the pace that gets it to the quarter on its
    -- dial-in (and the rival's margin) is held to that pace, AC's AI braking down to it. Only ever slower: a car that
    -- can't make its dial-in goes flat out.
    local car = ac.getCar(1)
    local speed = car and car.speedKmh / 3.6 or 0
    local top = 1e9
    if car and rival.lastMetres > STRIP.STRIPE_FROM * STRIP.QUARTER then
      local timeLeft = bracket.dialIn[1] + bracket.rivalMargin - (raceClock - rival.leftAt)
      local pace = timeLeft > 0 and (STRIP.QUARTER - rival.lastMetres) / timeLeft or math.huge
      if pace < speed then top = math.max(30, pace * 3.6) end
    end
    pcall(physics.setAITopSpeed, 1, top)
  end

  local playerAt, playerEt = slipQuarterOf(player)
  if not playerAt then return end
  bracket.playerCrossedAt = bracket.playerCrossedAt or raceClock
  local rivalAt, rivalEt = slipQuarterOf(rival)
  local rivalOut = rival.crashed or rival.brokeDown or rival.disqualified
  if not rivalAt and not rivalOut and raceClock - bracket.playerCrossedAt < STRIP.BRACKET_WAIT then return end

  bracket.decided = true
  bracket.playerWon = rivalOut or bracketPlayerWins(playerAt, playerEt, rivalAt, rivalEt)
  ac.log(string.format('[Street Corsa] Bracket race over: player %.3f on %.2f, rival %s on %.2f: %s', playerEt,
    bracket.dialIn[0], rivalEt and string.format('%.3f', rivalEt) or 'no time', bracket.dialIn[1],
    bracket.playerWon and 'WON' or 'LOST'))
  SC.endSession('FINISHED', bracket.playerWon)
end

-- Puts the player's car back on its line, stopped, facing down the strip
local function backToTheLine()
  local data = SC.carData[0]
  -- Marked first: AC may call onCarJumped from inside setCarPosition, and this jump is the mode's own
  tune.teleportedAt = SC.duration()
  pcall(physics.setCarVelocity, 0, vec3())
  -- setCarPosition takes the opposite of the car's look (Test Drive's parkPlayer)
  local ok, err = pcall(physics.setCarPosition, 0, data.laneStart, -stripForward)
  if not ok then ac.log('[Street Corsa] Could not put the car back on the line: ' .. tostring(err)) end
  data.grace = SC.TELEPORT_GRACE_SECONDS
end

-- Every frame of a test-and-tune: the pass's states one after the other, and the result written after each pass
local function updateTune(tick)
  if not tune then return end
  local data = SC.carData[0]
  local car = ac.getCar(0)
  if not data or not car then return end

  if not ac.getSim().isSessionStarted then
    -- Held until AC's start: the first tree is the mode's own
    pcall(physics.forceUserBrakesFor, 0.25, 1.0)
    return
  end
  local now = SC.clock() or 0
  local state = tune.state

  if state == 'WAIT' or state == 'STAGE' then
    pcall(physics.forceUserBrakesFor, 0.25, 1.0)
    if state == 'WAIT' then
      tune.state, tune.since = 'STAGE', now
      pcall(ac.setMessage, SC.say('TUNE_TITLE', 'Test and tune'),
        SC.say('TUNE_STAGE', 'Pass {pass} of {passes}: stage up', { pass = #tune.passes + 1, passes = tune.maxPasses }))
    elseif now - tune.since >= STRIP.TUNE_STAGE then
      -- The brakes come off at the first amber: from here the player holds the car
      -- The pass is measured from where the car stands: put back short of or past its line, it still stages
      data.leftAt, data.slip, data.redLight = nil, {}, false
      data.stagedAt = (car.position - data.laneStart):dot(stripForward)
      data.lastMetres = 0
      data.greenAt = now + STRIP.TREE_AMBERS * STRIP.TREE_STEP
      tune.state, tune.since, tune.stoppedFor = 'TREE', now, 0
      pcall(physics.forceUserBrakesFor, 0, 0)
      ac.log(string.format('[Street Corsa] Pass %d: tree on, green at %.2f s', #tune.passes + 1, data.greenAt))
    end
    return
  end

  if state == 'TREE' or state == 'RUN' then
    if state == 'TREE' and now >= data.greenAt then tune.state = 'RUN' end
    if data.leftAt and car.speedKmh < 3 then tune.stoppedFor = tune.stoppedFor + tick else tune.stoppedFor = 0 end
    local done = (quarterOf(data) and car.speedKmh < STRIP.TUNE_DONE_KMH)
      or (data.leftAt and tune.stoppedFor >= STRIP.TUNE_STOPPED)
      or now - data.greenAt >= STRIP.TUNE_PASS_SECONDS
    if not done then return end

    if recordPass(data) then
      -- Written after every pass: closing AC keeps what was run
      SC.writeNow('FINISHED')
    else
      tune.last = nil
    end
    if #tune.passes >= tune.maxPasses then
      tune.state = 'OVER'
      SC.endSession('FINISHED')
      return
    end
    tune.state, tune.since = 'SLIP', now
    return
  end

  if state == 'SLIP' and now - tune.since >= STRIP.TUNE_SLIP then
    backToTheLine()
    tune.state = 'WAIT'
  end
end

-- Every frame: the timeslips, then the bracket's own race or the test-and-tune's passes
function F.update(previousClock, tick)
  updateTimeslips(previousClock, tick)
  updateBracket()
  if SC.ended() then return end
  updateTune(tick)
end

-- A drag race heads up ends at AC's line, as a road race does; a bracket race is over at the quarter
-- (updateBracket), a test-and-tune when the player is done
function F.checkFinish()
  if not bracket and not tune then SC.finishAtTheLine() end
end

-- Back on the pits menu on a test-and-tune is leaving the strip, with the passes run so far
function F.onMenu()
  if tune then SC.endSession('FINISHED') end
end

-- AC put the player's car back: on a test-and-tune the mode does that after each pass; anything else is the player
-- going to the pits, done with the strip. True when the module took care of it.
function F.onJumped(carIndex, data)
  if not tune then return false end
  if tune.teleportedAt and SC.duration() - tune.teleportedAt < SC.TELEPORT_GRACE_SECONDS then return true end
  if SC.isActive() then
    ac.log('[Street Corsa] Put back to the pits: the strip is done')
    SC.endSession('FINISHED')
  end
  return true
end

-- How far a car has moved out of its own lane towards the other car's, in metres (negative: away from it)
local function intrusion(data, otherData, car)
  local towards = (otherData.laneStart - data.laneStart):dot(stripSide)
  local drift = (car.position - data.laneStart):dot(stripSide)
  return towards >= 0 and drift or -drift
end

-- Between the green and the line: the two cars touched. The one further out of its own lane hit the other and is
-- disqualified. The player's disqualification ends the race; a disqualified rival is stopped where it is, and the
-- player finishes to win.
local function judgeContact(carIndex, otherIndex)
  local carData = SC.carData
  local data, otherData = carData[carIndex], carData[otherIndex]
  if not data or not otherData or not data.laneStart or not otherData.laneStart then return end
  if data.disqualified or otherData.disqualified then return end
  if F.pastTheLine(data) or F.pastTheLine(otherData) then return end

  local car, other = ac.getCar(carIndex), ac.getCar(otherIndex)
  if not car or not other then return end
  local mine, theirs = intrusion(data, otherData, car), intrusion(otherData, data, other)
  local guiltyIndex = mine >= theirs and carIndex or otherIndex
  local guilty = carData[guiltyIndex]
  guilty.disqualified = true
  ac.log(string.format('[Street Corsa] Contact between cars %d and %d (%.1f m and %.1f m out of their lanes): car %d DISQUALIFIED',
    carIndex, otherIndex, mine, theirs, guiltyIndex))

  if guiltyIndex == 0 then
    SC.endSession('DISQUALIFIED')
  else
    SC.holdAI(guiltyIndex)
    pcall(ac.setMessage, SC.say('TITLE_DISQUALIFIED', 'Disqualified'),
      SC.say('RIVAL_DISQUALIFIED', '{driver} hit you. Finish the race and it is yours!', { driver = guilty.driverName }))
  end
end

-- Car against car: somebody left their lane
function F.onContact(carIndex)
  if SC.raceType() ~= 'DRAG' then return end
  local car = ac.getCar(carIndex)
  if not car or car.collidedWith == 0 then return end
  -- collidedWith is 0 for the track and non-zero for a car; with two cars the other one is the only one there is
  if SC.count() ~= 2 then return end
  judgeContact(carIndex, 1 - carIndex)
end

-- A test-and-tune pass under way when the session ends is one of the passes: the player may leave the strip still
-- rolling past the quarter
function F.beforeEnd(reason)
  if not tune then return end
  if (tune.state == 'TREE' or tune.state == 'RUN') and SC.carData[0] then recordPass(SC.carData[0]) end
  tune.state = 'OVER'
end

-- What the player hears about the bracket race's result: their ET against their dial-in
local function bracketLine()
  local _, et = slipQuarterOf(SC.carData[0])
  if not bracket or not et then return '' end
  local dial = bracket.dialIn[0]
  local MSG = SC.MSG
  local values = { et = MSG.num('%.3f', et), dial = MSG.num('%.2f', dial) }
  return '\n' .. (et < dial and SC.say('RESULT_BRACKET_BROKE_OUT', 'Your {et} on a {dial} dial-in: BREAKOUT', values)
    or SC.say('RESULT_BRACKET', 'Your {et} on a {dial} dial-in', values))
end

-- The result card of a race that ran its course: a test-and-tune's summary (title, message, and the player held), or
-- nil for the plain race over, with the bracket's line under it (resultNote)
function F.endMessage(reason, won)
  if not tune then return nil end
  local MSG = SC.MSG
  local best = bestPass()
  local title = SC.say('TITLE_STRIP_CLOSED', "Strip Closed")
  local message = #tune.passes == 1 and SC.say('TUNE_PASSES_ONE', '{passes} pass on the strip.', { passes = #tune.passes })
    or SC.say('TUNE_PASSES_OTHER', '{passes} passes on the strip.', { passes = #tune.passes })
  if best then
    message = message .. '\n' .. SC.say('TUNE_BEST', 'Best: {et} @ {speed}',
      { et = MSG.num('%.3f', best.quarter_mile_s), speed = MSG.speed(best.quarter_mile_mph or 0),
        mph = MSG.num('%.2f', best.quarter_mile_mph or 0) })
  end
  return title, message, true
end

function F.resultNote(won) return bracketLine() end

-- A bracket race is won at the quarter by its own rules, whoever led AC's race
function F.finalPosition(carIndex, data)
  if bracket and bracket.decided then return (carIndex == 0) == bracket.playerWon and 1 or 2 end
  return nil
end

-- What the result says of a car on the strip: its timeslip, a bracket race's dial-in and breakout, a test-and-tune's
-- passes (and the best of them as its timeslip)
function F.participant(data, entry)
  local round = SC.round
  if bracket and bracket.dialIn[data.carIndex] then
    entry.dial_in_s = round(bracket.dialIn[data.carIndex], 3)
    local _, et = slipQuarterOf(data)
    entry.breakout = et ~= nil and et < bracket.dialIn[data.carIndex]
  end
  entry.timeslip = timeslipOf(data)
  if tune and data.carIndex == 0 then
    entry.passes = tune.passes
    entry.timeslip = bestPass()
  end
end

-- What the overlays draw with: scratch points for the tree and the slip, which draw every frame (set just before each
-- call that takes them, which reads them there and then; never kept)
local LAMP = { OFF = rgbm(0.15, 0.15, 0.15, 1), AMBER = rgbm(1, 0.65, 0, 1), GREEN = rgbm(0.1, 1, 0.2, 1),
  RED = rgbm(1, 0.1, 0.1, 1), RADIUS = 16 }
local DRAW = { ALIGN_TOP_LEFT = vec2(0, 0), ALIGN_TOP_CENTRE = vec2(0.5, 0), from = vec2(), to = vec2(), box = vec2(), topLeft = vec2() }

-- The player's own tree, when the mode gives the green (a bracket race, a test-and-tune pass): three ambers coming on
-- half a second apart, then the green, or the red of a car that left early. Shown from a second before the first
-- amber until two seconds after the green.
local function drawTree(size)
  local data = SC.carData[0]
  local raceClock = SC.clock()
  if not data or not raceClock or not (bracket or (tune and (tune.state == 'TREE' or tune.state == 'RUN'))) then return end
  local firstAmber = data.greenAt - STRIP.TREE_AMBERS * STRIP.TREE_STEP
  if raceClock < firstAmber - 1 or raceClock > data.greenAt + 2 then return end

  local x = size.x - 70
  local y = size.y * 0.3
  ui.drawRectFilled(DRAW.from:set(x - 30, y - 30), DRAW.to:set(x + 30, y + (STRIP.TREE_AMBERS + 1) * 44 + 4), SC.COLOR.BOX, 8)
  for n = 1, STRIP.TREE_AMBERS do
    local lit = raceClock >= firstAmber + (n - 1) * STRIP.TREE_STEP and raceClock < data.greenAt
    ui.drawCircleFilled(DRAW.from:set(x, y + (n - 1) * 44), LAMP.RADIUS, lit and LAMP.AMBER or LAMP.OFF, 24)
  end
  local bottom = DRAW.from:set(x, y + STRIP.TREE_AMBERS * 44)
  if data.redLight then
    ui.drawCircleFilled(bottom, LAMP.RADIUS, LAMP.RED, 24)
  else
    ui.drawCircleFilled(bottom, LAMP.RADIUS, raceClock >= data.greenAt and LAMP.GREEN or LAMP.OFF, 24)
  end
  if bracket then
    ui.pushFont(ui.Font.Main)
    ui.drawTextClipped(SC.say('TREE_DIAL_IN', 'Dial-in\n{dial}', { dial = SC.MSG.num('%.2f', bracket.dialIn[0]) }), DRAW.from:set(x - 60, y + (STRIP.TREE_AMBERS + 1) * 44 + 8),
      DRAW.to:set(x + 30, y + (STRIP.TREE_AMBERS + 1) * 44 + 60), SC.COLOR.TEXT, DRAW.ALIGN_TOP_CENTRE)
    ui.popFont()
  end
end

-- A test-and-tune pass's slip, while the car waits to go back to the line
local function drawPassSlip(size)
  if not tune or tune.state ~= 'SLIP' then return end
  local say, MSG = SC.say, SC.MSG
  local slip = tune.last
  local lines = {}
  if slip then
    local pass = { pass = slip.pass, passes = tune.maxPasses }
    lines[#lines + 1] = slip.red_light and say('SLIP_PASS_RED_LIGHT', 'Pass {pass} of {passes}   RED LIGHT', pass)
      or say('SLIP_PASS', 'Pass {pass} of {passes}', pass)
    local function mark(label, value, format) lines[#lines + 1] = string.format('%-8s %s', label, value and MSG.num(format, value) or '-') end
    mark(say('SLIP_REACTION', 'R/T'), slip.reaction_s, '%.3f')
    local unit = { unit = MSG.speedName() }
    mark(say('SLIP_SIXTY', "60'"), slip.sixty_ft_s, '%.3f')
    mark(say('SLIP_THREE_THIRTY', "330'"), slip.three_thirty_ft_s, '%.3f')
    mark(say('SLIP_EIGHTH', '1/8 ET'), slip.eighth_mile_s, '%.3f')
    mark(say('SLIP_EIGHTH_SPEED', '1/8 {unit}', unit), slip.eighth_mile_mph and slip.eighth_mile_mph * MSG.perMph(), '%.2f')
    mark(say('SLIP_QUARTER', '1/4 ET'), slip.quarter_mile_s, '%.3f')
    mark(say('SLIP_QUARTER_SPEED', '1/4 {unit}', unit), slip.quarter_mile_mph and slip.quarter_mile_mph * MSG.perMph(), '%.2f')
  else
    lines[#lines + 1] = say('SLIP_NO_TIME', 'No time: the car never left the line')
  end
  local left = math.max(0, math.ceil(STRIP.TUNE_SLIP - ((SC.clock() or 0) - tune.since)))
  lines[#lines + 1] = ''
  lines[#lines + 1] = say('SLIP_BACK', 'Back to the line in {seconds} s. Go to the pits to leave the strip.', { seconds = left })

  local box = DRAW.box:set(460, 40 + #lines * 22)
  local topLeft = DRAW.topLeft:set((size.x - box.x) / 2, size.y * 0.15)
  local bottomRight = DRAW.to:set(topLeft.x + box.x, topLeft.y + box.y)
  ui.drawRectFilled(topLeft, bottomRight, SC.COLOR.BOX, 10)
  ui.drawRect(topLeft, bottomRight, SC.COLOR.BORDER, 10, nil, 2)
  ui.pushFont(ui.Font.Monospace)
  ui.drawTextClipped(table.concat(lines, '\n'), DRAW.from:set(topLeft.x + 20, topLeft.y + 20),
    DRAW.to:set(topLeft.x + box.x - 20, topLeft.y + box.y - 20), SC.COLOR.TEXT, DRAW.ALIGN_TOP_LEFT)
  ui.popFont()
end

-- While the race runs: the tree and a test-and-tune's slip
function F.drawUI(size)
  pcall(drawTree, size)
  pcall(drawPassSlip, size)
end

return F
