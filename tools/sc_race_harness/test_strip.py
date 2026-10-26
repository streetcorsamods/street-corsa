"""
Runs the race mode's drag strip extras (apps/new-modes/sc_race/mode.lua) in the stub world of test_chase.py, on a
straight strip: bracket races (each lane's own tree, red lights, breakouts, the rival taking the stripe) and
test-and-tune (pass after pass, the car put back on the line, the result written after each pass).

Each driver accelerates evenly up to 60 m/s and stops dead 430 m down the strip, past the quarter (402 m). At 8 m/s²
that is a 10.23 s quarter, at 6 m/s² an 11.58 (timed from the stage beam, 0.2 m on).

    python tools/sc_race_harness/test_strip.py
"""

import json
import math
import os
import sys

from test_chase import run, check, paths, HERE, GOLDEN_CONTEXT_ID

STRIP_LENGTH = 1000.0


# How close a reaction time comes to what the driver did: the harness's drivers go by the stub's clock and the green
# the mode logs (to 0.01 s), a tick or two off the mode's race clock
REACTION_SLACK = 0.12


def rollout(accel):
    """The time from moving off to the stage beam 0.2 m on, which a slip's reaction time counts too, as a real one does"""
    return math.sqrt(2 * 0.2 / accel)


def driver(accel, reaction, early=False, stop_at=430):
    """A driver who leaves `reaction` seconds after their green (before it, by that much, when early), accelerating at
    `accel` until `stop_at` metres. The player's green comes from the mode's log (world.green0)."""
    def go(world, i, t, car):
        green = world['green0'] if i == 0 else None
        if i == 0:
            if world['brakesUntil'] is not None and world['simSeconds'] < world['brakesUntil']:
                return 'hold'
            if green is None:
                return 'hold'
            leave = green - reaction if early else green + reaction
            if t < leave:
                return 'hold'
        if car['total'] > stop_at:
            return 'stop'
        return accel
    return go


def strip(player, rival=None):
    def accel(world, i, t, car):
        return player(world, i, t, car) if i == 0 else rival(world, i, t, car)
    return accel


def participant(result, index):
    return next(p for p in result['participants'] if p['car_index'] == index)


def bracket_ini(player_dial, rival_dial, reaction=0.15, margin=0.05):
    return {'STREET_CORSA.RACE_TYPE': 'DRAG', 'STREET_CORSA.DIAL_IN': f'{player_dial},{rival_dial}',
            'STREET_CORSA.BRACKET_RIVAL': f'{reaction},{margin}'}


def the_slower_dial_in_gets_the_green_first():
    # The player dials 12.20, the rival 10.40: the player's tree goes first, the rival's 1.8 s after
    result, log, _, _ = run('stagger', 2, bracket_ini(12.20, 10.40, reaction=0.2), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(6, 0.3), driver(8, 0)), max_seconds=60)
    player, rival = participant(result, 0), participant(result, 1)
    check('the player got the first green', abs(player['timeslip']['green_s'] - 3.5) < 1e-6, log)
    check('the rival 1.8 s later', abs(rival['timeslip']['green_s'] - 5.3) < 1e-6, log)
    check("the player's reaction is from their own green", abs(player['timeslip']['reaction_s'] - 0.3 - rollout(6)) < REACTION_SLACK, log)
    check('the rival was held until its green and left a moment after',
          abs(rival['timeslip']['reaction_s'] - 0.2 - rollout(8)) < REACTION_SLACK, log)
    check('the dial-ins are in the result', player['dial_in_s'] == 12.2 and rival['dial_in_s'] == 10.4, log)
    check('the race was run to the quarter', result['session']['end_reason'] == 'FINISHED', log)


def the_rival_takes_the_stripe():
    # The rival's car would run an 11.58 on a 12.00 dial-in: it lifts near the end and runs just over it
    result, log, _, _ = run('stripe', 2, bracket_ini(10.50, 12.00, margin=0.05), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.2), driver(6, 0)), max_seconds=60)
    rival = participant(result, 1)
    et = rival['timeslip']['quarter_mile_s']
    check(f'the rival did not break out ({et})', et >= 12.0 and rival['breakout'] is False, log)
    check(f'and ran close to its dial-in ({et})', et < 12.3, log)


