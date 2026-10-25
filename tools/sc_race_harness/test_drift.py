"""
Runs Touge Nights' drift battle (Content/touge_nights/formats/drift.lua, put into the mode's folder by the game as
format_touge_nights-drift.lua) in the stub world of test_chase.py, on its 2000 m loop: the player is scored on the
angle they slide at (world slip), the rival as a ghost at its skill's angle wherever it corners hard enough. The
higher score wins.

    python tools/sc_race_harness/test_drift.py
"""

import os
import shutil
import sys
import tempfile

from test_chase import run, check, HERE

MODULE = os.path.join(HERE, '..', '..', 'Street Corsa', 'Content', 'touge_nights', 'formats', 'drift.lua')
NAME = 'format_touge_nights-drift'


def drift_ini(laps=1, ai_level='90'):
    return {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.FORMAT': 'drift', 'STREET_CORSA.MODULE': NAME,
            'STREET_CORSA.LAPS': str(laps), 'STREET_CORSA.SCORE': 'HIGHER', 'CAR_1.AI_LEVEL': ai_level}


def participant(result, index):
    return next(p for p in result['participants'] if p['car_index'] == index)


def race(name, modules, player_slip, rival_speed=35, ai_level='90', player_speed=30):
    return run(name, 2, drift_ini(ai_level=ai_level), lambda t: player_speed, lambda t: rival_speed, None, max_seconds=400,
               slip=lambda i, t: player_slip(t) if i == 0 else 0, modules=modules)


def sliding_the_whole_course_beats_a_middling_rival(modules):
    result, log, _, _ = race('slide', modules, lambda t: 35)
    player, rival = participant(result, 0), participant(result, 1)
    check('the format is in the result', result['session']['format'] == 'drift', log)
    check('the race ran its course', result['session']['end_reason'] == 'FINISHED', log)
    check(f"both have a score ({player.get('score')}, {rival.get('score')})", player.get('score', 0) > 0 and rival.get('score', 0) > 0, log)
    check('the player won on points', player['performance']['final_position'] == 1, log)
    check('the result card says so', 'Drift battle over' in log and 'WON' in log, log)


def driving_straight_scores_nothing(modules):
    result, log, _, _ = race('grip', modules, lambda t: 0)
    player, rival = participant(result, 0), participant(result, 1)
    check(f"no points for grip driving ({player.get('score')})", player.get('score') == 0, log)
    check('the rival won', rival['performance']['final_position'] == 1, log)


def a_spin_loses_the_combo_running(modules):
    steady, log, _, _ = race('steady', modules, lambda t: 35)
    spun, log2, _, _ = race('spun', modules, lambda t: 140 if 40 <= t < 41 else 35)
    check(f"a spin costs points ({participant(spun, 0)['score']} < {participant(steady, 0)['score']})",
          participant(spun, 0)['score'] < participant(steady, 0)['score'] * 0.7, log2)


def a_better_rival_scores_more(modules):
    middling, _, _, _ = race('middling', modules, lambda t: 35, ai_level='88')
    best, log, _, _ = race('best', modules, lambda t: 35, ai_level='100')
    check(f"skill raises the ghost's score ({participant(middling, 1)['score']} < {participant(best, 1)['score']})",
          participant(middling, 1)['score'] < participant(best, 1)['score'], log)


def a_rival_that_never_corners_hard_scores_nothing(modules):
    # Slow on the loop's long bend: its pull stays under the ghost's corner, so it never "slides"
    result, log, _, _ = race('slow rival', modules, lambda t: 20, rival_speed=20)
    check(f"no corner, no points ({participant(result, 1).get('score')})", participant(result, 1).get('score') == 0, log)
    check('the sliding player won', participant(result, 0)['performance']['final_position'] == 1, log)


if __name__ == '__main__':
    folder = tempfile.mkdtemp()
    try:
        shutil.copyfile(MODULE, os.path.join(folder, NAME + '.lua'))
        for test in (sliding_the_whole_course_beats_a_middling_rival, driving_straight_scores_nothing,
                     a_spin_loses_the_combo_running, a_better_rival_scores_more, a_rival_that_never_corners_hard_scores_nothing):
            test(folder)
            print(f'ok  {test.__name__}')
    finally:
        shutil.rmtree(folder, ignore_errors=True)
    sys.exit(0)
