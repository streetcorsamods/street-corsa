"""
Runs the race mode's time attack (apps/new-modes/sc_race/format_time_attack.lua) in the stub world of test_chase.py, on
its 2000 m loop: a warm-up lap from the grid, then LAPS timed laps each; the best lap is each racer's score and wins.
The race is over once the player has their laps and the rival has too, is out, or has had 90 s more.

    python tools/sc_race_harness/test_time_attack.py
"""

import json
import os
import sys

from test_chase import run, check, paths, HERE, GOLDEN_CONTEXT_ID

LOOP = 2000.0


def attack_ini(laps=2):
    return {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.FORMAT': 'time_attack',
            'STREET_CORSA.MODULE': 'format_time_attack', 'STREET_CORSA.LAPS': str(laps), 'STREET_CORSA.SCORE': 'LOWER'}


def participant(result, index):
    return next(p for p in result['participants'] if p['car_index'] == index)


def lap_ms(speed):
    return LOOP / speed * 1000


def the_quicker_lap_wins():
    result, log, _, _ = run('quicker', 2, attack_ini(), lambda t: 30, lambda t: 25, None, max_seconds=600)
    player, rival = participant(result, 0), participant(result, 1)
    check('the format is in the result', result['session']['format'] == 'time_attack', log)
    check('the race ran its course', result['session']['end_reason'] == 'FINISHED', log)
    check(f"two timed laps each, the warm-up not one of them ({player.get('lap_times_ms')})", len(player['lap_times_ms']) == 2, log)
    check(f"the player's score is their best lap ({player['score']})", abs(player['score'] - lap_ms(30)) < 100, log)
    check(f"the rival's too ({rival['score']})", abs(rival['score'] - lap_ms(25)) < 100, log)
    check('the player won', player['performance']['final_position'] == 1 and rival['performance']['final_position'] == 2, log)
    check('the race waited for the rival to finish', len(rival['lap_times_ms']) == 2, log)


def the_slower_lap_loses_whoever_finished_first():
    # The rival is quicker and done first; the player still runs their laps, and loses on them
    result, log, _, _ = run('slower', 2, attack_ini(), lambda t: 25, lambda t: 30, None, max_seconds=600)
    player, rival = participant(result, 0), participant(result, 1)
    check('the player ran their laps', len(player['lap_times_ms']) == 2, log)
    check('the rival won on its best lap', rival['performance']['final_position'] == 1 and player['performance']['final_position'] == 2, log)
    check('the result card says so', 'Time attack over' in log and 'LOST' in log, log)


def a_rival_without_a_time_is_beaten():
    # The rival crawls: still on its warm-up lap when the wait runs out
    result, log, seconds, _ = run('no time', 2, attack_ini(), lambda t: 30, lambda t: 4, None, max_seconds=600)
    player, rival = participant(result, 0), participant(result, 1)
    check('the rival has no score', rival.get('score') is None, log)
    check('the player won', player['performance']['final_position'] == 1, log)
    check(f'after waiting for the rival ({seconds:.0f} s)', seconds > lap_ms(30) * 3 / 1000 + 80, log)


def one_lap_is_a_race_too():
    result, log, _, _ = run('one lap', 2, attack_ini(laps=1), lambda t: 30, lambda t: 29, None, max_seconds=400)
    check('one timed lap each', len(participant(result, 0)['lap_times_ms']) == 1 and len(participant(result, 1)['lap_times_ms']) == 1, log)


# What the mode writes for a time attack, as the C# side's contract test reads it (RaceResultContractTests); --golden
# writes it again
FIXTURES = os.path.join(HERE, '..', '..', 'tests', 'StreetCorsa.Tests', 'Fixtures')


def golden_file(write=False):
    name = 'sc_race_time_attack_result.json'
    ini = dict(attack_ini(), **{'STREET_CORSA.CONTEXT_ID': GOLDEN_CONTEXT_ID})
    result, log, _, _ = run(name, 2, ini, lambda t: 30, lambda t: 25, None, max_seconds=600)
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
        golden_file(write=True)
    for test in (golden_file, the_quicker_lap_wins, the_slower_lap_loses_whoever_finished_first, a_rival_without_a_time_is_beaten,
                 one_lap_is_a_race_too):
        test()
        print(f'ok  {test.__name__}')
    sys.exit(0)
