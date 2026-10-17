# Street Corsa roadmap

The game's official name is **Street Corsa** (decided 2026-09-24, and everything renamed in step 15: the repo, the
project and namespaces (`StreetCorsa`), the AppData folder (`%AppData%\StreetCorsa`) and the race mode (`sc_race`)).
"Street Rod" alone still means the original game this one follows.

Written 2026-09-24 after an analysis of the project and the user's answers (their original notes:
`docs/next_steps.txt`, untracked). Each step below lists what the user decided, what the research found, and the
design. Steps are meant to be vertical slices, one PR each, to `dev` (remote `github`; PRs are squash-merged).

## Where things stand

**Done: step 1, the race mode** (PR #17, merged as `a83683e`). See `docs/ac-integration/csp-lua-scripts.md`.
- **The race script is a CSP mode.** `apps/new-modes/sc_race` (was the Lua app `apps/lua/sr_race_manager`) runs with `ALLOW_PHYSICS_ALTERATIONS=1`, which grants the physics API. `ScRaceMode` installs it into the AC install before every race.
- **`race.ini` selects it** with `[RACE] __CM_CUSTOM_MODE=sc_race`. `assists.ini` gets `DAMAGE=100` and `TYRE_WEAR=1` for the race, and both files are restored afterwards.
- **Every race, drag races too, is a one-lap race session (`TYPE=3`).**
  - `JUMP_START_PENALTY=0`, pit teleports blocked, car recovery off: AC never moves a car.
  - `[STREET_CORSA] RACE_TYPE=DRAG|ROAD` tells the mode which kind of race it is.
- **Crash:** a collision with a change of velocity of 15 g or more over one frame.
  - A crashed player is stopped and the race ends.
  - A crashed rival is held where it stopped.
  - Past the finish line nothing counts.
  - A teleport is not a crash.
- **False start:** moving more than 1 m before the green. No contest, nothing changes hands, and each one costs 3 reputation (up to 15).
- **Drag race contact:** crossing lanes is fine. When the cars touch, the car further out of its own lane is disqualified: the player's disqualification is a loss, the rival's a win.
- **The player always gets to finish.** Quitting AC is a forfeit.
- **Result schema 1.3:** `end_reason`, `race_type`, `false_start`, `disqualified`, and `condition`. `condition` is the car as AC left it: body damage by zone, engine life, gearbox, oil and water, fuel, and per wheel tyre wear, virtual km, blown and suspension damage. Step 2 puts `condition` onto the parts (schema 1.4 since).

Tested by the user in the game (2026-09-25): collisions work.

Not yet tested by the user in a race: a clean drag race to the finish, a disqualification for contact either way, a
crashed rival being held, a false start on a road race, `assists.ini` coming back after the race.

**Done: step 2, damage, timeslips and the repair shop** (PR #18, merged as `d1106a2`; built
on 2026-09-24 as one PR, the user's choice). See `docs/ac-integration/csp-lua-scripts.md` "Damage" and
`docs/systems/parts-system.md` "Damage and repairs".
- **The user decided:**
  - AC's damage at 100%;
  - a car with a blown engine, a wrecked gearbox or a totaled body (200 km/h of hits, all zones together) does not
    race or free-run until repaired, and neither does one with a broken corner or a blown tyre;
  - after a crash the player is towed to the garage and sees the damage report there.
- **Damage lands on the parts** (`CarCondition`):
  - engine life on the `Tear` of the rotating parts (crankshaft, rods, pistons, camshafts);
  - gearbox damage on the transmission;
  - a bent steering rod on that corner's spring and shock;
  - tyre wear on the tyres' `Wear`, a blown tyre's `Tear` to 0;
  - body damage on the car, in km/h per zone (`Car.BodyDamageKmh`);
  - a little mileage `Wear` on the engine.
  `Car.EngineHealth` and the other car figures are worked out from the parts after every race and repair. Old result
  files without `condition` still get the flat wear of before.
- **The next race starts where the last left off.** `race.ini [STREET_CORSA]` gets `CAR_n_BODY`, `ENGINE_LIFE`,
  `GEARBOX` and `SUSPENSION`. The mode sets body and engine life. The car's data carries the rest (`AcDamageData`):
  slower shifts for a worn gearbox, toe for a bent axle. AC's tyre km are deliberately not carried: the tyre's wear
  already lowers its grip in `tyres.ini`.
- **Breakdowns** (schema 1.4, `BROKE_DOWN`): a blown engine, a gearbox or a corner past what it can take, a blown
  tyre. The player's is a loss, the rival's a win, both cars out a draw (`WinCondition.BothOut`).
- **Timeslips** for drag races (R/T, 60', 330', ⅛, 1000', ¼, trap speeds over the last 66 ft), shown in a timeslip
  dialog after the race.
- **The garage's Repairs button** (`RepairShop`): engine and gearbox rebuilds, straightened corners, new tyres, body
  work, for money and garage time (`GarageWorkMinor` 30 min, `GarageWorkMajor` 2 h).
- **Opponents' cars** carry their damage too, but race with just enough to leave the line (`CarCondition.Runnable`)
  until step 5 has them repair their cars.

Not yet tested by the user in the game:
- the scratches and dents redrawing when the body damage is set at the start;
- breakdowns, both ways;
- the timeslip marks on `ks_drag` (its layout is 1000 m, so the ¼ mile falls inside the lap);
- how the toe and gearbox data drive;
- the tow to the garage after a crash.

Left for later: overheating and oil starvation modelled by the game (CSP only fills oil figures for scripted cars),
fuel carried between races, body dirt, each car's best elapsed time, bracket racing with a dial-in, and whether AC's
full damage wrecks 1970 street cars too fast (the user chose 100% to start).

**Done: step 4, the economy** (PR #19, merged as `a04eaf3`). See `docs/systems/market-system.md` "Selling Cars".
- **The user decided:**
  - the difficulty belongs to the save, picked on the New Game screen (Easy, Normal, Hard, each figure adjustable, which
    makes it Custom), not app-wide settings;
  - selling through the paper is an ad with buyers calling over the days;
  - a dealer pays 60% of what the car is worth.
- **`GameRules`** (`GameState.Rules`; saves from before load as Normal) replaced the unused economy and difficulty fields
  of `GameSettings`, which now only holds the AC folder and the last free-run track. It drives:
  - `CarPriceMultiplier`: what the lots ask (new listings, relisted pink slips and trade-ins);
  - `PartPriceMultiplier`: the parts shop, the parts ads and the repair bay;
  - `RacePrizeMultiplier`: event cash, shown and paid (`EventReward.ScaledBy`);
  - `OpponentSkill/AggressionModifier`: on top of each rival's AI in `OpponentAIAdapter`, never under AI level 85;
  - `CarWearMultiplier`: AC's `DAMAGE` (`RaceDamagePercent`, at most 100) and the parts' mileage wear, and the simulated
    rivals' wear;
  - `PinkSlipFrequency`: rival-vs-rival pink slips (10% of road races on Medium), pink-slip events turning up, and how
    readily a rival takes a pink slip on;
  - `RaceSimulationEnabled`, `SeasonalRacingEnabled`, `MarketRefreshEnabled`: the daily rival races, their summer and
    winter rhythm, the dealers restocking.
  Presets: Easy 0.85/0.85/1.25, skill −3, aggression −15, wear 0.6, pink slips Low; Hard 1.15/1.15/0.85, +3, +15,
  1.3, High. The starting money stays $1M on every difficulty until a first release.
- **Selling** (`CarSaleService`, the garage's Sell button): the dealer on the spot, the scrapyard for a totaled car
  (15% ±20%), or an ad in the paper whose buyers show in the Sell dialog and under "Your Ads" in the newspaper.
  `CarsSold` and `CarsOwned` (on a purchase) are now counted.
- **Bigger bets** (`MatchupCalculator.StakesFactor`): the house limit grows by one for every 25 points of the rival's
  reputation over 50 (three times at 100) and doubles from 20:00, still capped by the poorer racer's money. The diner
  says why the stakes are up. Races still run in daylight: a night race (`SUN_ANGLE`) belongs with step 6.

Tested by the user in the game. Known issue: the newspaper's "Your Ads" panel runs off the bottom of the screen and
can't be scrolled; the newspaper screen is due a refactor.

## Step 5, living opponents (done, PR #20, merged as `70161ad`)

See `docs/systems/opponent-system.md` "Living Opponents" for what was built.

Checked with a scratch console harness: a new career on the real catalog, parts and lots, run for 60–90 game days
(review, rival races, market refresh; nothing saved). Rivals buy, sell, repair, go broke and come back, and newcomers
arrive on schedule. The review takes 0.1–0.8 s a day. The first cut tuned far too fast (every rival on 740 hp within
weeks), so it was slowed: 15% a day, one upgrade, 30% of the money over a reserve. The user then asked that the
rivals behave by the prices they find (prices get tuned later): every sum is price-relative, and the 1.8×-factory
power cap added meanwhile was dropped. Money is the limit. Many rivals end up in Chevelle SS 454
LS5s, because the dyno rates that engine at 555 hp against a cheap price (the known over-rating of some SLRR engines).

Tested by the user in the game (2026-09-24): the street talk panel, The King, a rival answering an ad, damaged rival cars.

Left for later: new racers generated when the pool runs dry (`OpponentGenerationService` is still unused); rivals
putting their own cars in the paper; rivals buying used parts out of the ads (they order new); the King's own
portrait; dyno horsepower in the diner's matchup panel (not picked by the user).

**The user decided (2026-09-24):**
- **Ramp up:** 6 rivals active at the start and 2 more each week, pulled from the inactive pool (the GameMaker rule).
  Rivals who lose their last car or go broke retire and come back through the daily review.
- **First cars are made, with real parts:** a used engine (sometimes tuned) and running gear on New Game, so the dyno
  and the repair bill are real from day one. After that they buy off the lots and answer the player's ads.
- **The King in this PR:** a fixed boss, strong well-tuned car, top skill, pink slips only, shown once the King victory
  unlocks (10 wins, 50 reputation). He lives through the same daily review.
- **Visible:** "word on the street" at the diner (purchases, tuning, going broke, coming back, pink slips) and rivals
  among the buyers who answer the player's ads (the car then races under them).
- Not picked: dyno horsepower in the diner's matchup panel (it keeps the catalog figures).

**Design:**
- `Car.PowerHp` / `UsedCarListing.PowerHp`: the dyno figure, cached; the simulator and the rivals' choices use it.
- `OpponentLifeService` + `OpponentReviewTask` (daily, before the race simulator): retired racers are fixed first,
  then the ready ones are checked; best car to the front (`Cars[0]`, the convention everywhere), buy when carless,
  repair when the whole bill is affordable (`RepairShop.Jobs`), sell the wreck and buy again when that gets them
  racing, keep at most 2 cars (extras to the trade-in lot at the dealer's 60%, wrecks for scrap), bankrupt = cash
  injection $50–500; then activation up to `6 + 2 × weeks`; then tuning.
- `EngineTuner` (Parts/Cars, dyno-checked): bolt-on swaps scored `gain% / cost × priority` (block swap 10, blower 8,
  carbs/injection 7, exhaust 6, manifold/camshaft 5, air 3), at least 3% gain, parts that stop fitting replaced to a
  depth of 5, a same-family bigger engine as the block swap; new parts at the mail-order price, replaced parts traded
  in. Run off the UI thread on clones, a few racers a day.
- The simulator's wear goes onto the parts (`CarCondition.ApplyRace` with a made-up condition), crashes can total a
  car, a pink-slipped car stays with the winner (the review sells it on), a carless loser retires.
- Rivals' cars race with their real damage (no more `CarCondition.Runnable`); a rival whose car can't race isn't at the
  diner.

## Step 2: damage, as real as AC allows (done, PR #18)

The research and the design sketch, kept as the record. What was built differs in places: see "Where
things stand".

**User:**
- "Everything that AC takes into consideration should be ported to the game": map everything AC tracks.
- Visual damage wanted ("even scratches in the body will work").
- A broken engine, suspension or gearbox during the race puts that racer out. If both break, it's a draw (agreed).

**Facts (CSP 0.2.11 SDK, checked in the game 2026-09-24):**
- **Reading** needs no permission. Everything above is already in `condition`.
  - Oil temperature and oil pressure read about 0 on stock cars: CSP only fills them for cars with a script. The game should model overheating and oil starvation itself, from the parts and how long the engine sat near the limiter.
  - Brake temperatures exist only on the 46 cars that have `[TEMPS_*]` in `brakes.ini`.
- **Writing at the start**, with the mode's physics API:
  - `physics.setCarBodyDamage(i, vec4)` and `physics.setCarEngineLife(i, v)`: both take, and read back, in the game.
  - `physics.setTyresVirtualKM` and `physics.setCarFuel`.
  - `ac.setBodyDirt(i, v)`, which needs no permission.
- **No setter for gearbox or suspension damage.** Fake them in the car data the game already writes for each race:
  - a bent corner as toe and camber offsets in `suspensions.ini`;
  - a worn gearbox as a tighter `RPM_WINDOW_K` or slower shifts in `drivetrain.ini`.
- **Visual damage follows `damage[]`.** 102 of the 165 cars have a `damage.ini` with paint scratches, cracked glass and hanging bumpers or bonnets, all driven by the body damage zones. Setting the body damage at the start therefore brings the visuals back too (still to confirm in the game that the scratches redraw at once). CSP can also crumple the bonnet on some cars (`[DEFORMING_HOOD]`).
- **What AC already does by itself:**
  - it kills an engine whose `engineLifeLeft` reaches 0;
  - over-revving wears the engine, since `AcEngineData.cs:155` already sets `RPM_THRESHOLD` from the weakest engine part;
  - missed shifts raise `gearboxDamage`.
- **Breakdowns the mode can force** from part wear (all need the physics API): `setCarEngineLife(i, 0)`, `blowTyres`, `lockUserGearboxFor`, `setGripDecrease`.

**The disconnect to fix first:**
- The race's wear currently lands on car-level fields (`Car.EngineHealth`, `TransmissionHealth`, `BodyCondition`, `TireCondition`) in `RaceResultProcessor.ApplyCarDegradation`. Once a car has its part tree, those only change its price.
- The part `Wear` that actually changes grip, brakes and the engine script (`AcRunningGearData.cs:87`, `PartScriptRuntime`) is set when a part is made (`EngineFactory.cs:101`) and **never goes down**.
- Step 2 moves the race's wear onto the parts, and works the car-level values out from the parts or retires them.

**Design sketch:**
1. **C#:** read `condition` into `RaceResultJson`, then map it to parts:
   - body zones go to the body panels' `Tear`/`Wear`, kept as km/h per zone so they can go back exactly;
   - `engine_life` goes to the engine internals;
   - `gearbox_damage` to the transmission;
   - `suspension_damage` to that corner's suspension parts;
   - tyre wear to the tyres.
2. **The car keeps its AC state for the next race:** body zones, engine life and tyre km, stored on the car or its parts.
3. **`RaceCarData`/`race.ini` carry the start state to the mode.** A new `[STREET_CORSA]` key or a small JSON file next to `race.ini`. At the start, the mode calls `setCarBodyDamage`, `setCarEngineLife`, `setTyresVirtualKM` and `setBodyDirt`. The rival's car carries its own state the same way.
4. **Breakdowns:** the mode checks every frame for a dead engine (`engineLifeLeft <= 0`), a gearbox near 1, or a blown tyre. That car is out:
   - a player breakdown ends the race, `end_reason` `BROKE_DOWN`;
   - a rival's breakdown holds it where it stopped;
   - both broken is a draw.
5. **Repair shop:** time and money per part, using `GameAction.GarageWorkMinor/Major`, which exist but are unused. A "totaled" state for a car whose crash was hard enough.
6. **Open:** how much of AC's damage to scale (the full rate may wreck cars too fast for 1970 street cars), and whether a car too damaged to run can race at all (`RaceSetupBuilder` already refuses a player car that won't run).

## Step 2b: timeslips (done with step 2, PR #18)

**User:** yes.

The mode records reaction time (green to the car moving), 60 ft, ⅛ mile, ¼ mile elapsed time, and trap speed for
both cars, measured from AC's green (`sim.isSessionStarted` turning true; in a race session that is AC's start
lights, and the rival leaves on it). The distances come from progress along the track (`car.splinePosition` times the
track length, or distance from the line along the strip's axis the mode already has). They go into the result, and
the app shows a timeslip after a drag race. Built: the elapsed times run from the car leaving the line (0.2 m), the
reaction time from the green to that, the distance is along the strip's axis. Later: bracket races with a dial-in,
and each car's best elapsed time.

## Step 3: crash and start rules (done)

**Done:**
- a crash is 15 g during a collision (Test Drive uses 10 g in `C:\GAMES\Assetto Corsa\extension\lua\new-modes\test-drive`);
- quitting AC is a forfeit;
- a false start is no contest plus a reputation drain;
- drag race contact disqualifies the car further out of its lane.

**Dismissed:** a flagger in the street. The user thought AC had one; building one (a model and an animation, or a picture standing in the road) was dismissed, so AC's start lights stay.

## Step 4: economy (done)

The plan as it stood; what was built is under "Where things stand".

**User:** yes to all, except that the starting money stays at the $1M placeholder until a first release (they are testing).
- **Selling cars:** to the dealer and in the newspaper. `GameAction.SellCar` exists but nothing uses it, and `CarsSold` is never written.
- **Make `GameSettings` do something.** None of its economy or difficulty fields is read anywhere (`CarPriceMultiplier`, `PartPriceMultiplier`, `RacePrizeMultiplier`, `OpponentSkill/AggressionModifier`, `CarWearMultiplier`, `PinkSlipFrequency`, `RaceSimulationEnabled`, `SeasonalRacingEnabled`, `MarketRefreshEnabled`). The New Game screen could offer a difficulty. `CarWearMultiplier` could scale AC's damage (`IniModificationService.RaceDamage`, now 100) and the mileage wear (`CarCondition.MileageWearPerKm`).
- **The repair shop:** done in step 2 (`RepairShop`). Its prices are constants in `RepairShop` (60% of the damaged
  parts' new price, $15 labour, $8 per km/h of body damage): `PartPriceMultiplier` or a labour setting could scale them.
- **Selling a wreck:** a totaled car should sell for scrap (the GameMaker version pays 15% ±20% of its value).
  `CarValuation.ConditionOf` already sees the damage, since the car's figures come from its parts.
- **Bigger bets:** they are $10–100 on drag and $25–250 on road today (`MatchupCalculator.cs:17-32`), and could grow at night and against higher-reputation rivals.

## Step 5: living opponents

**User:**
- "Opponents should be fully alive".
- They buy cars from the dealers and the newspaper, like the player.
- They tune their cars within their budget.
- When broke, they can come back with new money.

Source: the user's GameMaker remake, `C:\Users\Administrator\GameMakerProjects\street-rod`.
`RaceSimulatorService.cs` already ports its race simulation (`scr_simulate_races`) almost line for line. What was never
ported is the daily life cycle:

- **The daily review** (`scripts/scr_review_racers`), a new scheduled task:
  - **Pools:** `inactive`, `readyToRace` and `retired`. The minimum number of active racers is `6 + 2 × weeks elapsed`, pulled from `inactive`.
  - **Pick a car:** the best owned car, scored as `hp×10 + condition×5 + 1000 if it runs + value×0.1`.
  - **No car:** buy the best affordable one from the same listings the player sees; it leaves the market. It needs at least $100. Purchase score:
    - `hp×2 + cond×3 + 500 if it runs + (hp/price)×1000`;
    - plus `(1 − price/budget)×200` when the car costs under 70% of the budget, so money is left for repairs.
  - **Car doesn't run:** repair only if the whole bill is affordable (`StructCar.repair`, `StructCar.gml:541`). Otherwise the racer retires until they can. Since step 2 this is real: an opponent's car keeps its damage, and `CarCondition.WhyCannotRace` plus `RepairShop.Jobs` give the bill. Once opponents repair, `RaceSetupBuilder` can stop making their cars `Runnable`.
  - **Bankrupt** (no running car, and less money than the cheapest used car, or $200 if the market is empty): a cash injection of $50–500, then back to racing.
  - Retired racers are checked daily and return once they're ready.
- **Tuning within budget** (`StructCar.tuneUp` + `scr_get_car_upgrades`), to run every day in the review, not only when a car is generated:
  - each upgrade is scored by `improvement% / cost × slot priority`: block 10, supercharger 8, fuel injection 7, exhaust 6, intake 5, transmission 4, fuel and air delivery 3, differential 2, others 1;
  - an upgrade must gain at least 3%;
  - parts that stop fitting (block → intake/exhaust → carb → air cleaner) are replaced, searching to a depth of 5;
  - upgrades are picked greedily within budget, one per slot.

  Build it on the part tree's slot fittings (`FindFittingParts`), paid from the opponent's money.
- **Changes this needs in Street Corsa:**
  - `CarPurchaseService.PurchaseAsync` and `NewspaperPartsService.BuyNew/BuyUsed` are wired to `gameState.Player` and spend game time: they need a buyer and a path that costs no time.
  - `RaceSimulatorService` fakes horsepower as `100 + PurchasePrice/50` (line ~501) and only uses `Cars[0]`. It needs the real dyno figure cached on the `Car` and refreshed when parts change, or tuning won't change who wins.
  - `OpponentInitializationService.GenerateOpponentCar` invents cars instead of buying listings. Opponents start with $2000–5000 here, against $200–800 in the GameMaker version.
  - `OpponentEvolutionService` and `OpponentGenerationService` are never instantiated. The docs say evolution runs after each race; it doesn't. The pool only shrinks.
  - A crash could total the car as in the GameMaker version: 15% ±20% of its value as scrap, which goes to the winner in a pink-slip race.
- **Also:**
  - `MatchupCalculator` compares catalog horsepower, not what's under the hood: use the dyno.
  - Add "The King" as a boss at the top of a reputation ladder. The King victory can't be won today, because no racer is called "The King" (`KingVictory.cs:13, 84`).

## Step 6: police chases (done, PR #21, merged as `fc450b8`)

**The user decided (2026-09-24):**
- real AI cops (design (c) below), not a ghost or a text roll;
- the 1974 Dodge Monaco mod as the police car, which the player installs;
- busted is a fine and the car in the impound, and the race counts as a loss;
- getting away earns reputation;
- the rival can be busted too, and both busted is no contest;
- races after 20:00 run at night, the police chance rolled from the time, the reputation and the stakes, and the
  diner shows the risk.

**What was built** (see `docs/ac-integration/csp-lua-scripts.md`, "The police chase"):
- **The career side.**
  - The diner rolls the patrol when a road race is agreed (`PoliceCars.Patrol`, `PoliceRules`):
    - the chance: 8% by day, 30% from 20:00, up to 15% more for the better known racer, 5% more each for a pink slip
      and a wager of $1,000 or more, at most 50%;
    - the cars: two, one for each racer, on a track with four pit boxes or more.
  - The police car is the installed car with liveries marked `"street_corsa_police": true` in `ui_skin.json`, a
    Monaco first (`PoliceCars.Find`). Without one, no police come and the diner shows no risk.
  - race.ini gets the cops as `[CAR_2]`… and `[STREET_CORSA] POLICE`/`POLICE_SPOT`, and `SUN_ANGLE` from the game's
    clock for every race.
- **The race mode** runs the chase and writes `pursuit` (schema 1.5):
  - speed traps or a patrol;
  - a cop racing each racer, a racer it gets past busted;
  - one roadblock per cop;
  - lights and a synthesized siren.
- **The result:**
  - busted: the fine ($750, $500 more for each earlier bust, at most $5,000);
  - busted: the impound (2 days, one more for each earlier bust, at most 7, $100 a day; an unpaid fine goes on the
    bill), collected with the garage's Collect button;
  - getting away: +2 reputation each time, up to +10.
- **Rivals** pay to collect their cars, sit out meanwhile, and lose a car left unpaid 14 days past its date.
- **Organised events** (the newspaper) never draw police, but run at the game's hour too.
- **Drag races** have no police: the strip has two pit boxes and nowhere to run.

**The police car:** the 1974 Dodge Monaco Police (Stereo) is installed and sanitized to factory spec (275 hp net 440, 2.94
pursuit axle). Its liveries carry `"street_corsa_police": true` in `ui_skin.json`, which is how the game knows a police
livery (`PoliceCars`). Marked liveries never reach the catalog, and a car with nothing else stays out of it (no police
car on a lot or in a rival's garage). The civilian Monaco of the same mod is an ordinary car of the set.

**Tested in the game** (2026-09-24, unattended runs, see `docs/ac-integration/csp-lua-scripts.md`): the busted and got-away
paths, roadblocks, a stuck cop put back, night at 21:00. The game found five things the stub couldn't:
- the INI list bug, which also fixes step 2's body and suspension carry-over;
- cops ramming a stopped player;
- a roadblock square across the road with no way through;
- AI racers stopping behind a roadblock instead of going round it;
- a roadblock "left" in the frame it went up.

Tested by the user in the game (2026-09-25): a police chase works.

Not yet seen by the user: the lights and the siren (the screenshots caught the terminal over the AC window); the
diner's police note; the garage's impound panel.

**Speed traps** (added after the first review): two patrols in three are cops parked on the verge at random spots on
straights round the track, set off by whoever goes past (see the race mode doc). Only a racer a cop has been after has
a chase to win or lose. A cop that catches the rival stays with them. Checked in the game. The police chance was kept
as it was (the user thought 30–50% of races would be too many).

**The police drive like racers** (the user's rules, 2026-09-25):
- **One cop for each racer.** Each races its prey at AI level 150% with full aggression, and tries to get past.
- **Getting past is the bust.** The player's car is taken over by an autopilot and braked to a stop; the rival is
  held.
- **A dodged roadblock** sends the cop after its prey at full throttle.
- **The finish line is home:** a racer over it is out of the police's reach.
- **The police Monaco has the 426 Hemi.** The factory 440 couldn't keep up with a 450 hp Chevelle.
- **Dropped:** the boxing-in, the PIT, the pinned bust and a pushed rubber band, tried along the way.
- **The chance:** the police chance is unchanged, and it always sends two cars.

Left for later: Test Drive mode (race-explorer); rivals busted in their own races with each other; the difficulty's say
in the police chance; hiding the cops from AC's HUD leaderboard. Any other car can be a police car by marking a skin.

**User:** loves it, and wants it for Test Drive mode too.

**Research (2026-09-24):**
- **No free single-player police-chase script exists.** Cops-and-robbers only exists on online servers with human cops.
- **CSP AI only drives along the track's AI spline.** It can be:
  - shifted sideways with `physics.setAISplineOffset` / `setAISplineAbsoluteOffset` (clamped to the spline's width; `true` blinds it to other cars);
  - swapped for another spline with `physics.setAISpline`, which is experimental; the user's `new-modes/spline-spike` was built to measure it and has no recorded results;
  - taken over with `ac.overrideCarControls(i)` for steering (pedals combine by maximum, so hold the throttle limit at 0 and drive it yourself).
- **Pace controls:** `setAITopSpeed`, `setAIThrottleLimit`, `setExtraAIGrip` (the cheap rubber band), `setAIStopCounter`, `setAILevel`/`Aggression`. CSP resets aggression, so re-set it about every 0.5 s.
- **Placing and hiding cars:** `setAICarPosition`, `setCarVelocity`, `ac.setCarActive(i, false)` to hide, `physics.disableCarCollisions`.
- **Lights and siren:** AI cars' own light bars can't be switched from Lua. Draw them: an `ac.LightSource` on the roof, alternating red and blue, and/or `setMaterialProperty('ksEmissive')`. Siren: `ac.AudioEvent.fromFile({use3D = true, loop = true})` with `:setPosition` every frame, which gives a Doppler effect.
- **Cars:**
  - the [1974 Dodge Monaco police](https://www.overtake.gg/downloads/1974-dodge-monaco.22283/) is the best period car (rated 4.91, Bluesmobile-style liveries, no working light bar);
  - or a black-and-white skin on a Galaxie 500 four-door, Impala 1962, Belvedere 1965, Fury 1958, Coronet 1967 or Montego 1970, all already installed;
  - no police car is installed today.
- **Tracks:**
  - in the Street Corsa install: Black Cat County (rural US) and Highlands. Both are loops with one AI line.
  - in the main AC install, tracks with `traffic.json`: `new_plymouth` (a real town grid), `tripoli`, `mecheria` and others.
  - chases don't suit the drag strip.

**Design, simplest first:**
- (a) **Text only:** after the race, work out the outcome from risk and the player's margin.
- (b) **A ghost cop:** lights and siren that follow the player's progress with no collisions, with busted or escaped decided from the gap.
- (c) **A full AI chase on loop tracks:**
  - 1–3 police as extra `race.ini` entries, hidden and parked, then placed 150–300 m behind the player and released at full pace;
  - pace adjusted from the gap, and shifted sideways to box the player in within about 30 m;
  - if a cop falls too far behind, it jumps ahead as a roadblock.
- (d) **Later:** custom driving code for town tracks such as `new_plymouth`.

**Outcomes:**
- **busted:** a cop within 8 m and the player under 15 km/h for 3 s;
- **escaped:** more than 600 m ahead for 20 s;
- the result carries a `pursuit` field, and the app applies a fine, impound or reputation;
- the police are left out of the race result.

**Setting:** a night street race (`SUN_ANGLE`/time in `race.ini`; races run in daytime today), the risk rolled from the neighbourhood, time and reputation.

## New ideas the user liked

- **Test-and-tune:** paid time at the strip for a timeslip with nothing at stake, building on the free run.
- **Rivals remember you:** a grudge rematch after a pink-slip loss, with talk lines to match. `StaticTalkService` today; the LLM talk planned in `docs/ai-integration.md` later.
- **Newspaper articles written from race history** (the processed race sessions are already saved).
- **Car history:** every car keeps its odometer, previous owners and wins, and its price reflects them.
- **From `docs/ideas.txt` (2026-09-26):** planned as the polish PR and steps 16–19 below.
- **From `docs/ideas.txt` (2026-09-27, batch 3):** planned as the polish PR and steps 20–23 below.
- **Parts that fail in the race:** an over-revved engine throws a rod in AC, and you find the damaged part on the workbench afterwards. Since step 2 the whole rotating assembly takes the damage together (the parts view shows it); singling out the one part that failed is still open.

## Step 7: career gaps (done, PR #22, merged as `6aa3d39`)

The known gaps the analysis found, closed in one PR. **The user decided (2026-09-25):** only the picked victory
path wins, and the player keeps racing after the win or goes to the main menu; about half the events become road
races; the prize camshaft is the best one that fits. See `docs/career-system.md` ("Default Events", "Winning the
Game") and `docs/systems/time-system.md`.

- **End Day.** The garage's calendar panel has an End Day button: after asking, the rest of the day goes by, the
  night's tasks run and the game is saved.
- **Events:**
  - five are road races (Classic Showdown, Fifties Fever, Sixties Showdown, European Invasion, Chevy Challenge),
    raced on an installed circuit; the other five stay at the strip;
  - at most five invitations are open at once;
  - the career's reputation and cars-owned counters follow the player (`CareerState.SyncStanding`), so events that
    ask for reputation show from the first day;
  - the prize camshaft is real: the dearest camshaft that fits the winning engine and beats its own, on the shelf,
    or $500 when none does;
  - a pink-slip event only draws a racer of the pool, who has a garage for the player's car.
- **Victory:** only the picked path wins (any, with none picked); picking a path already reached wins at once. The
  victory screen shows the career sheet, with Keep Racing and Main Menu.
- **Cleared away:** `GameState.UsedCars`, `GameState.UsedParts`, `NewspaperAds.Cars` (never filled; old saves still
  load) and the unbound `RefreshMarketCommand`. `Player.Stats.CarsOwned` already went up on purchases.
- **Docs brought up to date:** `career-system.md`, `systems/time-system.md`, `diner-refactoring-plan.md`.

Not yet seen by the user: the End Day button, the victory screen, a road-race event. The end of the game (victory)
takes too long to reach by playing; the user will test it in the first release.

## Steps 8–15: what comes next (planned 2026-09-25)

Everything left over from steps 1–7, plus `docs/ideas.txt`, grouped into vertical slices. **The user decided:** the steps
are taken one at a time, in this order. The rename comes last because it touches every file and would clash with any
open branch.

### Step 8: playtest and fix
The user plays what nobody has seen yet, from a checklist, and one PR fixes what breaks. Already tested: collisions and
a police chase. Left out: the end of the game, kept for the first release.
- **Race rules:** a clean drag race to the finish, a contact disqualification either way, a crashed rival being held,
  a false start on a road race, `assists.ini` coming back.
- **Damage:** the dents redrawing at the start, breakdowns both ways, the timeslip marks on `ks_drag`, how the toe and
  gearbox data drive, the tow to the garage.
- **Police:** the lights and the siren, the diner's police note, the garage's impound panel.
- **Career:** the End Day button, a road-race event.
- **Economy:** a price balance check after the $5k price fix (audit 2026-09-25). **The user decided (2026-09-25):**
  they review the prices of every car and part themselves first, then the economy.

**Started before step 8 was done (2026-09-25):** the user plays the checklist while step 9 is built.

### Step 9: a new main screen (done, PR #23, merged as `7ff1fda`)
From `docs/ideas.txt`. Owned or catalog cars picked at random stand in a showroom, filmed with slow camera moves (like
Gran Turismo's menus). Over it, a fade to black rises from the bottom to about half the screen, and New Game, Load Game
and Settings are semi-transparent cards that fade in and out; the screen never navigates away to show them. It builds
on the garage renderer (`GarageRenderer`) and the engine preview.

**The user decided (2026-09-25):** catalog cars only (no save is loaded yet); the rooms rotate (the garage plus the
installed AC showrooms); no sound; the menu stays as buttons, and a click opens that button's card, one at a time, with
fades and a Back button, the cars always behind. **As built:** see `docs/screens/main-screen.md`. The cars fill the
top 72% of the window in a wide frame, with the fade from 40% to solid black; six GT-style shot kinds with dissolves
between them and a fade through black between cars; three shots a car, three cars a room. Esc closes a card. The old
picture shows until the first frame, and whenever there is nothing to show.

**The user saw it (2026-09-25): "I love it!"** Their tweaks, done: the fade and the menu moved to the top (cars in the
bottom 80%); several cars in each room (a lineup of 3–4, the camera staying with one at a time and keeping clear of
the others); the screen opens black, and the old picture only comes up when there is nothing to show. Also found:
encrypted car mods (scrambled normals) are now left out of the catalog, see `docs/systems/catalog-system.md`.

**Reviewed before the merge (2026-09-25):** an xhigh code review found 15 issues, all fixed in the PR. The worst was the
garage's navigation tiles throwing on hover. The others: Esc closing every card, cards opened as safely as screens, a
broken room skipped rather than ending the showroom, the central car in the main slot. Build and tests pass; not yet
tried in the game.

### Step 10: the world remembers (done, PR #24, merged as `2db9eb1`)
Three features, one PR:
- **car history:** every car keeps its odometer (`Car.OdometerKM` exists), previous owners and wins, and its price
  reflects them (`CarValuation`);
- **grudges:** a rival who lost a pink slip asks for a rematch, with talk lines to match (`StaticTalkService`);
- **newspaper articles** written from the race history.

**The user decided (2026-09-25):** history moves the price modestly (wins up to +15%, owners and mileage down to
−15%, condition still leads); the rematch is a diner offer (marked, grudge lines, pink slips accepted whatever the odds,
two weeks, in the street talk); the paper's articles cover the player's notable races and the rivals' big ones.

**As built:** see `docs/systems/market-system.md` ("Car History", "Pricing"), `docs/systems/opponent-system.md`
("Grudges") and `NewsWriter`.
- The history lives on the car (`CarHistory`), not in the race sessions: the rivals' races were never saved. The
  history is copied onto the listing and back, and a relisted car keeps its id (review fix).
- Articles (`GameState.News`, two weeks kept) are written when a race is settled, only when there is a story: the
  player's pink slips, the King, rematches, the police, wrecks, event wins, upsets, cash races of $1,000 and more, the
  first win; from the rivals' own races, pink slips and wrecks. The paper prints the last 3 days, 5 pieces, newest
  day first and the weightiest first within it, the lead in bigger type.
- The newspaper got a third column for them, **Street News**; the market buttons are two thirds their size side by
  side, and "Your Ads" scrolls: the overflow from step 4 is fixed. The invitations panel stays up with a line when
  there are none.
- The race session record now keeps the game date, both names and car ids, the race type and the event.
- Found on the way: the diner never told the talk service a pink slip was on the table, so the pink-slip lines were
  never said; fixed.
- The race snapshot now covers the street talk and the news too, so a race that fails to save leaves none behind.

Not yet seen by the user in the game: the new newspaper (checked in a render at 1920×1080), a rematch at the diner,
history on the lots and in the garage, the price effect on the lots.

**Reviewed before the merge (2026-09-25):** an xhigh code review found 14 issues, all fixed in the PR. The worst: picking
a rival who wanted a rematch left pink slips on the table for the next rival, and a rematch took a car worth nothing.
The others: a grudge kept after the rival bought the car back, headlines naming a racer twice, event races losing
their story to a wreck, older saves showing used cars as first-owner cars, a car won back counting its owner twice,
the King's car priced before its record. Build and tests pass; not yet tried in the game.

### Step 11: strip extras (done, PR #25, merged as `fdac6cc`)
- **Test-and-tune:** paid time at the strip for a timeslip with nothing at stake, building on the free run.
- **Bracket racing** with a dial-in.
- **Each car's best elapsed time.**
- **The part that failed:** name the one part that broke, not the whole rotating assembly (see step 2).

**The user decided (2026-09-25), on `feature/strip-extras`:**
- test-and-tune is free and costs only game time, like the free run; each pass gives a timeslip, with nothing at stake
  beyond the usual wear;
- bracket races come from the diner (a rival offers a bracket race as a race type), from bracket events, and the
  dial-in comes from the test-and-tune slips and the car's best ET;
- real bracket rules: each racer sets a dial-in, the slower car gets the green first by the difference, and breaking
  out (running quicker than the dial-in) loses; the player picks theirs, suggested from the best ET, and rivals dial in
  from their car's figures;
- when the engine fails, the weakest (most worn) part of the rotating assembly breaks and goes to 0%, the others take
  a smaller share, and the damage report and the repair shop name it.
Also chosen (not asked): the best ET shows in the garage and in the car's history, and it suggests the dial-in.

**As built** (see `docs/ac-integration/csp-lua-scripts.md` "Bracket races" and "Test-and-tune", schema 1.6):
- **Test-and-tune:** the garage's Test & Tune button, an hour at the strip (`GameAction.TestAndTune`). It picks
  ks_drag drag1000 when installed, else the longest strip that runs the quarter (`RaceSetupBuilder.PickStrip`).
  - It goes through the race pipeline with the player alone (`RACE_TYPE=TUNE`, 6 passes). Each pass has its own
    tree and timeslip, and the car is put back on the line after each.
  - The result is written after every pass. Afterwards the slips show side by side in the timeslip dialog, with the
    day's best.
  - The car wears and can break as in a race. Nothing else changes: no stats, no money.
- **Bracket races:**
  - At the diner, a "Run it as a bracket race" box on any strip that runs the quarter (400 m and up). There are two
    new bracket events, Bracket Night and Dial-In Shootout.
  - The player picks a dial-in in a dialog (±0.01 and ±0.1). It is suggested from the car's best rounded up to 0.05,
    or estimated from power and weight.
  - The rival dials in from its car's best, else Hale's formula for a street car (`BracketRules.StreetEtFactor`,
    not yet checked against AC's AI).
  - The mode runs a tree for each lane and holds the rival to its green. The rival takes the stripe by capping its
    AI top speed near the end.
  - Red light = false start (no contest, the existing rule). A breakout loses unless the other broke out by more.
  - The career decides again from the slips (`BracketRules.Decide`, `WinCondition.BracketFinish`, `PlayerBrokeOut`,
    `OpponentBrokeOut`).
- **Best ET:** `CarHistory.BestQuarterSeconds`/`Mph`/`Date`, from any quarter the car runs, whoever drives it. It
  shows with the history, and the timeslip says when it is a new best.
- **The part that failed:** the weakest rotating part takes the loss and the others 35% of it. The report says "a
  connecting rod let go", and the repair shop names the part.
- **Timeslip dialog:** any number of columns (lanes or passes), a DIAL row in a bracket race, "RL" on a red light,
  and a note line.
- **Tests:**
  - `tools/sc_race_harness/test_strip.py`: the chase stub laid out as a straight strip, 8 scenarios, and golden
    files for a bracket race and a test-and-tune.
  - The C# contract test checks that the career and the mode pick the same bracket winner.

Not yet tried in the game. **The user decided (2026-09-25):** they test these after the first release; the
checklist is `docs/playtest-step-8.md`, "6. Strip extras (after the first release)".
- the tree overlay and AC's own start lights together;
- the rival held and let go at its green;
- the stripe-taking with AC's real AI;
- `setCarPosition` putting the car back on the line between passes, and going to the pits to end a test-and-tune;
- whether the rivals' estimated dial-ins are near what AC's AI runs;
- the diner's bracket box and the dial-in dialog.

**Reviewed before the merge (2026-09-25):** an xhigh code review found 15 issues, all fixed in the PR. The worst: on a
test-and-tune every write after the first pass failed (CSP's `io.move` does not overwrite by default), so a crash on a
later pass was never applied. The others: a pass under way when the session ended was dropped, a car put back past its
spot never staged, the mode and the career could disagree on a breakout at the dial-in, bracket events could go to a
strip short of the quarter, strip lengths misread ("1,000 m", feet), small engine wear lost, an engine without rotating
parts keeping no damage, and a TUNE file settling a race. Build and tests pass; not yet tried in the game.

### Step 12: a bigger opponent pool (done, PR #26, merged as `52e9b4a`)
After step 10, since both touch the rivals and the newspaper.
- New racers when the pool runs dry (`OpponentGenerationService` is written but unused).
- Rivals put their own cars in the paper and buy used parts out of the ads.
- The King's own portrait.

**The user decided (2026-09-25):**
- new racers come from a second batch of 30, drawn with the ComfyUI script like the first 30;
- racers leave the scene for good now and then;
- a full private market: rivals advertise spare cars, which the player and other rivals buy;
- used parts both ways: what a rival takes off goes into the parts ads under their name, and rivals buy used parts
  there when they ask less.
Also chosen (not asked): once the new faces run out, racers who left 90 days ago or more come back; rivals trade up
now and then, which is what fills the paper.

**As built** (see `docs/systems/opponent-system.md`, "A bigger pool"):
- 60 racers and the King (`drv_031`–`drv_060` with prompts). Older saves get the newcomers and any missing portrait
  (`EnsureNewcomers`).
- Leaving (`RacerStatus.Departed`): 0.2% a day; 5% a day after 45 days sitting out; always on going broke a third
  time. Never the King, nor a racer with a race pending, a grudge or an offer on the player's car.
- Rival car ads (`RivalCarAds`): 75–90% of the lot price, 10% off after a week, to a dealer after 14 days. They show
  on the paper's Used Cars page as "(private)". Trade-ups: 5% of days, 20% more power, a quarter of the price left over.
- Rival part ads (`RivalPartAds`): parts taken off go into the paper; unsold ones get the shop's trade-in. The tuner
  buys used loose parts and whole engines (`UsedPartOffer`).
- The Life harness (180 days) came out at about 30 private sales, 18 leaving and 7 back. A first run had 22 leaving at
  0.4% a day, so the chance was halved.

**Portraits:** the 30 new racers and the King were drawn with `generate-portraits.js` (ComfyUI, juggernautXL) on
2026-09-26; the King's definition points at `king.png`.

**Reviewed before the merge (2026-09-26):** an xhigh code review found 15 issues, all fixed in the PR. The worst: a
rival counted as going broke once per day of it, not once per spell, so almost every broke rival quit within three
days. The others: Total Domination could be won with racers on the scene unbeaten once beaten ones left, and Season
Champion ignored racers away from town; a trade-up to a car that can't race put the only racing car in the paper; a
tuned used engine out of the paper passed the swap-power cap on its stock build; rivals could buy expired part ads;
comebacks announced as new faces; ad age by the hour; a null definition id stopping the whole daily review; the Used
Cars page running the dyno for every rival ad. Buying a rival's car now has its own entry point, and the trade-in rule
lives in one place (`PartPricing.TradeIn`). Build and tests pass; not yet tried in the game.

### Step 13: the street encounter (done, PR #27, merged as `f8d6612`)
From `docs/ideas.txt`, after step 9 because it reuses its renderer work. Not SLRR's Valo City: the player's car seen
from the driver's seat, looking left; a rival drives up and the race dialog pops up, as in Street Rod. Both cars have
their engine sound and body movement under braking and throttle (the engine preview has both).

**The user decided (2026-09-26):**
- the street is one we build: a CC0 panorama (Poly Haven) projected on a ground and a dome, written as a KN5;
- a new place, **Cruise**, reached from the diner: the player sits at the curb, the clock runs, a rival the game picks
  pulls up and makes an offer, to take, change or wave off; the diner's challenge panel stays;
- the light follows the game's clock (day, dusk, night), more rivals after 20:00.

**As built** (see `docs/screens/cruise.md`):
- **The street** is Poly Haven's Pretville Street, a 1950s American film set (CC0), made into three KN5s (day, dusk,
  night) by `tools/StreetScene` (`prepare_street.py`, then `StreetSceneBuilder`): a lit floor that takes the cars'
  shadows, and a self-lit ground ring and dome, all mapped from where the photo was taken. `Assets/Streets/pretville`.
- **The view** (`StreetViewport3D`) is from the player's car's own driver eyes, looking out of the side window; drag to
  look round. The player's engine idles and rocks the car and the view.
- **The rival** drives up the lane and stops alongside (`RivalDrive`), and pulls away when waved off: wheels turning,
  nose diving on the brakes and squatting on the launch, brake lights, headlights at night, and its engine turned by
  the wheels, heard from where the car is.
- **Two engines at once:** `EngineAudio` has a second channel, sharing the bank when both cars have the same sound.
- **Who and what** (`StreetEncounters`): waits of 50/35/18 minutes on average by day, at dusk and from 20:00; grudges
  come looking, the bold are out at night, the King seldom; drag races mostly; cash within the street's limits for the
  hour, pink slips now and then (always with the King or a grudge). A rival is met once a night.
- **One way to start a race:** the diner's challenge code moved into `ChallengeLauncher`, which both screens use. After
  a street race the player comes back to the street.
- 25 new tests (`StreetEncounterTests`); 750 in all. Checked with two off-screen harnesses (the viewport alone, and
  the whole screen on a copy of a save). **Tested by the user in the game (2026-09-26): "It's perfect!"**

**Reviewed before the merge (2026-09-26):** an xhigh code review found 15 issues, all fixed in the PR. The worst: a
rival's own pink-slip offer skipped the diner's value checks, so a junker could win a $20k car and do it again; the
first offer showed at the slider's old range ($10 for a $60 offer) and pink slips could not be answered with cash; a
failed way off the street left the screen dead; the rival's engine was heard from the wrong side (the renderer's space
is right-handed, FMOD's is not). The others: a race past 22:00 came back to the street in the morning, a save during a
day turnover, rivals met kept across loaded saves, the throttle stuck after Alt-Tab, a pop as a rival's engine
started, the rival's bank kept after it left, the same bank in two files failing to load, FMOD updated twice a frame,
the street redrawing at full rate forever, a first rival arriving already stopped on a slow machine. Who is out and
their portraits now come from one place (`RacersOut`) for the diner and the street. 751 tests pass; **the fixes are
not yet tried in the game.**

### Step 14: deeper simulation (done, PR #28, merged as `5930d9c`)
**The user decided (2026-09-26):**
- this step is heat and oil (overheating and oil starvation, modelled by the game) and fuel and dirt carried between
  races, in one PR;
- an engine that overheats or runs short of oil fades, wears and can blow in the race;
- the car keeps the fuel a race leaves, filling up costs money, and running dry puts the car out;
- **left for later:** the police extras (rivals busted in their own races, the difficulty's say in the police chance,
  the cops off AC's HUD leaderboard; CSP's `ac.setRaceScore` can only sink them to the bottom) and chases in Test
  Drive, which is race-explorer's mode in the main AC install, another project.

**Research (CSP 0.2.11 SDK, checked in the game):**
- Nothing ever overheated: results showed water and oil at about 80 °C. AC's water temperature is an estimate that
  changes nothing.
- The mode can set the water gauge (`physics.setWaterTemperature`), fade the power (`physics.setCarRestrictor`, which
  takes torque at high rpm: 100 took 9% at 5000 rpm on the Nova, 200 took 28%), set the fuel (`physics.setCarFuel`)
  and the dirt (`ac.setBodyDirt`); it reads `car.maxFuel` and `car.dirt`. All took in the game.
- The player's assists had `FUEL_RATE=0`: AC burned no fuel at all. A race now sets it to 1.
- The radiators, fans and water pumps of the source game have no figures; oil pans have a capacity (GM only).
- The cars pull 1 to 1.4 g in corners. A 0.9 g sump starved the AI on every lap; 1.05 g held for 3 s does not.
- `mode.lua` is at Lua's limit of 200 locals: the new code is one local, a module made by a function.

**As built** (see `docs/ac-integration/csp-lua-scripts.md`, "Heat and oil", and `docs/systems/parts-system.md`):
- **The career rates each car** (`EngineCooling`): its cooling against what its engine makes (sized for the car's
  factory power, or the block's after a swap; a racing radiator +35%, a performance water pump +10%, none 0.15), its
  fan, and the g its oil pan holds. race.ini `CAR_n_COOLING`, `CAR_n_FUEL`, `CAR_n_DIRT`.
- **The race mode runs the heat** for both racers: the gauge climbs as the radiator falls behind, the engine goes flat
  from 110 °C, cooks from 118 °C and boils over at 135 °C; a stock sump runs dry in a long sweeper at the limit and
  wears the bottom end under throttle. The player is warned. Breakdowns `OVERHEAT`, `OIL`, `FUEL`. Schema 1.7.
- **Fuel and dirt carry over**: `Car.FuelLitres` (null is full), `Car.FuelTankLitres`, `Car.BodyDirt`. The garage
  shows the fuel; its Repairs list fills up ($0.11 a litre) and washes ($3), 15 minutes each (`GameAction.GarageChore`).
  Under 2 litres keeps the car home. The rivals always race full and clean: their races leave the tank alone.
- **The report says what would help**: "A bigger radiator would keep it cool", "A deeper oil pan would hold it".
- **Rivals keep their engines cool**: their tuner fits a performance radiator or pump when the power outgrows the
  radiator, and makes no upgrade it cannot keep cool.
  **The user decided (2026-09-26):** every engine will have the cooling parts it needs (performance radiators, water
  pumps, fans). The game is to go open source and the community is expected to mod in parts, so every make should end
  up with similar possibilities. Design on that assumption: don't add workarounds for a make that has no performance
  radiator today (Ford rivals may stop tuning at about 1.18x their factory power until those parts exist); the fix is
  the missing parts, not the rule.
- **Review fixes (2026-09-26, /code-review):** fuel kept only against a known tank size; heat and oil damage wear the
  whole bottom end instead of naming a rod; a swapped block is cooled for its own power; a missing water pump counts;
  the fan stays when a radiator goes on; the mode counts only life it took (physics off: nothing) and its heat report
  cannot lose the result; fuel lines are not a "Damage Report"; cooling cached per stock build, not rated for a free run.
- Tests: `DeeperSimulationTests` (on the real catalog too), `tools/sc_race_harness/test_heat.py` (11 scenarios);
  797 in all. **Checked in the game** unattended on Black Cat County (a car rated 0.30 boiled over at 90 s, its
  factory-rated rival stayed at 90 °C); **not yet seen by the user.**

### Step 15: the rename to Street Corsa (done, PR #29, merged as `bc65528`)
Namespaces (`StreetCorsa`), the AppData folder (`StreetCorsa`, with a migration), ids like `sc_race`, and the repo.
Before a release, check the licence of every shipped asset: the icons in `Assets/Icons` are of unknown origin, and the
garage showroom is not licensed for the public repo.

**Done on `feature/rename-street-corsa` (2026-09-26):**
- The project is `Street Corsa/Street Corsa.csproj` (assembly "Street Corsa"), namespace `StreetCorsa`, the solution
  `Street Corsa.slnx`, the tests `tests/StreetCorsa.Tests`. Saves still load: `SaveTypeNames` maps the old
  assembly-qualified `_type` names by class name.
- The race mode is `sc_race` (repo `apps/new-modes/sc_race`, script 3.8.0): race.ini section `[STREET_CORSA]`,
  outbox `Documents\Assetto Corsa\out\sc_race` (and `sc_race_quarantine`), `metadata.source` `sc_race`. The launcher
  still takes `sr_race_manager` as a source, and `ScRaceMode` deletes the old
  `extension\lua\new-modes\sr_race`.
- `LegacyRename` runs first at start-up, before the log opens: `%AppData%\StreetRodAC` and the old outbox and
  quarantine move to the new names, entry by entry, never over something already there.
- The init screen's title is text ("STREET CORSA / POWERED BY ASSETTO CORSA") until the logo art exists; the old
  "Street Rod" image is gone.
- **Kept on purpose:** the AC install folder `C:\GAMES\Street Rod AC` (a folder on the user's machine, the default
  AC path while the settings name none); the markers the game leaves in AC car folders (`streetrod_clone.json`,
  `sfx\_streetrod_keep`), since a restore still pending from an older build finds its files by them;
  `CarProfile.StreetRodEra` (a catalog field); "Street Rod" where it means the original game; the dated audit
  findings.

## Steps 16–19: from `docs/ideas.txt` (planned 2026-09-26)

The second batch of the user's ideas (idea 2, the street, was step 13). **The user decided (2026-09-26):** a small
polish PR first, then the modding foundation before the features, so the engine stand and the gas station are built
mod-ready instead of retrofitted. One step at a time, in this order.

### Polish: main screen tweaks and parts scale (done, PR #30, merged as `9acb8d0`)
- **Main screen** (idea 1): smaller menu badges (160 px in `Styles/Buttons.xaml` `MenuButtonStyle`, to about 110–120),
  set a little lower; the fade (rows 20*/26* of `MainMenuScreenView.xaml`) starting lower; the cars less of the focus,
  e.g. the showroom slightly dimmed behind the menu.
- **Parts 20% bigger** (idea 5). The converter scales nothing beyond SLRR's cm to m (`SlrrMesh.UnitsToMetres`), so the
  parts are as big as SLRR made them. First measure a block against the real thing (a small block is about 0.66 m long,
  0.53 m wide) to find the true error. Try it at runtime: a scale in `CarPartsLayout.EnginePlacement` and `BySlot`
  (`CreateScale(s) * frame`, with `crankLocal`/`slotLocal` times `s`); children, candidates, bounds, picking and
  placement nudges all follow from the parent world, and `slot_shifts.json` stays in pack units. Never scale the whole
  prop (`SetProp`/`LocalMatrix`): it scales about the car's origin and the parts leave the hubs. Not scaled with it:
  the hub constants (a bigger engine may need to sit further back), `PlaceholderMesh.HalfSize`, `PartTravel`,
  `PartsMinRadius`. If it looks right, make it permanent in the converter (mesh and cfg positions together).

**As built (2026-09-26), seen by the user in the game:**
- The main screen: 116 px badges (were 160), 60 px from the top (were 28), closer together; the fade reaches 56% of
  the height (was 46%); the cars under it 15% darker. See `docs/screens/main-screen.md`.
- **Measured: the parts are about real size**, not small. GM small blocks 0.74 × 0.54 × 0.34 m (a real one is about
  0.6 × 0.5 × 0.33 m, so a little large already), the big Mopar and Ford blocks 0.75 × 0.63 m, the stock disc 0.265 m
  (about 11"), a four-barrel 0.26 × 0.17 m. Only the stock coil spring is small (0.28 m tall, 0.095 m across; real
  about 0.35 × 0.13 m). The user tried 1.2 and liked it, the discs still small, and decided (2026-09-26) the rule is
  for the ported models only: modders are expected to ship parts at the right size for AC. So it is data, not the
  converter: `Assets/Parts/model_scales.json` scales parts by id prefix (`engines/`, `exhaust/`, `suspension/` 1.2,
  `brakes/` 1.35); each part keeps its own scale, so an unscaled mod part on a scaled block stays its size.
  Taken out again on 2026-09-27: the user checked and the models are well sized, so every part is drawn as made.

### Step 16: everything moddable (done, PR #31, merged as `05d5231`)
Idea 7. The game goes open source: the community should be able to drop a mod into a folder and have the game pick it
up, and saves must survive mods coming and going.

**Research (2026-09-26):**
- Picked up by themselves today: cars and tracks (AC's `content`), engine sounds (`Assets/Sounds/<folder>`, plus banks
  harvested from AC cars), part packs (`Assets/Parts/**/pack.json`; the later pack wins a clashing id, only logged).
- One shared file to edit: `engine_builds.json`, `part_aliases.json`, `slot_shifts.json`, `Assets/Dealers/dealers.json`,
  `Assets/Opponents/opponent_definitions.json` (new racers do join old saves, `EnsureNewcomers`),
  `Assets/Showcase/scenes.json`.
- Written in C#: events, milestones and victory paths (`RaceEventService.CreateDefaultEvents`), the newspaper
  (`NewsWriter`), the talk (`StaticTalkService`).
- Streets: only the first folder of `Assets/Streets` is ever used (`StreetScenes.LoadFirst`).
- No mods folder, no merging of several roots; every path is fixed under `Assets` or the AC install.
- Saves: unknown part ids are kept and skipped (they may come back), renamed ones follow `part_aliases.json`; removed
  cars become `Legacy`; racers live whole in the save. A dropped dealer leaves listings pointing at nothing; events and
  streets have no handling.

**Design to discuss when the step starts:**
- `Mods/<mod>/mod.json` (id, name, version, author), one subfolder per content type (parts, builds, dealers, racers,
  events, streets, gas stations, sounds, showrooms, text).
- The base game is loaded through the same loader as the first mod, so the path is always exercised.
- One file per entry instead of a shared list; the shared files (builds, aliases, dealers, racers) merged from every
  mod.
- Ids namespaced by mod so two mods cannot clash; clashes reported, not silently overwritten.
- Events, milestones, newspaper and talk lines move from C# to data.
- A save records the mods (and versions) it was made with; loading warns about missing ones, and every content type
  handles a missing id the way parts and cars already do.
- Open: where the folder lives (next to the game or in Documents), and whether streets get picked at random or by
  the clock.

**The user decided (2026-09-26):** the folder is `<game>\Content` ("like AC"); one PR for all of it, events,
milestones, victory paths, newspaper and talk included; ids namespaced by mod with an explicit override
(`"replaces"`); streets picked by the clock.

**As built (2026-09-26), tested by the user in the game before the review fixes** (for modders: `docs/modding.md`):
- `Mods/`: `ModSet` finds `Content\<mod>\mod.json` (the base game `Content\streetcorsa` first, then by `order` and id;
  `_`/`.` folders off; bad ids, `car`, duplicates left out and logged); `ModIds` (qualify, resolve: a mod's own entry
  first, then the base game's; a full id means exactly that); `ContentRegistry` (merge by full id, `replaces` on
  purpose, problems per line); `ModJson` (a file is a list under its name, a list, or one entry).
- All of `Assets\{Parts,Sounds,Dealers,Opponents,Showcase,Streets,Garages}` moved to `Content\streetcorsa\{parts,
  sounds,dealers,racers,showcase,streets,garages}`; `Assets` keeps the UI's pictures. The csproj copies `Content\**`.
- Parts: `PartsCatalog.Load(roots)` reads every mod's parts folder (packs, `_scripts`, builds, aliases, model scales,
  each about the mod's own parts); `PartDefinition` knows its `ModId`, `LocalId`, `PackFolder` (the model is found
  next to its pack.json, no longer from the id) and `ScriptRoot`; `ScriptClassLoader` searches several roots. The
  shop's `engines/` check and the sound matcher's family go by `LocalId`. Slot shifts stay one base-game file by full id.
- Sounds: every mod's sound folders, namespaced; pins resolved against the parts. Dealers, racers, showcase scenes
  merge from every mod. Racer portraits are saved as a path under Content; a save's `/Assets/Opponents/...` still
  resolves (`Portraits`). Base racers keep their GUIDs (unprefixed ids).
- **A new dealer no longer respawns the whole market** (it did: `IsMarketStale`); only its lot is stocked.
- Streets: every mod's; `"times"` in street.json (default: the lights it has models for); `StreetScenes.Pick` keeps
  the street while it fits the light, else a random one that fits.
- Career as data (`CareerContent`): `events/events.json` (car filters by `type`), `milestones/milestones.json`,
  `victories/victories.json` (`DataVictory`: unlock/goal steps `counter`, `beatKing`, `beatAll`, `mostWins`, text
  templates). The five victory classes are gone; the King comes out when the "King" path opens (none: from the
  start). Event prizes may be `part:<id>`.
- Words as data (`GameText`, `text/*.json`: `lines` add to a pool, `replace` rewrites it): the paper (`news.*`),
  talk and challenge replies (`talk.*`, `replies.*`), street talk (`street.*`), names (`names.*`). The rules picking a
  pool stay in C#. A pool of one draws no random number, so the base game reads the same for a seed as before.
- Saves: `GameState.Mods` stamped on every save; loading without one asks first (`ModsInSave.Warning`), then
  `ModsInSave.Tidy` drops the invitations, dealers and lots of mods that are not installed (never what an installed
  mod merely failed to read). Portraits are saved by mod id, not folder. The log lists the mods at start-up.
- Tests: the test project copies the base game's JSON into its output (`Content\streetcorsa`), so the services'
  installed defaults work; `ModsTests.cs` covers the loader, parts/sounds/dealers/racers/streets of two mods, the
  career and text, and saves. 825 tests pass; the game started on the moved content with the same counts (1521
  parts, 161 builds, 8 sounds, 10 dealers, 61 racers, 5 main-screen rooms).
- Review fixes (/code-review xhigh, 2026-09-26): a `null` or a bad value in a mod's entry leaves out only that entry
  (it crashed start-up, or dropped every dealer or scene); showrooms only from `showrooms` lists; one-entry files may
  use `id`; a street without `times` needs no model for every light, and the Cruise screen keeps a street in its
  nearest light when none fits; a mod's scripts find its own shared classes first; a locked path's goals are never
  done (progress stays the average of all steps); victory texts use `GameText.Fill`; the old single-folder loaders and
  `AppSettings.GaragesPath` are gone. 832 tests pass.
- Not done: the dealer map picture is still a compiled resource (a mod's dealers need pins on the LA map); the
  garage's `slot_shifts.json` is one base-game file; mod problems are in the log only, not shown in the game.

### Step 17: the engine stand (done, PR #32, merged as `86daf2f`)
Idea 3. Peripherals, manifolds and even the heads can be worked on in the car; the internals only on a stand, and in
the proper order: no crankshaft before the rods and pistons are out.

**The user decided (2026-09-26):** strict order for the internals, part by part. Peripherals can still come off as an
assembly (a manifold with its carb).

**Research (2026-09-26):**
- Nothing enforces an order today: `Workbench.Remove` takes a part off with its whole subtree (pulling the crank takes
  the rods, pistons, flywheel and clutch), and the workbench only warns how many parts come with it.
- The tree (GM small block): the block holds the crank (rods, then pistons; flywheel, clutch; gear; damper), the
  camshaft, both heads (valve covers, headers), the oil pan, the intake (carb, air filter, distributor), the
  transmission, the timing cover (water pump, fan), alternator, oil filter. The heads and the pan are the rods'
  neighbours, not their parents: blocking by neighbours needs a small rule table ("rods and pistons need the heads
  and the pan off").
- The whole engine can already be pulled to the shelf, but nothing mounts onto a shelf part (`FindPlaces` searches the
  car only) and there is no stand. `PartKinds` has no internal/peripheral split. Work costs 5 min a part (at most 30),
  no money.

**Design:** `PartKinds` gets an internal group (crankshafts, rods, pistons, camshafts; the rest are peripherals); a
stand in the garage holds one engine (a simple model of our own); pulling and refitting the engine is a big job (about
`GarageWorkMajor`); internals come off one at a time, only with nothing on them and their neighbours off; the
workbench mounts onto the engine on the stand as it does onto a car. Ordering rules are data, so mods can supply
them for their blocks.

**The user decided (2026-09-26, when the step started):** the stand is a view of its own in the garage ("Stand",
next to "Parts": the car goes, the engine sits alone on the stand, the same click-to-select and click-to-mount);
the internals are the crankshaft, the rods, the pistons and the camshaft (stand only, one at a time, nothing on
them); full realism for the peripherals too (a rule table: the intake needs the distributor off, the heads the
intake, the flywheel the transmission...); a block on the shelf cannot be taken apart, only put on the stand (other
assemblies still come apart there); every garage has a stand from the start, free.

**Findings (2026-09-26):** only the big parts have a framework class; timing covers, chains and gears, dampers,
distributors, water pumps, fans, belts, alternators, valve covers are plain `EnginePart`/`Part` in every pack, but
their names are consistent across GM, Ford and Chrysler. So the rules go by a **work kind**: a mod's own `"kind"`
on the part first, then the framework class, then name keywords (longest first), all data. A stand holds a block
by its bellhousing face, so the transmission, clutch and flywheel come off (to the shelf) when an engine goes on it.
The rules work both ways: what has to be off before a part comes off has to be off before it goes back on.

**As built (2026-09-26), not yet seen by the user in the game** (docs: `docs/systems/parts-system.md` "The engine
stand" and "The work order", `docs/modding.md` "The work order"):
- `WorkOrder` (`Content\streetcorsa\parts\work_order.json`, merged from every mod): 38 kinds (classes first, then
  name words in list order, whole words, plural s allowed; a pack's `"kind"` wins), 20 rules, `stand_strips`
  (transmission, clutch, flywheel, and the radiator, which stays with the car). `EngineWork.WhyNotOff`/`WhyNotOn`
  give the reason in words; `StripForStand`.
- `Player.EngineStand` (the save); `CarPartsService.BringUpToDate` renews it like a car's engine (a missing mod's block
  waits on the shelf).
- Workbench: Stand toggle next to Parts (`IsCarView`/`IsStandView`), "Pull the engine" (onto the stand when free,
  else the shelf, 2 h), "Off the stand" (20 min), "Put it in the car" (2 h), shelf "Put on the stand" (20 min, not
  "Take apart" for blocks); the card shows why a part cannot come off, the shelf hint why a part cannot go on.
- 3D: `EngineStandLayout` (crank 0.78 m up, front to +Z, head at the block model's rear bound, a stand of red square
  tube boxes on casters, no part node). **The user asked (2026-09-27)** for the stand to be always visible beside the
  car, 3 m to its left, with the engine on it as it is, and the camera to turn to it when the stand is picked: so the
  stand is scenery (`SetScenery`), the car stays solid in the Stand view, the engine on it becomes the pickable prop.
  Works in an empty garage too. Checked with an offscreen harness (GM 350 beside the Camaro: front, side, stand view).
  Then (2026-09-27): a click on the stand or its engine opens the Stand view (hover glow and label), and the camera
  glides back from the stand to the car without the renderer's framing jump (checked tick by tick in the harness);
  in the Stand view a click on the car opens the car's Parts view (the user's "the other way around").
- Tests: `EngineStandTests` (kinds of a GM 350, car vs stand, strip, mount reasons, mod kinds and `replace`, the
  workbench flow, and every one of the 161 builds down to the bare block on the stand and back). 1000 tests pass.
- Not done: the engine can still be started (and rocks the stand) from the engine panel while the stand is shown.
- Review fixes (2026-09-27, /code-review xhigh, 15 findings): stand work and view toggles no longer re-evaluate the
  car or stop its running engine (`CarEngineChanged` event, raised only for work on the car); `WhyNotOff` asks the
  rules of the whole subtree, as `WhyNotOn` does (a head with a stand-only camshaft on it stays in the car); blockers
  named once; the stand's tree is cached and only rebuilt when the stand changed, and a stand that fails to build no
  longer stops the car's view; the engine moves between scenery and prop in one frame (`LeaveStandAsync`, the stand's
  scenery built with the prop); the solid car hides stand parts from picking; the ghost car is drawn against the
  stand's depth; the car's outline fades back with it; car hit tests use its bounding box; `BoundsOf` skips textures
  and catches any read failure; the stand's boxes and materials are built by `PlaceholderMesh`. 1162 tests pass. The
  3D changes are not checked in the game or the harness.

### Step 18: engine layout, and the Chevrolet Corvair (done, PR #35, merged as `b89bf7e`)
Idea 4. The Corvair's engine is in the back.

**The user decided (2026-09-26):** the Corvair keeps a V8 as now (the matcher gave it a GM 283), but drawn in the
back; a flat six will be modded later.

**Research (2026-09-26):** both `chevrolet_corvair_69` and `_s1` are installed and in the catalog, RWD,
`CG_LOCATION=0.38`. No flat or boxer block exists in any pack (25 `Block_Vee_OHV`, 2 `Block_Inline_OHV`).
`CarPartsLayout` puts every engine 0.18 m behind the front axle, facing forward; nothing reads the engine's place from
AC's data, and `StockEngineMatcher` ignores layout and cylinder count.

**Design:** engine layout as a general car property (front, mid, rear), guessed from the car's data (front weight
share, drive) and set in the Car Catalog Editor; `CarPartsLayout` places the engine by it (behind the rear axle,
transmission forward, for a rear engine), and so do the x-ray view and the mount candidates. **The user decided
(2026-09-27):** a mid engine sits ahead of the rear axle facing the same way as a front engine, transmission pointing
back; only a rear engine is turned around. No Corvair special
case: any rear- or mid-engined car works the same. When a flat-six pack exists, the matcher should prefer it by
layout and cylinders.

**Built (2026-09-27, `feature/engine-layout`):** see `docs/systems/parts-system.md` "Engine layout". The guess is
front-wheel drive or 44%+ on the front axle = front, else rear (mid can't be told from the weight: set by hand); of
the 167 installed cars only the two Corvairs come out rear. Checked in an offscreen render of the Corvair in all
three layouts; not yet seen in the game. Left for later: the matcher preferring a flat six by layout and cylinders.

### Step 19: the gas station (done, PR #36, merged as `e328923`)
Idea 6. The player drives to the gas station, as in Street Rod, with its "cruising in town" view from inside the car.

**The user decided (2026-09-26):** filling up only at the station (the garage's fill-up goes); the car washes move
there too.

**Research (2026-09-26):** no AC track mod was found with a period gas station. The way is the street's: our own KN5.
Candidates (CC-BY, credit needed; check each page again before shipping): WardsterSAW's "Vintage Gas Station"
(50s–60s, about 6k faces), IcaroArrigoni's "50's Abandoned Desert Tank Station", NickSamarin's "Vintage Gas Station",
Rescue3D's "Vintage Gas Pump"; CC0 dressing from Poly Haven (`metal_jerrycan`, `oil_tin`, `barrel_03`, `old_tyre`,
`tire_pump`). Oil-company logos must be repainted with made-up brands: CC-BY does not cover trademarks. No CC0
gas-station HDRI exists.

**Design:** a new place built on the street's renderer (`StreetViewport3D`, `RivalDrive`, the two-channel
`EngineAudio`): the cruise there seen from the driver's seat, the car rolling in to the pump, filling up and washing
there. A station is a mod content type (step 16). Open: an empty tank on the way (a tow, as after a crash?), and
what else the station sells (oil, a quick check-up).

**The user decided (2026-09-27):** a car too dry to get there is towed in; the station sells oil too, and the attendant
gives plausible hints about the car's condition ("your radiator would need a replacement shortly"); the scene is a gas
station out of an installed AC track, not shipped ("we won't publish it"): Bannochbrae's, by the start line.

**Built (2026-09-27, `feature/gas-station`):** see `docs/screens/gas-station.md`. `tools/StationScene` cuts the station
out of the player's Bannochbrae (250 m round its gas-station sound, race-day dressing left out) with a Poly Haven sky
per light and a ground-height grid; the KN5 and the grid are gitignored, `station.json` is ours. The car drives in off
the road from the driver's seat (`PathDrive`: a curve through the station's points, slow in the bends, braking to the
pump) and out again. At the pump: fill up, top up the oil (by the quart), wash, a quarter of an hour each; the
attendant checks the oil and names up to three worn parts he'd notice. A tank under 0.8 l brings the tow truck (25
dollars, an hour). The garage lost its fill-up and wash; engines now burn oil race by race (more when worn), and a low
sump surges sooner in the race mode. Cars now sit on the road as AC has them (a model's origin is not always on the
road: the GTO's tyres were half in it), the driver's eyes placed by the steering wheel for every car (the mods' own seat some
drivers on the wheel), through AC's onboard lens; the street too. After the user's first look: fades in and out, a progress bar while the pump runs, the engine off at the pump and
started to drive off, smoother (centripetal) curves. Checked in offscreen renders and a viewport harness; not yet seen in
the game.

## Steps 20–23: from `docs/ideas.txt`, batch 3 (planned 2026-09-27)

The third batch of the user's ideas. **The user decided (2026-09-27):** as before, a small polish PR first, then one
step per idea, one at a time, in this order. Details of each are settled when it starts.

### Polish 2: init screen, shadows and dealer pins (done, PRs #37 and #38)
**Built (2026-09-27), user tested in game:** the init screen is now the main screen's title (`IntroView`), shown at
least 4 s and until the first room fades up behind it (15 s at most). Eastside Garage moved toward Montebello,
Industrial Motors to the City of Industry. Shadows: the main screen's cars kept their bounding boxes where they first
loaded, so the shadow cascades (centred on the main slot's box, drawing only cars whose box they take in) missed some
cars; `GarageRenderer.RefreshBounds()` after the layout fixes it. The dealer lots had no bug: a roofed showroom shades
its whole floor, and the shadows soften away from the main slot (sharp cascade about 10 m, none past about 23 m).
Aiming the sharp cascade at the car looked at would need a patched actools (`ShadowsPosition` is not virtual).

- **Init screen** (idea 3): shown for a fixed time before the main screen, and the main screen's cars and scene load
  behind it, so the menu never shows over a black screen while the cars render.
- **Shadows** (idea 4): some cars have no shadow, or a bad one, at the dealers and in the main screen's cinematics;
  find out why and fix it.
- **Dealer overlap** (idea 2, first half): Eastside Garage overlaps Downtown, Industrial Motors overlaps Vermont, on
  the dealer map (`Content\streetcorsa\dealers\dealers.json`); move them apart.

### Step 20: dealers come and go (done, PR #39, merged as `a16fc7e`)
Idea 2, second half. More dealers, and now and then one goes out of business or a new one opens; most stay in
business all game. Built like the opponents' churn (step 12). Needs the dealer map moddable, which step 16 left out
(the map picture is still a compiled resource), so a mod's dealers get pins on the map.

**The user decided (2026-09-27):** 20 dealers, about 12 open at a time; a change about every two months; a closing
dealer holds a closing-down sale announced about a week ahead, and the other racers are eager to get the bargains; which
lot goes under depends on how it is doing (cars sitting unsold, sales to the player and the rivals), still with chance.

**Built (2026-09-27, `feature/dealer-churn`), played by the user:** see `docs/systems/market-system.md` "Dealers
Come and Go".
- Ten new dealers in `dealers.json` (Fairplex Auto Mart in Pomona and Firestone Boulevard Motors in Downey open from the
  start, the other eight `"startsOpen": false`), placed so no pins overlap.
- The save's `DealerLocation` carries the business (`Status` Open/ClosingDown/Closed/Waiting, `OpenedOn`, `ClosesOn`,
  `ClosedOn`, `Trade`); `DealerRoster.Sync` merges the definitions over it instead of replacing the list.
- `DealerLifeService` (the daily `dealer_review` task, first): trade counting, the sale (20% off, 35% in the last three
  days, no restocking, shut after seven days), a change about every 60 days toward 60% open, never under 40%. The paper
  and the diner report it (`Content/streetcorsa/text/dealers.json`).
- The rivals shop the sales (`OpponentLifeService.ShopTheSales`): trade-ups at 10% more power, flips at the deep cut,
  at most two cars a day.
- The map pin and lot header show the sale; the price card strikes through the old price. Trade-ins only go to open lots.
- The dealer map is content: `Content/streetcorsa/dealers/map.json` (a `maps` list: picture and crop anchor) with the
  picture moved there from `Assets`; the last mod's map wins.

### Step 21: part dealers (done, PR #40, merged as `738a19d`)
Idea 1. Parts shops on the map like the car dealers, specialised by make (a Mopar shop, a Ford shop) or by kind
(speed shop, junkyard with used parts), each with its own prices and stock. After step 20: it reuses its map and churn.

**The user decided (2026-09-27), started on `feature/part-dealers`:** all four kinds of shop, pinned on the dealer map
with their own pin style and a car dealers / part shops / both toggle:
- **Make specialists** (a Mopar shop, a Ford shop, a GM shop): new parts for the makes listed in the shop's data (no
  make-specific code, so a mod's make gets its own shop), about list price, deep stock, weekly restock.
- **Speed shop**: performance parts for any make (carbs, blowers, cams, headers, intakes), above list, thin stock with
  the odd top-end part.
- **Wheel and brake shop**: running gear (tyres, rims, brakes, springs, shocks), under mail order, wide stock.
- **Junkyard**: used parts at any wear, whole engines and cores, cheap; buys the player's old parts above trade-in.
- **Stock is limited** and each shop restocks on its own schedule; the rivals buy from the shops too.
- **Mail order stays, at a premium and with a few days' delivery**; the newspaper's used-part ads stay as they are.
- Proposed with it and built: a regulars' discount, shops opening and closing with step 20's cycle (closing sales the
  rivals shop), shops as content in `Content\streetcorsa\partshops\`; about 10 shops, 6 open.

**Built (2026-09-27, `feature/part-dealers`), not seen in the game yet:** see `docs/systems/parts-system.md` "Part shops".
- Ten shops in `Content/streetcorsa/partshops/partshops.json`: Hemi Hut (Mopar), Blue Oval Parts (Ford), Bowtie Supply
  (GM), Bellflower Speed & Custom, Foothill Tire & Brake and San Pedro Auto Wreckers open from the start; South Bay Speed,
  Inland Tire & Brake, Sun Valley Salvage and Fontana Wrecking wait to open. Pins checked on a render of the map.
- A pack says its make (`"make"` in `pack.json`: the base game's chrysler `mopar`, ford and ford_six `ford`, gm `gm`); a
  specialist stocks every pack of its makes, a mod's too.
- `PartShopService`: stock (shelves of new parts that run down; a junkyard's yard of used parts and whole engines, each
  priced), the shop's prices, the regulars' discount (5% from $500 spent there, 10% from $2,000), haggling at a
  junkyard (once a part, half the time 10-25% off), selling to a shop (a junkyard pays 42% of worth and sells it on, never under 10% over that).
- `PartShopLifeService` (the `partshop_review` task, after the dealers', before the rivals'): deliveries, and the
  dealers' open/close rules; a closing-down sale takes 20%/35% off and the bargain hunters clear out a fifth a day. The
  town's other customers buy a little every day and a delivery changes a third of the lines; a shop's trade is the town's
  custom (the same for all) plus what the player and the rivals buy, so a junkyard only the player shops at is no likelier
  to go under than any other. Two simulated years: about six open, closings spread over the kinds.
- The mail order costs 20% over list and comes in three days (`Player.PartOrders`); the garage says what came.
- The rivals tune at the cheapest shop that has a part (`EngineTuner.TuneUp`'s `newPrice`), take it off that shelf, and
  tune twice as often while a part shop's sale is on.
- The shops have their own map (the dealer map's picture, gold square pins), from a Parts dealers button beside Car
  dealers in the garage (the user's call, 2026-09-28: car and part dealers kept apart; the button's picture made with
  ComfyUI, juggernautXL, seed 101, and PIL lettering). `Screens/PartShop` is the counter.

### Step 22: flip the engine on the stand (done, PR #41, merged as `dda3a18`)
Idea 5. The engine turns upside down on the stand with an animation, to fit the crankshaft, bearings and oil pan; the
part moves turn with it. Cosmetic only: nothing is refused when the engine isn't flipped.

**The user decided (2026-09-28), built on `feature/engine-flip`:** a "Turn over" toggle beside Parts and Stand, shown
while the stand is worked on with an engine on it; picking a part worked on from below (on the engine, or from the
shelf) turns the engine over by itself, one worked on from above turns it back; the camera stays put; a plain half
turn about the crankshaft's line.
- Which side a kind is worked from is data: `"worked_from": "below"` (crankshaft, main bearing bridge, oil pan) or
  `"above"` (pistons, heads, intake, carb, air filter, blower, valley and valve covers, breather, distributor,
  ignition) on the kind in `work_order.json` (`WorkKind.WorkedFrom`, `WorkOrder.SideOf`); the last mod to say so says
  so. A kind that does not say leaves the engine as it is, so the block, rods, camshaft and the front of the engine
  never turn it.
- `PartsWorkbenchViewModel.IsEngineTurnedOver`: false again when the work leaves the stand, and when another engine (or
  none) is on it.
- `GarageRenderer.EngineTurnedOver` turns the stand's prop and its candidates (their `LocalMatrix`) about the spindle
  (x = `EngineStandLayout.Spot.X`, y = `CrankHeight`, along Z) in 1.6 s; the nodes' own matrices are untouched, so
  part moves, picking, the placement mode and the parts' world places all work as before, in the prop's space.
  `MoveParts` now works "out" and the unknown kinds' "away" in the prop's space too (it mixed the garage's and the
  prop's, which was harmless while the only prop matrix was the running engine's small lean).
- Off the stand the engine is upright: `LeaveStandAsync` turns it back (while the scenery is built) before it goes
  back into the scenery or onto the hoist, and `StandActive = false` snaps it upright whatever happened.
- Seen in the offscreen harness (small block: upright, half way, over; the air cleaner hangs just clear of the stand's
  legs). Not seen in the game yet.
- Review fixes (`/code-review xhigh`, 13 findings, all fixed):
  - The turn is eased legs toward an angle (`_turnAngle`), no longer a 0..1 progress. It goes only as far as the
    engine clears the stand's base (`TurnLimit`, from the part nodes' boxes swept about the spindle, worked out on every
    `SetProp`): a blown engine only goes over onto its side. A limit that drops (a blower put on while it is over) turns
    it back from where it is.
  - The engine does not turn while parts come off it (`IsHoldingTurn`), and `ApplyStandAsync` waits for a turn to end
    before parts come off or the new model goes up. So "Off the stand" while it is over turns it upright first, then
    lifts it off, and a part's way is never turned under it.
  - Waits go by progress with a stall patience (`WaitForRendererAsync`, shared with the part motions,
    `GarageRenderer.TurnSecondsLeft`), no fixed 2.5 s window. The garage is locked (`IsMovingEngine`) while the engine
    turns back before the hoist.
  - The stand's head (plate, cross, spindle) is its own node, `EngineStandLayout.HeadNode`, turned with the engine;
    `EngineStandLayout.Spindle` is the one place the axis is; the prop's placement is kept through the turn and the
    body's lean.
  - One rule for upright each way: the view model resets in `Refresh` only (the stand not on show, or another engine),
    and the renderer turns only while `StandActive`, snapping upright when it goes off. `IsTurningEngine` uses the same
    target, so a flag left on off the stand no longer keeps the viewport drawing.
  - `WorkOrder.Named<T>` serves both the ways and the sides.
  - Harness: the head turns with the engine, and the small block still goes the full half turn. A blown engine was not
    rendered, because no build has a blower.

### Step 23: the player's car lot in 3D
Idea 6. A 3D scene instead of the list. Options to propose when the step starts: a warehouse with the cars parked in
rows under lights (the showroom's renderer), or an outdoor lot with a fence, lit by the time of day (the street's).

**The user decided (2026-09-29), built on `feature/car-lot`:** the warehouse; our own scene, written as a KN5 (not an AC
showroom); the car's card takes the car (the old list's pick, 15 minutes, back to the garage), starts and revs its
engine, sells or advertises it (the garage's Sell, without taking the car first) and tells everything about it. A
warehouse holds a fixed number of cars and there is no limit: more cars fill a second warehouse, and so on; the player
moves between them and can jump to the warehouse a wanted car is in. Warehouses look different from one another by
changing the one scene's pictures, not by building more scenes.

**As built** (see [screens/car-lot.md](screens/car-lot.md)):
- `tools/WarehouseScene`: `prepare_warehouse.py` downloads Poly Haven CC0 textures and draws the enamel sign, its
  digits, the bay paint and the skylights; `WarehouseSceneBuilder` models a 28 x 24 m brick shed on steel trusses
  (ten bays, five a side, noses to the aisle; a roller door, six hanging lamps, skylights) and writes
  `Content/streetcorsa/warehouses/warehouse/warehouse.kn5` (3.8 MB, every look's pictures in it) and `warehouse.json`.
- Five looks (red brick, old factory, whitewashed, block shed, yellow brick), each with its lamps' colour; warehouse n
  wears look n - 1 in turn, and its number goes up on the far wall's sign by pointing two digit plates at other
  pictures. Light by the game's clock (day, dusk, night): the skylights' glow and the key light change, the lamps are
  always lit.
- `LotViewport3D` (was `DealerLotViewport3D`) takes a `Dressing` (materials pointed at pictures, glow, light and
  lamps, applied on the standing scene) and a `LotView` (the scene's view down the aisle, which the camera comes back
  to). Going to another warehouse swaps the cars and the dressing, not the scene.
- The card was dropped (user, 2026-09-29): with a car picked, one button at the bottom middle, Take this car; selling
  and starting it stay the garage's.
- The car the player drives is in the garage, not in a warehouse (user, 2026-09-29): taking a car out parks the one from
  the garage in the bay it leaves (`CarStorage.TakeOut`).
- `Car.Warehouse` / `Car.Bay` keep a car's place (`CarStorage`): new cars take the first free bay from warehouse 1; a
  car coming back keeps its old bay if it is free; of two cars in one bay the one had longer keeps it. New places are
  saved when the lot opens.
- `Screens/CarLot` replaces `Screens/CarSelection`: the warehouse, arrows to the others, every car in a list down the
  side (a click goes to its warehouse and onto it), Take this car at the bottom.
- Mods: a `warehouses` folder, one scene per folder; with several, warehouse n is in scene (n - 1) mod count.
- Checked with offscreen harnesses (the scene through the renderer; the real screen with fake services), not yet in
  the game.

## Step 24: from `docs/ideas.txt`, batch 4 (planned 2026-09-29)

The fourth batch of the user's ideas: the garage's look, two new dialogs and fades everywhere. **The user decided
(2026-09-29):** one PR for all of it.

- **Garage buttons** (idea 1): round, smaller buttons with an icon inside (the user's PNGs in `Assets/Icons`, drawn as
  masks in one light contrast colour, steering wheel and trophy included), a thick outline and a semi-transparent
  background, tooltips; no button pictures. Placement: Quit top left; Hit the streets centre left (steering wheel);
  Newspaper bottom left, Part dealer above it, Car dealer above that; Gas station bottom right; Career stats top
  right (trophy), Radio before it in the same row, Calendar below Career; Car info centre right.
- **No bankroll bar**: the bottom bar and its code go; the money shows as a small readout top right, under the icons.
- **Car name bar**: outlined and partly transparent, the text in good contrast.
- **Car info** (idea 2): a centred dialog with everything about the car and every installed part's condition,
  mileage, fuel and so on. The floating car bar keeps the 3D toggles and the actions; condition, mileage and fuel move
  to the dialog; Preview in Showroom goes.
- **Quit** (idea 3): a centred dialog with the options; the Back and Exit buttons, their pictures and logic go.
- **Fades** (ideas 4, 5): every screen change fades out and in; the loader shows until the scene is fully loaded, and
  its spinner and message fade with the screen. Launching AC fades to the loading screen, and the scene fades back
  when AC closes. Every dialog fades in and out.
- **Game-wide** (note): buttons and dialogs everywhere get the thick outline and semi-transparent background.

**As built (2026-09-29, `feature/garage-ui`), not yet seen in the game:**
- `RoundIconButtonStyle` (icon in `Tag`, drawn as an opacity mask in `UiOutline` cream, amber under the pointer) and the
  icons as `Styles/Icons.xaml`; `quit.png`'s white fill was made transparent so it reads as a mask. The dialog buttons,
  the garage's toggles, tooltips and the dialog card use the new glass/outline colours (`Ui*` in Colors.xaml).
- Every screen's picture back/exit button is a round icon button; the unused button pictures, brushes,
  `ExitButtonStyle`, `OverlayTileButtonStyle` and `Components/BottomBar` are gone. The six screens with the bottom bar
  show the money top right (`MoneyReadoutStyle`).
- `Dialogs/Quit` (Main Menu, Quit to Desktop, Stay) and `Dialogs/CarInfo` (figures, history, every part indented under
  what it is on, with a wear bar). The garage's own exit fade (`ExitTransition`) went: the window does it.
- `MainWindow`: a change of screen takes a RenderTargetBitmap still of the old screen (D3DImage included), shows it on
  black and starts the fade at ApplicationIdle, once the new screen is laid out (building one can block the UI for
  half a second, which ate a fade started at once). Dialog and radio hosts fade with `Reveal`; the closing dialog stays
  in the host while it fades, disabled. A free run fades to a loader (`RacingLayer`) on the launcher's events and back.
- `Controls/LoadingIndicator` (fades out, `WaitFor` a viewport): garage, street and gas station use it; the lots keep
  their own covers.
- Checked with an off-screen harness driving the real game on a copy of a save (stills of the garage, dialogs and
  transition frames).

**Tweaks after the user's first look (2026-09-30):**
- One money card (money, time, date) in the window, top right over every screen of a game in play (`IScreen.InGame`,
  false for the main menu, sound library and catalog editor), read off `App.CurrentGameState` four times a second. The
  screens' own money/time readouts and their view-model properties are gone; what sat top right moved under the card.
- Garage: career, calendar, radio in one column under the card.
- The "now playing" note comes out of the radio button to its left and goes back into it (`RadioToastAnchor`); on a
  screen without one, out of the money card.
- The car radio's AM sound fades in and out over 1.5 s (`FMOD_DSP_SetWetDryMix` on each effect, stepped by the hub's
  pump); the static fades with it. The radio gets into the car when the street is on screen or the station's curtain
  lifts, not at the click; it leaves the car with the screen.
- A free run or a test-and-tune keeps the radio as it was (`App.KeepsRadio`); only a race switches it off.

## Step 25: from `docs/ideas.txt`, batch 5 (planned 2026-10-01)

The fifth batch of the user's ideas: a racing hub, the newspaper as real classifieds, 3D previews, and AI. **The user
decided (2026-10-01):** one PR for all of it, built before E7; every car ad goes in the paper (dealers, private sellers,
rivals); a car ad opens a screen of its own, a part ad a modal; AI supports Ollama, ComfyUI, OpenAI-compatible
endpoints and the Anthropic API.

What the code has today (mapped 2026-10-01): the garage's "Hit the streets" opens the Diner (Cruise is a button
there); race invitations (events from `events.json`) and Street News (`NewsWriter`) are columns of the Newspaper,
next to links to the Used Car Market and Used Parts screens; the car list is virtualized
(`VirtualizingUniformWrapPanel`), not paged, and the parts list stops at 250 rows; no ad has a description; there is
no single-part 3D view and no dialog hosts a viewport; there is no AI or HTTP code, but `ITalkService`
(`StaticTalkService`) is the seam the rivals' lines already go through.

- **Racing hub** (idea 1): the garage's steering-wheel button becomes "Go race!" (text key, not hard-coded) and opens a
  styled modal: the race invitations (entered as today: `EventEntry`, dial-in, race setup), Street News, and a "Hit the
  streets" button that goes to the Diner as before. The Newspaper loses both columns.
- **Newspaper** (idea 2): the classifieds only, two sections, used cars and used parts, laid out like a period paper's
  ads (dense small type, bold run-in headline, ruled columns, newsprint), endless scroll that adds ads page by page.
  Every car ad is there, dealers' included; the dealer filter and its code go, and the Used Car Market screen is folded
  into the paper. The player's own car ads and their offers stay in the cars section, at the top. Mail order and
  selling parts stay with the part dealers.
- **Ad blurbs**: a short, varied description on every ad (`Description` on the listing and part ads, written once and
  saved): a standout part ("327 V8, dual quads", "blower"), "all original", or a character line ("grandma's car,
  church on Sundays"), from text pools; not all condition and mileage.
- **Part preview**: a part ad opens a modal with a small 3D viewport of the part alone (a new control on
  `D3DViewportBase`, its KN5 from the parts catalog), turned and zoomed with the mouse.
- **Car preview**: a car ad opens a screen of its own, "you go see it": the car at the seller's curb (the street
  scene) or on the dealer's lot, orbit camera, Buy and Back. A car needs the whole window, the lot viewport and street
  scene can be reused, and a 3D scene inside a fading modal is untried.
- **AI** (idea 3): an AI section in Settings, off by default: text provider (Ollama, OpenAI-compatible, Anthropic),
  endpoint, model, key (encrypted with DPAPI, never plain in `settings.json`), a Test button; ComfyUI address and a
  workflow for images. The game never waits on it: the built-in text is always there, AI text is made in the
  background, kept in the save, and a failure falls back quietly. Uses: rival talk (`ITalkService`), ad blurbs, a
  dealer's word on a car, news stories, the Diner's street talk; ComfyUI portraits for new rivals (as in step 12) and
  maybe newspaper photos. Prompts and ComfyUI workflows are mod data (`ai/` in Content), so a game mod has its own voice.

**As built (2026-10-01, `feature/ideas-batch-5`), the owner tested it:** three parallel branches merged. After the
first look: the paper fills the screen in bigger type, no Buy on the ads (a car is bought only after going to see it,
a part from its look), the car stands on its tyres, the camera walks round it, a garage button under the back one.
- Racing hub: `Dialogs/RaceHub` (`NavigationService.ShowRaceHub`), the garage's wheel button says "Go race!"; the hub
  withdraws itself before the event entry; invitations load through `RaceInvitations.Load`.
- Newspaper: "The Street Trader", AUTOMOBILES FOR SALE and PARTS & ACCESSORIES, newsprint theme keys (`NewsprintFont`,
  `NewsHeadFont`, `Newsprint*` colours), `ClassifiedColumnsPanel` (an ad goes in the shortest column, so ads never
  move as pages arrive), 30 ads a page. The Used Car Market screen and the dealer filter are gone;
  `UsedCarListingViewModel` is in `Screens/Shared`. `Screens/UsedParts` is now the mail order (New and Sell tabs),
  reached from a "Mail order" button on the part shops' map. Headlines and listing names say the make once
  (`CarNames.MakeAndModel`: "AMC Javelin", not "American Motor Company AMC Javelin").
- Blurbs: `Description` on `UsedCarListing`, `RivalCarAd`, `PartAd`, from `text/ads.json` (`AdBlurbs`); old saves'
  ads get theirs when first shown. Only parts that did not come from the factory are bragged about, a blower always.
- Previews: `Controls/PartViewport3D` in `Dialogs/PartPreview` (the part with what is bolted on it, a dark studio,
  turns until touched); `Screens/CarViewing` with `Controls/CarViewingViewport3D`: a dealer's car alone in its
  showroom, anyone else's at the curb of a street lit for the hour (the same street per ad), a rival's ad bought from
  the rival; the engine panel, Buy, Back to the paper, the ad's blurb, and "What do you make of it?" when the AI is on.
- AI: see `docs/ai-integration.md`. Tested live with Ollama `llama3.2:3b`; ComfyUI, Anthropic and OpenAI-compatible
  only against fake servers.

## Step 26: from `docs/ideas.txt`, batch 6: talking to people (planned 2026-10-01)

The sixth batch, all for when the player's AI is on: call whoever placed an ad (a car or a part) and talk to them;
talk to a car dealer's salesman, who sells with all the jive he has but answers true; talk to the rivals on the
street, who remember longer. Each person has a personality, remembers past talks for a while and then forgets, and
answers about what is for sale and about past races with the truth ("I raced you last week!" "Sorry, dude. I don't
remember you. Do you want this valve cover or not?"). **The user decided (2026-10-01):** this is next, before E7; one
PR; talk can change a deal, but the game decides the price and the AI only says it; rivals can be talked to at the
Diner and at the Cruise curb; races between rivals start being recorded so they can talk true about them too.

What the code has today (mapped 2026-10-01): only rivals are people (`Opponent`: id, name, age, gender, aggression,
biography, grudge). A private car seller has no identity (`UsedCarListing` with no `DealerLocation`); a part ad's
seller is a name made up at random (`PartAd.SellerName`, or `SellerRival`); a dealer is a business with no
salesman (`DealerDefinition`, `DealerLocation`). The player's races are saved (`ProcessedRaceSession`: date, opponent
by name, won, pink slip); races between rivals are only text in the street talk and the paper. The AI is one question,
one answer (`AiTextRequest(System, Prompt, ...)`); nothing remembers a talk; the game has no chat window.

- **People:** a `Person` in the save (id, name, age, sex, a temperament and a way of talking from the mods'
  `people/temperaments.json`, the person's kind: private seller, salesman, shop clerk, rival). Made once, when first
  needed, from the ad's seed so an ad keeps its seller: `UsedCarListing.SellerId`, `PartAd.SellerId`, a salesman per
  dealer (`DealerLocation.SalesmanId`; a dealer's mod data may name one) and a clerk per part shop. A rival is its own
  `Opponent`; the person record only holds what rivals lack. Salesmen lean to the jive talkers.
- **Chat with the AI:** `AiTextRequest` carries a list of turns; the three providers send them as messages. `IAiText`
  gets `ChatAsync(use, values, turns)`, always the player's priority, with the same queue, timeout and fallback.
- **What a person knows (the fact sheet):** sent with every turn and filled by the game, never by the AI: who they are
  and their temperament; what is for sale (the car's make and model, engine and parts as the engine panel shows them,
  condition, mileage, owners, wins, days on sale, the asked price; a part's name, condition, what it fits, price); the
  races they had with the player (`ProcessedRaceSession` by name: when, where, who won, pink slip) and, for rivals,
  their recorded races with other rivals; what they remember of past talks; the deal on the table. The prompt says to
  answer only from it, to say "don't know" otherwise, and to steer back to the sale when the talk wanders.
- **Haggling:** when the player names a price (a number in what they typed: "1200", "$1,200", "1.2k"), the game
  decides before the AI answers: take it, counter, or refuse (rules `talk.haggle`: the most a seller comes down, more
  for a private seller who wants it gone, a car long on sale, a gruff or broke rival less, a salesman who opens high
  and comes down in steps, never below what the game says it is worth less a margin). The decision goes in the fact
  sheet ("The player offers $1,200. You counter at $1,400.") and the AI only says it. An agreed price is a deal
  (`GameState.Deals`: listing or ad, price, until a date) that Buy uses; it is shown in the talk window.
- **Memory that fades:** when a talk ends (with at least one thing said by the player), a background request writes
  one line from the person's side ("The kid asked about the 327's compression, offered $900, I said no."), kept with
  the game date in `GameState.Memories`. Only the last few are given back, and they are forgotten after
  `talk.forgetDays`: about 14 days for a private seller or a clerk, 30 for a salesman, 90 for a rival. Deals and races
  are facts from the game, not memories, so they are always told true.
- **Rival races recorded:** `RaceSimulatorTask` saves each rival-vs-rival race (`RivalRaceRecord`: date, both names,
  cars, winner, pink slip) in the save, the oldest dropped past a cap.
- **The talk window:** `Dialogs/Talk`, a modal in the game's look: the person's name, portrait (a rival's, else none)
  and what they are (on the phone, at the lot, at the curb); the talk so far; a text box and Send; the deal, if any;
  Hang up / Goodbye. While the AI writes, "..." on their side; if it fails, a line from the text pool ("The line went
  dead.") and the talk can go on. The AI writes in the player's language (the persona says so).
- **Where:** "Call the seller" on the car viewing screen (the "What do you make of it?" button becomes the talk: the
  seller's first words are their take) and in the part preview; "Talk to the salesman" for a dealer's car; "Talk" at
  the Diner for the picked rival and at the Cruise curb before the player accepts or waves off. All shown only while the
  AI is on.
- **The salesman talks first** (the user's tweak, 2026-10-01): on the dealer's lot he is not waited for. As the player
  arrives he welcomes them and talks up what he has on the lot; when the player picks a car he goes on about it without
  being asked, and the player can put in a question at any time. The talk is a panel on the lot screen (and on the car
  viewing screen for a dealer's car), not a modal: the same talk control the window uses. He remembers what he said
  about each car (kept per car with the person, this visit and the last ones), so coming back to a car after looking at
  others he says something new, not the same pitch. Picking cars quickly asks only for the last one.
- **Ollama tuned for the game** (the user's ask, 2026-10-01; no training, a Modelfile): a `streetcorsa` model made from a
  base the player has (llama3.1:8b advised on a 12 GB card) with the game's persona as its system prompt, its
  parameters (temperature, repeat penalty, a context big enough for a talk and its fact sheet) and a few example
  exchanges in the period's voice. The Modelfile is mod data (`ai/ollama.json`: base, parameters, examples), made in
  Ollama from Settings with one button (`POST /api/create`), then picked as the model. What the game sends Ollama is
  tuned too: `num_ctx` for talks, `keep_alive` so the model stays loaded between lines, stop words.
- **Mod data:** prompts `chat_seller`, `chat_salesman`, `chat_clerk`, `chat_rival` (each a persona of its own on top of
  the game's) and `chat_memory` in `ai/prompts.json`; temperaments in `people/temperaments.json`; window words in
  `text/ui_talk.json`; figures in `rules/ai.json`'s `talk` (turns kept, memories given, forgetDays per kind, haggle).

**As built (2026-10-01, `feature/talking`), not played yet:** see `docs/ai-integration.md`, "Talking to people".
Changed from the plan: no shop clerk (the paper's part ads are private sellers or rivals; the shops are not called);
no seller ids on the ads: a person is found by the ad's key (`car:<listing>`, `rivalcar:<ad>`, `part:<ad>`,
`dealer:<id>`, `rival:<opponent>`) and made from a seed of it; temperaments are `ai/people.json` and the words
`text/chat.json` (`chat.*`, as `talk.*` is the rivals' lines already); the Modelfile is `ai/ollama.json`. A deal is the
ad's price itself (no `GameState.Deals`): a counter-offer lowers it too, and the seller's first price is kept with the
person so asking again never goes under the first floor. The car viewing screen's "What do you make of it?" became the
talk (`AiWriter.DealerTakeAsync` is no longer called). Tried live against Ollama: `streetcorsa` (llama3.1:8b) talks
well; llama3.2:3b mixes up prices.

## Step 27: from `docs/ideas.txt`, batch 7: polish (built 2026-10-03)

The owner's play-test list, one PR (branch `feature/ideas-batch-6`), before E9. The owner's decisions: the Door icon is
the owner's `car_door.png`; the player's own ad opens a window with the buyer's offer and, with the AI on, a call to the
buyer who haggles (a new talk role); the car key starts the engine and swaps the buttons for the engine bar, stopping it
swaps them back; Test and Tune picks its strip from the same cards as Free Run.

- **Cars drive home**: back from the street, the diner or the gas station the car drives into the garage as one from
  the lot does (`Screens/Shared/DrivingHome`), unless it was towed, held or is dry.
- **Gas station**: the driveway bell is a `sound` step of `station_arrive`, a second before the car stops; the pump is
  heard filling (`station.pump.*`, `AudioHub.PlayLoop`, CC0 freesound 382268); night headlights reach 40 m at twice the
  brightness (`SceneLights.BrightenHeadlights`); the attendant's window goes on DRIVE OFF.
- **Garage bar**: round icon buttons under the car (key, parts, stand, turn over, repairs, lights, door, rotate;
  `RoundIconToggleStyle`), the engine bar behind the key; Sell (handshake) beside the car's name, a dot for an ad, amber
  for an offer.
- **Racing hub**: Free Run and Test and Tune beside Hit the streets (`Screens/Shared/PracticeRuns`), each through the
  track picker (`Dialogs/TrackPicker`: search, lazily loaded cards, the last pick first; `GameSettings.TestAndTuneStrip`).
- **Paper**: the masthead in the top band over the thick rule; the player's ads among the others at stable places
  (`ClassifiedOrder.Mix`), ringed in grease pencil, their words written and offered to the AI like the rest; clicking one
  opens `Dialogs/PlayerAd` (the car, the offer, Take / Turn down, Call the buyer). The buyer haggles up to a hidden
  ceiling per offer (`ai.talk.mostRaise`) or walks away (`Haggle.DecideAsk`).

Not seen in game: all of it. Unchecked: the headlights' look, 300+ tracks in the picker, the buyer talk's tone.

## Step 28: from `docs/ideas.txt`, batch 8 (planned 2026-10-03)

The owner's seven ideas after E11/E12, talked over on 2026-10-03. Branch `feature/ideas-batch-8` off `dev` `9e45ed1`.
Ideas 1 and 2 are built (PR #65 to `dev`, not merged, review pending); 3 to 7 are planned below, each with the owner's
decisions and what the research found.

### Built: ideas 1 and 2 (PR #65, commit `7c08bf9`, played by the owner)

- **Switch to a car just bought.** With no car in the garage the new one goes straight in. With another car there (an
  impounded one counts: it is collected from the garage) the new one is parked in the car lot and the player is asked
  "Switch Cars?"; yes swaps them (`CarStorage.TakeOut`) and saves (`ICarPurchaseService.SwitchTo`). The question comes
  after the result dialogs and before the screen moves on, so the garage opens on the car chosen
  (`PurchaseFlow.AskToSwitch`, `CarPurchaseService.TakeIfGarageEmpty`). `purchase.bought` names where the car is
  (`{where}`: `purchase.where.garage` / `purchase.where.lot`). Switching takes no game time.
- **Buy always in the talk.** "Buy it for $X" shows whenever a car or a part is talked about, at its price as it stands
  (a shake the AI did not take in still leaves it); selling the player's own car to a buyer still needs an agreed price,
  the ad window has its own buttons (`TalkViewModel.CanBuy`). The talk card is full height again on the dealer lot and
  the car viewing screen.
- **Found in play: a bought car drives in.** Purchases never handed the garage an arrival; now the "bought" leave
  carries one when the car bought is the garage's car (`PurchaseFlow.ArrivalOf`).
- **Found in play: nested wheels turn.** 9 of the game's cars keep a wheel inside its hub or suspension
  (`HUB_LF/SUSP_LF/WHEEL_LF` on `pb_charger_se_69`; also the El Camino '70 mby, Bel Air nostalgia V8, '65 GTO, both
  C10s, Camaro SS v2, two C1 Corvettes). `CarBodyRock` stopped at the outer group, so those wheels never turned or
  steered in any scripted move. It now collects groups nested in groups (not inside a wheel: what is in it turns with it)
  and sets each against its parent's place.

Tests: switch rules and the always-there Buy (2140 pass). `check.ps1` fails only on the old EngineBench Corvette build.

### Ideas 3 and 4: cars look used (built 2026-10-04 on `feature/cars-look-used`, not seen in game)

- **Owner's decisions:** dealer cars come in a bit better condition, newspaper cars noticeably crappier (rust, dirt,
  wear drawn when the car is made, by where it is sold). Ride height: suspension damage only (a broken spring sags its
  corner), but everything prepared for aftermarket springs with an adjustable height later.
- **The car's looks** (`Car.Rust`, `PaintWear`, `GlassCracks`, `LookSeed`, with `BodyDirt` and the body zones; the same
  on `UsedCarListing`), rolled when a car comes into the game by `CarLooks.Roll(condition, source)`: a dealer's listing
  (`LookSource.Dealer`: less rust and worn paint, washed, rarely a cracked screen) or a private car (a rival's own, the
  player's first: more of everything, dirty); the King's car is kept like a show car. The look goes with the car
  through a purchase, a relisting and a rival's ad; the seed keeps its pattern the same everywhere. A dealer prices a
  rusty car down (`CarValuation.LooksFactor`, `market.rustLoss` and the rest).
- **Rules** (`condition.*`): rust grows every day (`RustTask`, every car in the game: `rustPerDay` times the rust there is
  plus `rustStart` times the worn paint; sound paint and no rust stays sound); a front hit of `crackFromKmh` (25) cracks
  the windscreen, across at `shatteredKmh` (70), and the race report says so; a spring under `sagFrom` (0.6) lets its
  corner down, `mostSag` (5 cm) broken (`CarLooks.RideHeight`, the one place a corner's height is worked out: springs
  that set a height add theirs there). The body shop (`RepairShop`): rust repair and respray (`repairs.rustCost` and
  `paintCost` by how far each goes), a respray alone, a new windscreen (`windscreenCost`).
- **The wear layer** (`Looks/`), one technique for the scenes and AC: AcTools' Dark renderer draws no dirt or damage, and
  the car mods name their paint anything ("body", "EXT_Car_Paint", "c1", "Material"; only 41 of 199 game cars use AC's
  dirt shader), so a model of its own is drawn over the car. `BodyShell` looks at the car from 18 ways round in a
  software depth buffer: the triangles seen from outside (faces seen edge on do not count) of the materials that cover
  most of what is seen (but for glass, tyres, rims, chrome, lights and the like) are the paint, and only those carry
  wear (a dashboard in the paint's material stays clean); the windscreen is glass facing forwards above the hood. Each
  triangle is laid on one of six views of the car (sides, front, back, top, bottom) by the way its corners face, and
  copied 2.5 mm off the body. `WearPainter` paints the 2048 px picture from the point of the car each pixel stands for:
  rust low down and round the wheel arches first (noise over a bias), stains and lifting paint at its edges; chips,
  scratches at hip height and a sun-faded top for worn paint; scrapes and fake-lit creases at the end of the car that
  was hit (dents as shading: the layer has no normal map); dirt low down and behind the wheels; cracks on the
  windscreen. `WearLayer` keeps each model's shell and the last layers made (about 2 s the first time for a model, off
  the UI thread).
- **Scenes:** the look rides in `ShownSet.Look` (`ICarPartsService.Shown(car)`, `Shown(id, parts, CarLooks.Of(listing))`
  for a car on sale), so every scene that shows parts shows wear (`ShownPartsOnCar.PutLookAsync`, under the car's root in
  its model's space); the garage's parts view (a ghost car) leaves it off. Sag: `ShownSet.Heights` (`CornerHeights`);
  the viewport's body rock (`CarBodyRock.PoseOf`) leans the body by it, the wheels left on the ground, also at rest
  (garage, car viewing, a lot's chosen car, the player's car on the street; a lot's other cars and a rival stand level).
- **AC:** the race (and a free run) gets the layer without the dirt, written to `%AppData%\StreetCorsa\Looks`
  (`WearLayer.ForRaceAsync`, the newest 30 kept) and mirrored into the mode's `looks\` like a part's model;
  `CAR_n_LOOK` in race.ini, `car_parts.lua` loads it into the body node in the model's space (`CAR_n_MODEL_NODE`).
  AC keeps drawing its own dirt (`ac.setBodyDirt`) and its own scratches and deformation from the body damage. Ride
  height: `RaceStartState.RideHeight` shortens suspensions.ini's `ROD_LENGTH` per axle by the mean of its corners
  (`AcDamageData`). Dirt: the mode adds `DIRT_PER_S` (0.015) a second with all four wheels off the track (grass, dirt,
  a surface with dirt, off the track's own) at 30 km/h or more, sets it into AC as it goes and reports the worse of
  AC's and its own.
- **Found in play: the wear came on after the car drove in.** The shell was kept per loaded model, so every garage visit worked it out again (3 s on the C10). Now it is kept per model file, made while the car loads (`WearLayer.WarmAsync`), its 18 looks run at once (1.6 s on the C10), and the garage holds its reveal and the drive-in for the wear, 4 s at most (`CarViewport3D.ShownSettledAsync`).
- **Found in play: the drive-in stuttered now and then.** Not reproduced off screen (the harness drives in smoothly with the wear on), so what the layer costs while a scene moves was cut: its making uses half the cores at most (`WearLayer.Work`), lends its big arrays instead of making new ones (no picture-sized garbage for the collector), and each model's shell is kept on disk (`%AppData%\StreetCorsa\Looks\shells`, `ShellFile`; the C10 0.9 s instead of 2.6 s from the second time on). A car on the move now logs every late frame (Debug, `Slow frame on the move`, with a full garbage collection named) to find any stutter left.
- **Found in play: a 20-car dealer took 28 s to come up** (3 s before the wear): each car waited for its own 2048 px picture to be painted, written as a PNG and read back, one after another. Now a lot's cars get 1024 px pictures (`WearLayer.LotPicture`), all made at once on the side as the lot starts loading (`LotViewport3D.WarmWear`; 18 cars in 3 s), and the scenes' pictures are DDS with their mips (`Dds`: nothing to decode, loaded as the layer is first drawn, never a frame without them); races keep PNGs. The layers kept are held to 200 MB of pictures.
- **Not done / to watch in play:** the station's wash does not clean the car on its scene until it is shown again; the
  layer has no LODs (AC's far LODs keep it as drawn over LOD A) and does not follow CSP's mesh deformation of a crashed
  car; each car on a lot carries its own 1024 px picture (about 6 MB of video memory). Rolls and prices unbalanced.
- **Harnesses** (session scratchpad): `lookshot` (the garage viewport off screen with a look: car, rust, paint, cracks,
  dirt, hits, seed, then heights) and `texdump` (writes a car's layer and its pictures).

### Idea 5: parts visible in AC (done, PR #66, merged as `6328dd4`)

Rims, tyres, blur and the engine parts standing out of the hood, in AC and the game's scenes; see `parts-in-ac` notes in
`docs/ac-integration/csp-lua-scripts.md` and the PR.

### Idea 7: the parts shop and its clerk (built 2026-10-04 on `feature/parts-shop`, not seen in game)

Taken before idea 6 (the owner agreed): its room is our own, so no outside models to license. One PR.
- **The room:** `tools/ShopScene` builds `shopscenes/shop` (Poly Haven CC0, own signs, pegboard and checker floor):
  counter and till, back-wall shelving under the sign, tyre rack, gondola, engine stand, display table, tubes; four looks
  by shop kind (a shop may name its own `look`). The camera stands inside the door, sways, can be dragged and wheeled.
- **The stock in it:** the shop's real shelves (a junkyard's yard, with what is bolted on) stood in the scene's 103
  `places` by `ShopStockLayout` (engines on the stand, wheels in the rack, the big parts low, one part a place) and drawn
  as the renderer's scenery; stood again after a purchase.
- **The counter:** one window over the room: the clerk's talk and the shop's list (as before, plus a price limit chip
  and the clerk's picks marked first). Without a scene the counter is the screen.
- **The clerk** (`PersonKind.Clerk`, `chat_clerk` + `clerk_search`, `IAiText.JsonAsync`): the owner's design, the game
  searches and the AI voices it; dyno gain per part for the player's engine (`EngineTuner.PowerWith`); a shop's own
  `clerk` in its data, else drawn; clerks never haggle; purchases are remembered. Live on `streetcorsa`: the 8B often
  says `asks: false` while filling in a kind, so anything read makes it an ask, and `chat.fit_words` force the fit filter.
- Not done: no clerk standing in the room (a portrait only when the shop's data gives one); clicking a part in the
  room does not pick its row.
- Docs: `docs/screens/part-shop.md`, `docs/modding.md` (`shopscenes`, `partshops` clerk/look, the prompts),
  `docs/ai-integration.md`.

### Idea 6: the diner street (built 2026-10-05 on `feature/diner-street`, see [screens/diner.md](screens/diner.md))

- **As built:** Main Street (`streets/mainstreet`), our own block of the Quaternius kit (CC0) and WaajaaH's diner
  (CC BY), one KN5 with a sky dome per light, signs of our own, glowing at dusk and night; the Cruise screen is set there
  too and Pretville left the base game (the owner: "the street dome looks bad"). The diner: down Main Street from the
  driver's seat into a bay facing the diner, the racers' cars parked along its front; its name and Into the diner over
  the street; one window with the racers and the word on the street, a racer's card, races, stakes and talk in the same
  window; out of the bay backwards and off down the street to the garage or the curb. The gas station's viewport became
  `DriveInViewport3D` (with `DriveInStage`, `IDriveInScene`), shared by both; `tools/Shared` gained `ObjModel` and
  `Ground`. Pipeline: `tools/DinerScene/build_street.py`.
- **After the owner's first look (2026-10-05, "works like a charm"):** houses on their lawns beyond downtown in place
  of most of the brick ("too many buildings"), and shadows for the racers' parked cars (the shadow box widened over
  the bays, the street lit more by the sun).
- **Not done:** clicking a racer's car in the lot does not open them; the parts store download was left out (a generated
  kiosk); the street has no lamp posts or traffic; the diner's ground floor is the model's plain front.

The plan as it was:

- **Diner refactor (owner):** a full 1960s/70s American street model instead of the street dome; a cinematic arriving
  and leaving like the gas station's; the street scene is the screen, with no data by default: a toggle opens one modal
  with the rivals list and the word on the streets, and clicking a rival switches the same modal to their options and
  talk.
- **Assets (research 2026-10-03, licences from Sketchfab's API):** no free realistic 1960s Main Street exists, so the
  block is built: Quaternius *Downtown City MegaKit* (CC0) as the base, realistic CC-BY buildings mixed in (gamico
  *Modular Urban Dry Cleaners Building*, Orzechu *Low-poly Old American brick building with shop*, soysascha *Corner
  brick storefront building*), WaajaaH *Diner Exterior Asset Reworked* (CC-BY) in a gap, Poly Haven CC0 textures, own
  period signs; the *Bozeman Montana Downtown Strip* (CC-BY) as the proportion reference. Rejected: Fab/CGTrader free
  assets (no redistribution), CC-NC diners. CC-BY needs a credit line each. The owner downloaded the kit, the diner and
  the auto parts store to `D:\JUEGOS\3D Models\scenes`.
- **Parts shop (owner):** a 3D scene from inside the shop instead of the black screen with a list; the soysascha store
  is outdoors only, so it is our own interior: counter, shelves, an engine on a stand, tyres, our own part meshes as stock
  (better_peter *Store Shelves*, CC-BY, for dressing).
- **The clerk (owner approved the design):** one modal with the stock list and the clerk's talk. The game does the
  search, the AI only voices it: the player's words go to Ollama with structured JSON output and come back as a filter
  (kind, fits my car, price); the game applies it to the shop's real stock with the fit check against the player's car
  and engine, the list filters; a second call gets the top ~5 as facts and recommends among them, naming part ids that
  highlight rows. Vague asks ("something fast") fall back to the biggest dyno gain for the player's engine. Cost: two
  calls a message (about 3 to 6 s).

## Step 29: from `docs/ideas.txt`, batch 9 (planned 2026-10-04)

The owner's list: the newspaper's car ads (1), a paint job in a 3D paint booth (2), no going out on the streets in a car
that cannot (3). The owner picked 1 and 3 first, one PR (branch `feature/ideas-batch-9`); 2 after the diner street
(step 28, idea 6).

### Built: ideas 1 and 3 (2026-10-04, not seen in game)

- **The paper is mostly private people's cars (1).** Before, every car on every lot was in the paper, sorted by make.
  Now private people sell their own cars (`UsedCarListing.PaperOnly` with no dealer): 24 to 34 at a time
  (`market.privateAdsLow/High`), any car of the market by how common it is, rougher (`privateMin/MaxCondition` 0.3 to
  0.85, 30,000 to 150,000 km, looks rolled as a private car's) and asking 85% of what a lot would (`privatePriceShare`).
  They turn over like the lots' cars (14 days) and are topped up by the daily market refresh. The seller's name comes
  from the ad's key (`TalkPeople.SellerName`, the same draw as `TalkPeople.Seller`, without putting everyone in the
  save); looking at the car meets them at the curb, as a rival's ad does. The dealers put `dealerAdsInPaper` (1) car
  off each lot in the paper, the same one each read, and keep 0 to 2 cars out back (`backLotLow/High`, condition 0.25
  to 0.55, not cleaned up): in the paper only, seen at the dealer's showroom, never on the lot, not counted in its
  stock (a sale out back still counts in its trade). The racers' own ads stay as they were. Every lot's whole stock
  is still on the dealer map.
- **The ads run in no order (1).** Car and part ads in an order drawn from each ad's id (`ClassifiedOrder.Shuffled`):
  random, but an ad keeps its place for as long as it runs, so it can be found again; the player's ads go in among
  them as before.
- **No streets without a car that runs (3).** `StreetReady` is the rule: a car picked and in the catalog, not in the
  impound, and `DrivingHome.WhyWontRun` with the tank counted (nothing broken, gas for a race, an engine in it), then
  the dyno says the engine starts (off the UI thread). The racing hub works it out as it opens: "Hit the streets" waits
  greyed out, and the note under the buttons turns amber with why. The diner checks the same rule (without the dyno)
  when entered, for a mod's way in that skips the hub, and sends the player home with why.

### Code review fixes (2026-10-05, not seen in game)

- **The paper:** the car each lot prints is picked by other numbers than the paper's order (`PaperAds`): by the same
  ones, every dealer's ad was on the first page. A private ad's engine work is asked at the private share too, so the
  whole car is 85% of a lot's. The day's count of private ads and of each dealer's cars out back is drawn afresh and the
  oldest past it come down (`TakeDown`, bought by somebody else): the paper used to sit at the most. An older save's
  market gets its private ads and back lots when the paper is opened (`FirstMarket`, `SpawnPaperOnlyAsync`), not only
  after the day turns over (never with the market's turnover off). The AI tells a car out back as in the paper.
- **Buying a private car:** the history says bought from the seller by name (the seller one of its known owners, one
  fewer earlier owner), and the message is the private sale's.
- **The rivals** buy, go bankrupt and are helped by the lots' cars only (`UsedCarListing.IsOnALot`): the private and
  back-lot cars are the player's finds, and the rivals' living stays as it was tuned.
- **The streets:** `StreetReady` is the one rule for the racing hub, the diner and the strip (Cruise's own check is
  gone; a car with no engine got onto the strip), says why under a title of its own (no car, a car no longer
  installed, the impound, won't run), and a check that throws lets the player in. The engine's problem words are
  `EngineSpecs.ProblemOf`/`WontRun`, shared with the garage's gas station check; `TalkPeople` draws a private seller's
  sex and name in one place for the paper and the phone, and copes with a save without its people.

### Idea 2: the paint booth (built 2026-10-05, feature/paint-booth)

- **Owner:** a paint booth in 3D. The Paint button (`icons/paint_spray.png`, the owner's) in the garage's bottom bar
  fades to the booth, the car drives in, a spray animation runs while its skin changes; a custom colour, not only the
  mod's skins; after painting the car does not back out, the screen fades back to the garage and the car makes its
  entrance in its new colour.
- **Owner's decisions (2026-10-05):** a paint job costs money and time; a colour of the player's own is tinted where
  the car's paint can take it, the mod's skins only where it cannot (with a note why).
- **As built** (see `docs/screens/paint-booth.md`): our own booth and painter (`tools/PaintBoothScene`, every picture
  and sound made there), the car rolling in from the body shop, the pick on the car as the player picks (the mod's
  skins, sixteen colours of the day, hue / depth / lightness), $350 (+$75 mixed) and 4 hours, the painter walking in
  and round the car spraying while the colour goes on in 8 coats, then home where the car drives into the garage. No
  temp skin through `CarDataOverlay` after all: the colour is the car's own textures tinted (`Looks/CarPaint`), put over
  its own by name in every scene (AcTools' procedural override) and in AC (CSP `applySkin` in `car_parts.lua`,
  `CAR_n_PAINT_k`). The car's files are never touched. The icon is a stand-in drawn in the icons' style until the
  owner's `paint_spray.png` comes.

## Step 30: parts that can be fixed (planned 2026-10-17)

**Owner:** car parts should be fixable, kept real: a battered car bought to have its whole engine refurbished. Until now
a part's mileage (`Wear`) was never fixed (a worn part was replaced) and the repair shop only took damage (`Tear`) off.
The owner picked the plan below and phase 1 first (branch `feature/part-rebuilds`).

The idea, the 1960s way: a worn engine is not made new, it is cut to the next size and fitted with parts made for that
size, and the small parts are rebuilt with a kit or swapped at the parts counter for a rebuilt one.

**Where it stands (2026-10-17):** phases 1 and 1b built on `feature/part-rebuilds` (commits `949d33d`, `f912586`),
PR #73 to dev, open. The owner played it: "I really love it". `tools/check.ps1` passes but for the rated
`vette-63-fuelie` build, which failed before this step too. Not pushed to gitlab (done after the merge, as ever).

**To pick up in a new session:**

1. Merge PR #73 into dev once the owner says so (squash or merge, as they ask), then sync dev to gitlab.
2. Not yet seen anywhere: the grime over a part still mounted (a tune-up on the car, the block clean-up on the stand);
   only a loose part on the bench was rendered off-screen. Ask the owner how it looked, or render it: the off-screen
   harness is a console app on `GarageRenderer` (the garage KN5, `SetScenery(EngineStandLayout.CreateModel(..))`,
   `SetWork(clean, grime, placement, followsParts)`, `Shot`), not in the repo.
3. Ask whether the synthesised work sounds pass or want recordings (CC0, credited in `sfx/credits.csv`).
4. Balance: the job prices (`cost`, `cost_at_least` in `rebuilds.json`) are first guesses.
5. Get the owner's answers to the open questions below, then build phase 2.

- **Phase 1: kits and exchange units (built).** Jobs per part kind in the parts view (the part card, and a part on the
  shelf), as data: `parts/rebuilds.json` in every mod (`PartRebuilds` on `PartsCatalog.Rebuilds`), jobs matched by
  the work order's kinds or the shelves' groups (`Parts/Cars/Rebuilding`: offers, why not, apply). A carburettor
  rebuilt with a kit, a tune-up (points, condenser, cap and rotor), a valve job, a ring job, bearings, a gearbox kit,
  brakes relined; a water pump, generator, starter, fuel pump, distributor, carburettor or clutch swapped for a rebuilt
  one (its damage gone too, the old one the core). Each job costs its kit (a share of the part's new price), takes
  time, brings the part up to what the job can (a kit is not a new part), and some need the part off, or bare. A part
  too far gone for a kit (a head too worn to lap, a bore past a ring job: `min_wear`) says it wants a machine shop.
  No new save state.
- **Phase 1b: the job seen done (built).** A job plays out over 4 to 10 seconds, longer for a longer job
  (`RebuildShow.SecondsFor`), skippable. A loose part floats down onto a workbench behind the engine stand
  (`WorkbenchLayout`, drawn from boxes with the stand) and turns slowly there; for a part on the car or the stand the
  camera goes to it where it is (`CarViewport3D.WorkPiece`). The part's grime (`Looks/PartGrime`: oil, road dirt and a
  little rust, drawn a millimetre and a half over the part, since two surfaces at one depth hide the second, and blended
  at its amount through `GarageRenderer.SetWork`/`WorkGrime`) fades from what its old condition carried to what the
  new one does. Captions per step (`steps` in rebuilds.json) over looping work sounds synthesised by
  `tools/RebuildSounds/make_sounds.py` (hand tools, the parts washer, a hone, valve lapping, an air ratchet, the bench
  grinder, a hammer on a drift, the road); a clank between steps; the game's clock and the part's condition going up
  under a progress bar (`PartsWorkbenchViewModel.ShowWork`). An exchange unit is a drive to the parts counter and back
  (the road, the shop's air, its door bell). The owner's picks: a workbench spot, 4 to 10 seconds, a fade with sounds
  for the exchange (the parts shop's counter scene later). The job is done, paid and its time spent before the show:
  leaving or skipping only ends the showing.
- **Phase 2: the machine shop (next).** Blocks bored oversize (+.030, +.040, +.060; past that a sleeve or scrap), cranks
  ground undersize (.010, .020, .030) with bearings to match, oversize pistons for an oversize bore, heads milled
  (a little compression), cams reground, flywheels resurfaced, radiators re-cored, drums turned. Each part keeps its
  sizes and the cuts it has left, so a standard-bore block is worth more than one at +.060. Parts dropped off, ready in
  a few days. A draft of how, from what is there now:
  - **Data:** machine jobs in `rebuilds.json` too (`"way": "machine"`): a `cut` (`bore`, `grind`, `mill`, `regrind`,
    `resurface`), its steps of size (`[0.030, 0.040, 0.060]` in inches, or a mill's thousandths), what each step does to
    the dyno, `restores` (a machined part is as good as new: 0.97 or so), the days it takes.
  - **Save:** `PartInstance` gets the cuts made (`Dictionary<string, int> Cuts`, empty = standard). LiteDB reads an
    older save's parts with it empty; check `GameStateRepository`'s load fix-ups (`PartOrders ??= []` is the pattern)
    and the saves' schema stamp before calling it a bump.
  - **Matching parts:** an oversize bore wants pistons of its size, an undersize crank bearings of its size. The
    simplest real way: the machine job sells the matching pistons/bearings with it (in its price), and the pistons
    remember the size (`Cuts["bore"]` on the pistons too); a work order `needs`-style rule keeps standard pistons out of
    a bored block (`EngineWork.WhyNotOn` is where a part is refused, in the work order's words).
  - **Dyno:** bore and the clearance volume come from the block and head scripts (`DynoInputs.Bore`,
    `ClearanceVolume` from `"Vmin"`); the cuts go on after them as the parts' figures do (`Parts/Logic/PartFigures`,
    E11: "compression last, from the bore and stroke it ends with"): bore + oversize, `Vmin` less for a milled head.
    Then the engine's price follows by itself (`PartPricing.EngineNewPrice` from the dyno).
  - **Worth:** a part's worth (`PartPricing.Worth`) less for every cut gone, nothing at the last (a block past +.060).
  - **Drop-off:** `Player.PartOrders` (`PartOrder`: part, paid, ordered, `ArrivesOn`; delivered by
    `NewspaperPartsService` at the first review of its day) already brings parts to the shelf on a day: a machine
    job takes the part off the shelf and puts it in an order that comes back cut. The shelf or the paper says what is
    at the machine shop.
  - **Where:** depends on the owner's answer (below). A counter window reuses the parts shop's (`shopscenes`, the AI
    clerk as the machinist); a 3D place is a new scene.
- **Phase 3: hidden flaws.** A cracked block, a spun bearing, a burnt valve, a scored bore: found by a compression test
  or the oil pressure in the garage, by tearing the engine down, or by the machinist's crack check (the machinist talks,
  as the parts shop's clerk). Buying a battered car is a gamble.
- **Phase 4: speed machining.** A cam reground to a hotter grind, heads ported and polished, the engine balanced and
  blueprinted: the repairs become tuning.

Open questions for the owner (ask before phase 2): a quality roll on the player's own work, or a fixed result as now;
the machine shop as a 3D place, or a counter window like the parts shop's; hidden flaws (phase 3), or too punishing.
Settled: jobs are shown done in 3D (phase 1b).

## Epic: total conversion (planned 2026-09-30)

The owner wants the whole game moddable, so the community can add game modes and whole new games (a Tokyo Drift game
on the same program); the 1960s US setting is the base game's taste. **Changed 2026-10-03:** Street Corsa stays one
game; being open source, a whole new game is a fork. E9 is cut down to saves and mods (mod set stamped in the save,
string ids for the saved enums, the King as data; no game picker or game-type base), and E12 becomes an example add-on
mod instead of a separate game. The inventory of what is still baked in and the
steps E1-E12 are in `docs/total-conversion.md`. One PR per step, each playable; the base game keeps its look and play
(today's values become its data). Car scripting, first asked for on its own, is step E7 and uses JSON (Lua dropped).

E1 (foundations) and E2 (rules as data) are built together on `feature/total-conversion` (2026-09-30): mod.json's type,
requires, gameVersion and removes; game.json; the mods' problems shown at start-up; Steam's AC folder found; every
rule figure in `rules/*.json`, kept in the save from the career's start. What was left for later is listed there, "As
built". The owner played it ("Works good!"); reviewed, merged to dev as PR #51.

E3 (words) is built on `feature/words` (2026-09-30): every word on the screens, in the messages and in the race mode
is a text pool of the mods (about 1,300 keys), and a mod can add a language, picked in Settings. The owner played it;
merged as PR #52.

E4 (locale) is built on `feature/locale` (2026-10-01): `game.json`'s `locale` (culture, currency, units), the base game
in US 1960s units (mph, miles, gallons, °F), a units picker in Settings, money and figures written through `Money` and
`Measure`, the text naming a number's kind (`{price:money}`), date patterns as text. The owner tested it; merged as
PR #53.

E5 (look) is built on `feature/look` (2026-10-01): a mod's `theme/*.json` sets the colour, size and typeface keys (the
base game's `theme/theme.json` lists them all), its `theme/fonts` and `theme/images` stand in for the game's, an intro
logo and a window icon of a game's own, and the music the game starts to (`music.intro`). The base game looks exactly
as before (checked pixel for pixel). The owner played it; merged as PR #54.

E6 (cars and makes) is built on `feature/cars-makes` (2026-10-01): the makes in `cars/makes.json` (families, countries,
badge prices and rarity), what a car is given in `cars/cars.json` (the developer's hand-picked factory engines and
prices moved there out of catalog.db; the catalog editor saves there), the pricing curve and the engines' figures in
the rules (`cars`, `engines`), game.json's `cars` filter of the installed cars (none in the base game), the shelves in
`parts/part_groups.json`, the stand-in script classes in `parts/stand_ins.json`. Prices, families and groups came out
exactly as before. Not played yet. The order after that is E7-E12 as listed (E9 and E12 cut down on 2026-10-03, see
above), unless the owner picks otherwise.

E9 (saves and mods) is built on `feature/bosses` (2026-10-03): a save played with older mods says so before it loads,
and the King is one boss of several: any racer can be one (`"boss"` in the racers' data, with his path, money and
record), a path's `beatBoss` step is won by beating its bosses, a boss can have lines of his own, old saves' King
becomes the King path's boss. Race types as ids moved to E10; milestone triggers stay an enum. Not seen in game.

E10 (race formats) is built on `feature/race-formats` (2026-10-03): the kinds of race are data (`formats/formats.json`:
family, Lua module or a mod's own mode, laps, how it is won), the race mode is a core plus format modules
(`format_drag.lua`, `format_road.lua`, `format_time_attack.lua`), results carry the format and a score, events and the
diner pick a format, and the base game gains a time attack (ghost cars, a warm-up lap and three flying laps, the best
lap wins; at the diner on road tracks and as the paper's "Against the Clock"). Not seen in game.

E11 (data engines) is built on `feature/data-engines` (2026-10-03): a part gives its engine figures in plain metric
units (`"figures"` in its pack: bore, stroke, compression, breathing, cam, fuel, gears, turbos...), an engine whose block
has no SLRR script is made of them alone and runs on the game's dyno, figures go over the scripts' on an SLRR engine,
each turbo has its own lag and is a turbo of its own in AC (twin turbos), a gearbox of figures has up to 10 gears, and
an engine can lie across the car (`front_transverse`, `mid_transverse`, guessed for front-wheel drive). Not seen in game.

E12 (the example mod) is built on `feature/example-mod` (2026-10-03), closing the total-conversion epic:
`examples/mods/small_imports`, four small European cars (Kunos cars every AC has) with engines of figures alone, their
speed parts, a specialist shop, an import lot, a rival who drives imports, an autocross format and an Import Night
event. What it found is fixed: a lot and a racer can keep to one kind of car (`"cars"`), and any racer can have lines
of his own. Not seen in game.

E7 (car scripting) is built on `feature/car-scripting` (2026-10-02), after step 26: every car move is a JSON script of
the mods (`scripts/scripts.json`, the drives' feels in `scripts/feels.json`) played at a trigger (`lot.take_out`,
`garage.arrive`, `garage.leave`, `station.arrive`, `station.at_pump`, `station.leave`, `street.rival_arrive`,
`street.rival_leave`), on the marks and paths each scene's JSON names. One car driver replaces the three movers; the
base game's shows are its scripts, checked against the old movers number for number. The owner played it;
merged as PR #58.

E8 (flow) is built on `feature/flow` (2026-10-02): the screens by id in the mods' `flow/*.json` (kinds the program has,
named exits, `home`, the garage's tiles, the main menu's cards, where the day can end), navigation by id, every system a
game can switch off in `rules/systems.json` with its fallback (no station: filled up overnight; no career screen: the
rules' path), races back where they were set up (the racing hub's to the garage), and `screen.<id>.enter/.leave`
scripts on the garage, the car lot and the gas station. The base game plays as before. Not seen in game yet.

## Testing in AC without driving

See `docs/ac-integration/csp-lua-scripts.md`, "Testing the race mode without driving". In short:
1. back up `Documents\Assetto Corsa\cfg\race.ini` and `assists.ini`;
2. write a `race.ini` with `__CM_CUSTOM_MODE=sc_race`;
3. append a shim to the installed `mode.lua` (logging, autopilot, quit after N seconds);
4. run `C:\GAMES\Street Rod AC\acs.exe`;
5. read `Documents\Assetto Corsa\logs\custom_shaders_patch.log` (the lines tagged `[Street Corsa]`) and `log.txt`;
6. screenshot the windowed AC from PowerShell (`Graphics.CopyFromScreen`) while it runs;
7. afterwards, restore the cfg files, delete the result from `out\sc_race`, and copy the repo's `mode.lua` back.

**Hard-won facts:**
- CSP reads `[RACE] __CM_CUSTOM_MODE`; `MODE=` and `__CM_NEW_MODE_USED` do nothing.
- A mode's `prepare()` and `update()` aren't called on the pits menu: a module-level `setInterval` has to call `ac.tryToStart(true)`.
- AC's drag session starts about 14 s before the tree goes green, and its jump-start rule put the car back after about 17 m. It is no longer used.
- On `ks_drag` as a race session, the autopilot steers into the wall. Once it stops, AC retires it as an AI and ends the race, so a finish can't be tested unattended.
- AC's drag reset teleported the cars without firing `ac.onCarJumped`.
- The cfg folder is shared by the Street Corsa install (`C:\GAMES\Street Rod AC`) and the main AC install (`C:\GAMES\Assetto Corsa`). Test Drive, race-explorer's CSP mode, is deployed in the main install.
