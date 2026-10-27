"""
Runs Touge Nights' drift battle (the setting's formats/drift.lua, put into the mode's folder by the game as
format_touge_nights-drift.lua) in the stub world of test_chase.py, on a 500 m loop, tight enough that a ghost
cornering at 20 m/s and up counts as sliding. The player is scored on the car's own angle (the harness's slip), each
rival as a ghost at the angle its skill (AI_LEVEL) gives it; the higher score wins and the places go by it.

The module is a setting mod's, not in this repo: it is read from the game's Content folder (git-ignored, copied there
from the mod) or from the mod's master copy, the first found. Without either the tests are skipped.

    python tools/sc_race_harness/test_drift.py
"""

import math
import os
import shutil
import sys
import tempfile

from test_chase import run, check, HERE

LOOP = 500.0
MODULE = 'format_touge_nights-drift'


def repo_roots():
    """This checkout, and the main checkout when this is one of its worktrees (.claude/worktrees/<name>)"""
    root = os.path.normpath(os.path.join(HERE, '..', '..'))
    roots = [root]
    parts = root.split(os.sep)
    if '.claude' in parts and 'worktrees' in parts:
        roots.append(os.sep.join(parts[:parts.index('.claude')]))
    return roots


def find_module():
    found = [os.path.join(r, 'Street Corsa', 'Content', 'settings', 'touge_nights', 'formats', 'drift.lua') for r in repo_roots()]
    found.append(os.path.join('D:' + os.sep, 'JUEGOS', 'STREET CORSA', 'mods', 'settings', 'touge_nights', 'formats', 'drift.lua'))
    return next((path for path in found if os.path.isfile(path)), None)


def drift_ini(rivals=1, levels=None):
    ini = {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.FORMAT': 'drift', 'STREET_CORSA.MODULE': MODULE,
           'STREET_CORSA.LAPS': '1', 'STREET_CORSA.SCORE': 'HIGHER', 'STREET_CORSA.RIVALS': str(rivals)}
    for index, level in (levels or {}).items():
        ini[f'CAR_{index}.AI_LEVEL'] = str(level)
    return ini


def participant(result, index):
    return next(p for p in result['participants'] if p['car_index'] == index)


def places(result):
    return {p['car_index']: p['performance']['final_position'] for p in result['participants']}


FOLDER = None


def race(name, slip, levels, speeds, rivals=1, **kwargs):
    """speeds: {car index: m/s}, or a function of (index, race seconds); slip(seconds): the player's angle"""
    speed = speeds if callable(speeds) else (lambda i, t: speeds[i])
    texts = []
    # Every plain math.random() comes up 0.5: a ghost's corner is its skill's angle exactly, no spread
    result, log, _, _ = run(name, rivals + 1, drift_ini(rivals, levels), None, None, None, length=LOOP, modules=FOLDER,
                            speed=lambda i, t: speed(i, t - 3), slip=lambda i, t: slip(t) if i == 0 else 0,
                            random=0.5, on_text=lambda seconds, text: texts.append(text), **kwargs)
    return result, log, texts


def sliding_the_whole_course_beats_a_middling_rival():
    result, log, _ = race('slide', lambda t: 35, {1: 90}, {0: 30, 1: 30})
    player, rival = participant(result, 0), participant(result, 1)
    check('the format is in the result', result['session']['format'] == 'drift', log)
    check('the race ran its course', result['session']['end_reason'] == 'FINISHED', log)
    check(f"both have a score ({player.get('score')}, {rival.get('score')})", player['score'] > 0 and rival['score'] > 0, log)
    check('the player won on points', player['performance']['final_position'] == 1, log)
    check('the result card says so', 'Drift battle over' in log and 'WON' in log, log)


def driving_straight_scores_nothing():
    result, log, _ = race('grip', lambda t: 0, {1: 90}, {0: 30, 1: 30})
    check(f"no points for grip driving ({participant(result, 0)['score']})", participant(result, 0)['score'] == 0, log)
    check('the rival won', participant(result, 1)['performance']['final_position'] == 1, log)


