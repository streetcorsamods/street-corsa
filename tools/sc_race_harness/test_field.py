"""
Runs the race mode (apps/new-modes/sc_race/mode.lua) with a field of rivals (race.ini [STREET_CORSA] RIVALS) in the
stub world of test_chase.py, on its 2000 m loop: every racer a place of its own, a train start (STAGGER), the police
after any racer.

    python tools/sc_race_harness/test_field.py
    python tools/sc_race_harness/test_field.py --golden   (writes the C# contract fixture again, see golden_file)
"""

import json
import os
import sys

from test_chase import run, check, paths, HERE, GOLDEN_CONTEXT_ID

FIXTURES = os.path.join(HERE, '..', '..', 'tests', 'StreetCorsa.Tests', 'Fixtures')


def field_ini(rivals, **more):
    ini = {'STREET_CORSA.RACE_TYPE': 'ROAD', 'STREET_CORSA.RIVALS': str(rivals)}
    ini.update({'STREET_CORSA.' + key: value for key, value in more.items()})
    return ini


def places(result):
    return {p['car_index']: p['performance']['final_position'] for p in result['participants']}


# Five rivals: two quicker than the player and over the line first, two slower still on the road when the player
# crosses it, and one wrecked early on. The player's 30 m/s takes the line third.
FIELD_SPEEDS = {0: 30, 1: 33, 2: 31, 3: 25, 4: 20, 5: 28}


def field_speed(i, t):
    return FIELD_SPEEDS.get(i, 12)


def a_field_of_five_is_ranked_each_racer_its_own_place():
    texts = []
    result, log, _, _ = run('field', 6, field_ini(5), None, None, None, speed=field_speed, crash_at={4: 20},
                            on_text=lambda seconds, text: texts.append(text))
    got = places(result)
    check(f'six racers, the police none ({sorted(got)})', sorted(got) == [0, 1, 2, 3, 4, 5], log)
    check(f'every racer a place of its own, 1 to 6 ({got})', sorted(got.values()) == [1, 2, 3, 4, 5, 6], log)
    check(f'the finishers in the order they crossed the line ({got})', got[1] == 1 and got[2] == 2 and got[0] == 3, log)
    check(f'the cars still running by how far they got ({got})', got[5] == 4 and got[3] == 5, log)
    check(f'the wrecked rival last ({got})', got[4] == 6 and 'Car 4 crashed' in log, log)
    check('the race ends at the line', result['session']['end_reason'] == 'FINISHED', log)
    check('one wrecked rival of five is no walkover', 'Field cleared' not in log and 'it is yours' not in log, log)
    check(f'the card says the place ({texts[-2:]})', '3RD OF 6' in texts, log)
    check('the HUD shows the place in the field', any('P3/6' in t for t in texts) and any('AHEAD' in t for t in texts), log)
    check('the HUD names the car ahead', any('AHEAD' in t and 'Rival' in t for t in texts), log)
    check('the result says 2.0', result['metadata']['schema_version'] == '2.0', log)


def every_rival_out_the_race_is_the_players():
    # Two rivals, both quicker, both wrecked: the player finishes alone and wins, told so once the last one is out
    speeds = {0: 25, 1: 30, 2: 30}
    result, log, _, _ = run('cleared', 3, field_ini(2), None, None, None, speed=lambda i, t: speeds[i],
                            crash_at={1: 10, 2: 20})
    got = places(result)
    check(f'the player won ({got})', got[0] == 1, log)
    check('the wrecked rivals after him, the one that got further first', got[2] == 2 and got[1] == 3, log)
    check('told once every rival was out, not before', log.count('Field cleared') == 1, log)


def a_train_start_lets_the_cars_go_one_after_the_other():
    # Four cars two seconds apart, the player third on the grid: rival 1 goes at the green, rival 2 two seconds on, the
    # player at four, rival 3 at six
    texts = []
    result, log, _, _ = run('stagger', 4, field_ini(3, STAGGER='2'), None, None, None, speed=lambda i, t: 30,
                            grid={0: 3, 1: 1, 2: 2, 3: 4}, hold_brakes=True,
                            on_text=lambda seconds, text: texts.append(text))
    goes = [line for line in log.split('\n') if 'Train start: car' in line]
    check(f'three cars held, let go in their order ({goes})', [g.split('car ')[1].split(' ')[0] for g in goes] == ['2', '0', '3'], log)
    times = [float(g.split('goes at ')[1].split(' s')[0]) for g in goes]
    check(f'two seconds apart ({times})', all(abs(a - b) < 0.11 for a, b in zip(times, [2, 4, 6])), log)
    check('the player was told to hold and to go', 'you go in 4 s' in log and 'Go!' in log, log)
    got = places(result)
    check(f'and they finish in the order they left ({got})', got == {1: 1, 2: 2, 0: 3, 3: 4}, log)