def a_breakout_loses():
    # The player dials 11.00 and runs a 10.23; the rival runs to its dial-in: the rival wins, whoever got there first
    result, log, _, _ = run('breakout', 2, bracket_ini(11.00, 12.00), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.1), driver(6, 0)), max_seconds=60)
    player = participant(result, 0)
    check('the player broke out', player['breakout'] is True, log)
    check('and lost', player['performance']['final_position'] == 2, log)
    check('the rival won', participant(result, 1)['performance']['final_position'] == 1, log)


def first_to_the_quarter_on_the_dial_wins():
    # Both on their dial-ins, the player a sharper leaver: the player's car gets to the quarter first and wins
    result, log, _, _ = run('on the dial', 2, bracket_ini(10.20, 12.20, reaction=0.3), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.02), driver(6, 0)), max_seconds=60)
    player, rival = participant(result, 0), participant(result, 1)
    check('nobody broke out', player['breakout'] is False and rival['breakout'] is False, log)
    check('the player won', player['performance']['final_position'] == 1, log)
    check('the result says so', 'Bracket race over' in log and 'WON' in log, log)


def leaving_before_the_green_is_a_red_light():
    result, log, _, _ = run('red light', 2, bracket_ini(12.00, 11.00), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.5, early=True), driver(8, 0)), max_seconds=60)
    player = participant(result, 0)
    check('a red light is a false start', result['session']['end_reason'] == 'FALSE_START', log)
    check('on the slip too', player['timeslip']['red_light'] is True and player['timeslip']['reaction_s'] < 0, log)
    check('the player is marked', player['false_start'] is True, log)


# The rear axle (race.ini CAR_0_AXLE, the Nm it holds): the drive the rear tyres put down, |fx| at the loaded radius
# (0.3 m in the stub) summed over both, may break it when past that. math.random is pinned at 0.5: a load twice the
# strength breaks on the first frame (1 - e^-2 = 0.86 a frame), one under it never rolls at all. Run as a bracket race:
# the harness's drivers leave on the green the tree logs, which a plain drag race has not got
def axle_ini(strength):
    return dict(bracket_ini(10.20, 12.20, reaction=0.3), **{'STREET_CORSA.CAR_0_AXLE': str(strength)})


def the_rear_axle_holds_under_its_load():
    # 1500 N a tyre at 0.3 m is 900 Nm on a 1000 Nm axle: it holds, and the slip carries the peak
    result, log, _, _ = run('axle holds', 2, axle_ini(1000), None, None, None, length=STRIP_LENGTH,
                            strip=strip(driver(8, 0.1), driver(6, 0)), max_seconds=60, axle_force=lambda i, t: 1500, random=0.5)
    player = participant(result, 0)
    check('the race ran to the quarter', result['session']['end_reason'] == 'FINISHED', log)
    check(f"nothing broke ({player.get('breakdown')})", player.get('breakdown') is None, log)
    check(f"the peak load is on the slip ({player['condition'].get('axle_peak_nm')})", abs(player['condition']['axle_peak_nm'] - 900) < 1, log)
    check('and the axle is whole', player['condition']['axle_broken'] is False, log)


def the_rear_axle_lets_go_past_its_load():
    # 5000 N a tyre is 3000 Nm on a 1000 Nm axle: it lets go as soon as the car drives, and the race is over for the player
    result, log, _, _ = run('axle breaks', 2, axle_ini(1000), None, None, None, length=STRIP_LENGTH,
                            strip=strip(driver(8, 0.1), driver(6, 0)), max_seconds=60, axle_force=lambda i, t: 5000, random=0.5)
    player = participant(result, 0)
    check('the player broke down', result['session']['end_reason'] == 'BROKE_DOWN', log)
    check(f"the rear end let go ({player.get('breakdown')})", player.get('breakdown') == 'AXLE', log)
    check('the slip says so', player['condition']['axle_broken'] is True and player['condition']['axle_peak_nm'] >= 3000, log)
    check('the log says so', 'BROKE DOWN: AXLE' in log, log)