def a_spin_loses_the_combo_running():
    steady, log, _ = race('steady', lambda t: 35, {1: 90}, {0: 30, 1: 30})
    spun, log2, _ = race('spun', lambda t: 120 if 12 < t < 12.5 else 35, {1: 90}, {0: 30, 1: 30})
    check(f"a spin costs points ({participant(spun, 0)['score']} against {participant(steady, 0)['score']})",
          participant(spun, 0)['score'] < participant(steady, 0)['score'], log2)


def a_better_rival_scores_more():
    middling, _, _ = race('middling', lambda t: 35, {1: 85}, {0: 30, 1: 30})
    best, log, _ = race('best', lambda t: 35, {1: 100}, {0: 30, 1: 30})
    check(f"skill raises the ghost's score ({participant(middling, 1)['score']} to {participant(best, 1)['score']})",
          participant(best, 1)['score'] > participant(middling, 1)['score'], log)


def a_rival_that_never_corners_hard_scores_nothing():
    # 10 m/s round a 500 m loop pulls 1.3 m/s^2, short of a corner
    result, log, _ = race('slow rival', lambda t: 35, {1: 100}, {0: 30, 1: 10})
    check(f"no corner, no points ({participant(result, 1)['score']})", participant(result, 1)['score'] == 0, log)
    check('the sliding player won', participant(result, 0)['performance']['final_position'] == 1, log)


def a_field_is_ranked_by_score():
    # Three ghosts at 18, 42 and 29.2 degrees (AI_LEVEL 85, 100, 92); the player slides at 35: second of four
    result, log, texts = race('field', lambda t: 35, {1: 85, 2: 100, 3: 92}, lambda i, t: 30, rivals=3)
    got = places(result)
    check(f'ranked by score ({got})', got == {2: 1, 0: 2, 3: 3, 1: 4}, log)
    scores = [participant(result, i)['score'] for i in (2, 0, 3, 1)]
    check(f'the scores fall with the places ({scores})', scores == sorted(scores, reverse=True), log)
    check('the player lost to the best ghost', 'Drift battle over' in log and 'LOST' in log, log)
    check(f'the card says the place ({texts[-2:]})', '2ND OF 4' in texts, log)
    check("the board shows the best rival's score", any('RIVAL' in t for t in texts), log)


def each_rival_is_scored_on_its_own_place():
    # Rival 3 stops for good 8 s in (120 m round the loop: a share of 0.24) and is still on the course when the wait
    # runs out: its points are stretched to the whole course by its own share of it, not by rival 1's
    stop = 8.0
    result, log, _ = race('own place', lambda t: 35, {1: 92, 2: 92, 3: 92},
                          lambda i, t: 0 if i == 3 and t >= stop else (20 if i == 3 else 30), rivals=3)
    # The ghost's points as drift.lua counts them: 20 m/s at 29.2 degrees, the multiplier up a step every 1.5 s held
    angle = 18 + 24 * (0.92 - 0.85) / 0.15
    points, held = 0.0, 0.0
    for _ in range(int(round(stop / 0.05))):
        held += 0.05
        mult = min(5, 1 + math.floor(held / 1.5) * 0.25)
        points += 0.05 * 72 * (angle / 45) * mult
    share = (-40 + 20 * stop) / LOOP
    expected = points / share
    score = participant(result, 3)['score']
    check(f'rival 3 scored on its own share of the course ({score}, about {expected:.0f})', abs(score - expected) < 0.06 * expected, log)
    check('and the race waited for it as long as it waits', result['session']['end_reason'] == 'FINISHED', log)


if __name__ == '__main__':
    source = find_module()
    if not source:
        print('SKIPPED: no Touge Nights drift.lua (Street Corsa/Content/settings/touge_nights/formats or the mod\'s master copy)')
        sys.exit(0)
    FOLDER = tempfile.mkdtemp(prefix='sc_drift_')
    try:
        shutil.copyfile(source, os.path.join(FOLDER, MODULE + '.lua'))
        print(f'drift.lua from {source}')
        for test in (sliding_the_whole_course_beats_a_middling_rival, driving_straight_scores_nothing,
                     a_spin_loses_the_combo_running, a_better_rival_scores_more,
                     a_rival_that_never_corners_hard_scores_nothing, a_field_is_ranked_by_score,
                     each_rival_is_scored_on_its_own_place):
            test()
            print(f'ok  {test.__name__}')
    finally:
        shutil.rmtree(FOLDER, ignore_errors=True)
    sys.exit(0)