def a_held_rival_is_kept_from_retiring_while_it_waits():
    # AC sends a car that stands still to the pits: every rival held on the grid behind the player is kept from it
    # until its turn
    result, log, _, _ = run('stagger retire', 4, field_ini(3, STAGGER='2'), None, None, None, speed=lambda i, t: 30,
                            hold_brakes=True)
    held = log.split('Train start: car 3 goes')[0]
    check('each held rival kept from retiring while it waits', all(f'kept from retiring: car {i}' in held for i in (1, 2, 3)), log)


def put_back_in_the_pits_the_player_is_out_behind_every_car_running():
    # The player leads three rivals and goes to the pits half a lap in: the pit lane reads near the end of the lap, but
    # a player out of the race is placed after every car still running, not first
    speeds = {0: 35, 1: 30, 2: 29, 3: 28}
    result, log, _, _ = run('field pits', 4, field_ini(3), None, None, None, speed=lambda i, t: speeds[i], pits_at={0: 30})
    got = places(result)
    check(f"the race abandoned ({result['session']['end_reason']})", result['session']['end_reason'] == 'ABANDONED', log)
    check(f'the player last, the rivals by how far they got ({got})', got == {1: 1, 2: 2, 3: 3, 0: 4}, log)


def no_stagger_everybody_goes_at_the_green():
    result, log, _, _ = run('no stagger', 4, field_ini(3), None, None, None, speed=lambda i, t: 30 - i)
    check('nobody held', 'Train start' not in log, log)
    check('the quickest wins', places(result)[0] == 1, log)


POLICE_SPEEDS = {0: 30, 1: 40, 2: 15, 3: 29, 4: 12, 5: 45}


def police_go_after_the_racer_nearest_them():
    # Three rivals and two cops. The patrol shows up where the player is: the first cop takes him, the second the
    # rival nearest him, car 3 just behind (not car 1, far ahead). That cop is quick: it gets past car 3, busts it, and
    # once free goes after a racer no cop has. The player's cop is slow: he gets away.
    ini = field_ini(3, POLICE='4,5', POLICE_SPOT='0.1')
    result, log, _, _ = run('field police', 6, ini, None, None, None, speed=lambda i, t: POLICE_SPEEDS[i])
    pursuit = result['pursuit']
    check('the first cop after the player', 'Cop 4 after car 0' in log, log)
    check('the second after the rival nearest him, not car 1', 'Cop 5 after car 3' in log, log)
    check('it got past car 3', 'Cop 5 got past car 3' in log, log)
    racers = {r['car_index']: r for r in pursuit['racers']}
    check(f'pursuit.racers has the busted rival ({pursuit["racers"]})', racers.get(3, {}).get('outcome') == 'BUSTED', log)
    check('and the player, who got away', racers.get(0, {}).get('outcome') == 'ESCAPED' and pursuit['player'] == 'ESCAPED', log)
    check('every entry was chased', all(r['chased'] for r in pursuit['racers']), log)
    check('the busted rival was held: it ends last, out of the race', places(result)[3] == 4, log)
    check('the message names the busted driver', 'Rival 3 got caught by the police' in log, log)
    check('the free cop went after another racer', 'Cop 5 after car' in log.split('got past car 3')[1] and 'free again' in log, log)
    check('the police are not participants', len(result['participants']) == 4, log)


def golden_file(write=False):
    # The C# side reads this file as the mode's contract for a field (RaceResultContractTests): the five-rival road
    # race above, with the police after it, the player getting away and over the line third; one rival wrecked
    name = 'sc_race_field_result.json'
    ini = field_ini(5, CONTEXT_ID=GOLDEN_CONTEXT_ID, FORMAT='circuit', MODULE='format_road', LAPS='1', CAR_0_AXLE='2500',
                    POLICE='6,7', POLICE_SPOT='0.1')
    result, log, _, _ = run(name, 8, ini, None, None, None, speed=field_speed, crash_at={4: 20})
    got = places(result)
    check(f'the golden race: unique places, the player third ({got})', sorted(got.values()) == [1, 2, 3, 4, 5, 6] and got[0] == 3, log)
    check('with a pursuit of each racer chased', result['pursuit']['racers'], log)
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
    for test in (golden_file, a_field_of_five_is_ranked_each_racer_its_own_place, every_rival_out_the_race_is_the_players,
                 a_train_start_lets_the_cars_go_one_after_the_other, a_held_rival_is_kept_from_retiring_while_it_waits,
                 put_back_in_the_pits_the_player_is_out_behind_every_car_running, no_stagger_everybody_goes_at_the_green,
                 police_go_after_the_racer_nearest_them):
        test()
        print(f'ok  {test.__name__}')
    sys.exit(0)