def a_csp_without_the_wheels_drive_keeps_the_axle_whole_and_says_so():
    # No fx on the wheels (this CSP, or a field renamed): the car goes on, the axle holds, and the log has one line on it
    result, log, _, _ = run('axle unread', 2, axle_ini(1000), None, None, None, length=STRIP_LENGTH,
                            strip=strip(driver(8, 0.1), driver(6, 0)), max_seconds=60, random=0.5)
    player = participant(result, 0)
    check('the race ran to the quarter', result['session']['end_reason'] == 'FINISHED', log)
    check(f"nothing broke ({player.get('breakdown')})", player.get('breakdown') is None, log)
    check('the log says the load could not be read, once', log.count('The rear axle load cannot be read, the axle holds') == 1, log)
    check('the peak stays at nothing', player['condition']['axle_peak_nm'] == 0, log)


# When the harness takes the player to the pits (seconds of its clock): 3 s past pass 1's quarter, still at full
# speed; and part way down pass 3
MENU_PAST_QUARTER = 20
MENU_THIRD_PASS = 60


def tune_ini(passes):
    return {'STREET_CORSA.RACE_TYPE': 'TUNE', 'STREET_CORSA.TUNE_PASSES': str(passes)}


def test_and_tune_runs_its_passes():
    result, log, _, placements = run('tune', 1, tune_ini(3), None, None, None,
                                     length=STRIP_LENGTH, strip=strip(driver(8, 0.3)), max_seconds=200)
    player = participant(result, 0)
    passes = player['passes']
    check('one participant', len(result['participants']) == 1, log)
    check('a test-and-tune', result['session']['race_type'] == 'TUNE', log)
    check(f'three passes ({len(passes)})', [p['pass'] for p in passes] == [1, 2, 3], log)
    for p in passes:
        check(f"each a full quarter ({p['quarter_mile_s']})", abs(p['quarter_mile_s'] - 10.23) < 0.05, log)
        check(f"from its own green ({p['reaction_s']})", abs(p['reaction_s'] - 0.3 - rollout(8)) < REACTION_SLACK, log)
    check('the car was put back on the line between them', placements == 2, log)
    check('the best pass is the timeslip', player['timeslip']['quarter_mile_s'] == min(p['quarter_mile_s'] for p in passes), log)
    check('the strip closes after the last', result['session']['end_reason'] == 'FINISHED', log)


def going_to_the_pits_leaves_the_strip():
    # One pass takes about 18 s from the start (3 s staging, the tree, the run, the slip): the pits at 25 s end it
    result, log, _, _ = run('pits', 1, tune_ini(6), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.3)), max_seconds=200, menu_at=25)
    passes = participant(result, 0)['passes']
    check(f'the pass run so far is kept ({len(passes)})', len(passes) == 1, log)
    check('finished, not abandoned', result['session']['end_reason'] == 'FINISHED', log)


def leaving_past_the_quarter_keeps_the_pass():
    # Still at full speed past the quarter when the player goes to the pits: the pass is run, and counts
    result, log, _, _ = run('pits rolling', 1, tune_ini(6), None, None, None, length=STRIP_LENGTH,
                            strip=strip(driver(8, 0.3, stop_at=900)), max_seconds=200, menu_at=MENU_PAST_QUARTER)
    passes = participant(result, 0)['passes']
    check(f'the pass under way is kept ({len(passes)})', len(passes) == 1, log)
    check(f"with its quarter ({passes[0].get('quarter_mile_s')})", abs(passes[0]['quarter_mile_s'] - 10.23) < 0.05, log)


def every_pass_is_written_over_the_last():
    # A crash on the third pass: the file already holds two, and the last write must replace it (CSP's io.move fails
    # onto an existing file unless told not to)
    result, log, _, _ = run('tune crash', 1, tune_ini(6), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.3)), max_seconds=200, menu_at=MENU_THIRD_PASS)
    passes = participant(result, 0)['passes']
    check(f'the passes after the first are written ({len(passes)})', len(passes) == 3, log)
    check('no write failed', 'ERROR' not in log, log)


def an_older_csp_still_writes_every_pass_over_the_last():
    # A CSP build whose io.move does not take failIfExists: the last pass goes first, then the move is made again
    result, log, _, _ = run('tune crash, old CSP', 1, tune_ini(6), None, None, None, length=STRIP_LENGTH,
                            strip=strip(driver(8, 0.3)), max_seconds=200, menu_at=MENU_THIRD_PASS, old_csp=True)
    passes = participant(result, 0)['passes']
    check(f'an older CSP gets the passes after the first written too ({len(passes)})', len(passes) == 3, log)
    check('no write failed on it', 'ERROR' not in log, log)
    check('the log says which CSP build ran the race', 'CSP build 2000' in log, log)


def put_back_past_the_line_still_stages():
    # AC leaves the car a little up the strip: each pass starts from where it stands
    result, log, _, placements = run('past the line', 1, tune_ini(3), None, None, None, length=STRIP_LENGTH,
                                     strip=strip(driver(8, 0.3)), max_seconds=200, place_offset=0.5)
    passes = participant(result, 0)['passes']
    check(f'every pass was timed ({len(passes)})', [p['pass'] for p in passes] == [1, 2, 3], log)
    for p in passes:
        check(f"each a full quarter ({p['quarter_mile_s']})", abs(p['quarter_mile_s'] - 10.23) < 0.05, log)


def a_red_light_on_a_pass_is_only_a_red_light():
    result, log, _, _ = run('tune red', 1, tune_ini(2), None, None, None,
                            length=STRIP_LENGTH, strip=strip(driver(8, 0.5, early=True)), max_seconds=200)
    passes = participant(result, 0)['passes']
    check('both passes ran', len(passes) == 2, log)
    check('each a red light', all(p.get('red_light') for p in passes), log)
    check('and the session was not called off', result['session']['end_reason'] == 'FINISHED', log)


# What the mode writes for a bracket race and a test-and-tune, as the C# side's contract test reads them
# (RaceResultContractTests); --golden writes them again
FIXTURES = os.path.join(HERE, '..', '..', 'tests', 'StreetCorsa.Tests', 'Fixtures')


def golden_files(write=False):
    named = {'STREET_CORSA.CONTEXT_ID': GOLDEN_CONTEXT_ID, 'STREET_CORSA.FORMAT': 'drag', 'STREET_CORSA.MODULE': 'format_drag'}
    bracket = dict(bracket_ini(10.20, 12.20, reaction=0.3), **named)
    tune = dict(tune_ini(2), **named)
    for name, ini, cars, drivers in (('sc_race_bracket_result.json', bracket, 2, strip(driver(8, 0.02), driver(6, 0))),
                                     ('sc_race_tune_result.json', tune, 1, strip(driver(8, 0.3)))):
        result, log, _, _ = run(name, cars, ini, None, None, None, length=STRIP_LENGTH, strip=drivers, max_seconds=200)
        golden = os.path.join(FIXTURES, name)
        if write:
            with open(golden, 'w', encoding='utf-8', newline='\n') as f:
                json.dump(result, f, indent=2, sort_keys=True)
                f.write('\n')
            print(f'wrote {os.path.normpath(golden)}')
        with open(golden, encoding='utf-8') as f:
            kept = json.load(f)
        missing, extra = paths(result) - paths(kept), paths(kept) - paths(result)
        check(f'{name} has the shape the mode writes (run with --golden to write it again); '
              f'written but not in it: {sorted(missing)}, in it but not written: {sorted(extra)}', not missing and not extra, log)


if __name__ == '__main__':
    if '--golden' in sys.argv:
        golden_files(write=True)
    for test in (golden_files, the_slower_dial_in_gets_the_green_first, the_rival_takes_the_stripe, a_breakout_loses,
                 first_to_the_quarter_on_the_dial_wins, leaving_before_the_green_is_a_red_light,
                 the_rear_axle_holds_under_its_load, the_rear_axle_lets_go_past_its_load,
                 a_csp_without_the_wheels_drive_keeps_the_axle_whole_and_says_so,
                 test_and_tune_runs_its_passes, going_to_the_pits_leaves_the_strip,
                 leaving_past_the_quarter_keeps_the_pass, every_pass_is_written_over_the_last,
                 an_older_csp_still_writes_every_pass_over_the_last,
                 put_back_past_the_line_still_stages, a_red_light_on_a_pass_is_only_a_red_light):
        test()
        print(f'ok  {test.__name__}')
    sys.exit(0)
