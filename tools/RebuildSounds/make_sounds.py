"""
Makes the sounds of the work on a part at the garage's workbench (the parts view's "Work on it" jobs), synthesised
here (no recordings, nothing to credit but the game itself). Each loops under a step of a job:

  sfx/work/bench.ogg    hand tools: a wrench's taps and rattles, a ratchet now and then
  sfx/work/wash.ogg     a part scrubbed in the parts washer: a brush and the solvent
  sfx/work/hone.ogg     a hone in a drill going up and down a bore: the motor and the stones
  sfx/work/lap.ogg      a valve lapped in by hand, twirled back and forth on its seat, a tap now and then
  sfx/work/air.ogg      an air ratchet in bursts, the compressor chugging behind
  sfx/work/grind.ogg    the bench grinder's wire wheel: its whine and the bite of the part on it
  sfx/work/press.ogg    bearings and seals driven home: a hammer on a drift
  sfx/work/road.ogg     the drive to the parts counter and back: the road under the car

And the machine shop's (the machine jobs' steps, and the shop's own air):

  sfx/work/bore.ogg     a boring bar cutting a cylinder: the motor's drone, the tool's chatter going down the bore
  sfx/work/crank.ogg    the crank grinder: a big wheel's roar, the journal turning against it, coolant
  sfx/work/mill.ogg     a surfacing mill across a head: the cutter's whirr, the bite of each pass
  sfx/ambience/machine_shop.ogg   the shop round the counter: machines running out back, an air hose, a radio far off

And the garage's lift (the parts view's lift button):

  sfx/work/lift.ogg       the lift going up or down: the pump's motor and the oil through the valve (a loop)
  sfx/work/lift_stop.ogg  the lift at a stop: the valve shutting and the saddle's lock dropping in

  python make_sounds.py [<sfx folder>]   (default: the base game's, Street Corsa/Content/streetcorsa/sfx)

Each loop's end is crossfaded into its start, so it goes round without a seam, and levelled as the paint booth's are
(tools/PaintBoothScene/make_sounds.py). Needs numpy and soundfile.
"""
import os
import sys

import numpy as np
import soundfile as sf

RATE = 44100
RNG = np.random.default_rng(1964)
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Street Corsa", "Content", "streetcorsa", "sfx")
LOOP_RMS_DB = -24.0
SECONDS = 8.0


def band(x, low, high):
    """Noise between two frequencies, by FFT"""
    spectrum = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1 / RATE)
    shape = np.clip((freqs - low) / (low * 0.5), 0, 1) * np.clip((high - freqs) / (high * 0.3), 0, 1)
    return np.fft.irfft(spectrum * shape, len(x))


def wander(seconds, rate_hz, depth):
    """A slow wander round 1"""
    points = max(4, int(seconds * rate_hz))
    knots = 1 + (RNG.random(points) - 0.5) * 2 * depth
    return np.interp(np.linspace(0, points - 1, int(seconds * RATE)), np.arange(points), knots)


def seamless(x, fade_seconds):
    """The end crossfaded into the start: a loop with no seam"""
    n = int(fade_seconds * RATE)
    head, body, tail = x[:n], x[n:-n], x[-n:]
    ramp = np.linspace(0, 1, n)
    return np.concatenate([tail * (1 - ramp) + head * ramp, body])


def level(x, rms_db):
    rms = np.sqrt(np.mean(x ** 2)) + 1e-12
    y = x * (10 ** (rms_db / 20) / rms)
    peak = np.max(np.abs(y))
    return y / peak * 0.89 if peak > 0.89 else y


def ring(seconds, freqs, decay):
    """A struck piece of steel: a few partials dying away"""
    t = np.arange(int(seconds * RATE)) / RATE
    out = np.zeros_like(t)
    for i, f in enumerate(freqs):
        out += np.sin(2 * np.pi * f * t + RNG.random() * 6.28) * np.exp(-t * decay * (1 + i * 0.6)) / (1 + i)
    attack = np.minimum(1, t / 0.0015)
    return out * attack


def thud(seconds, freq, decay):
    """Something heavy hit: a low knock with a click on top"""
    t = np.arange(int(seconds * RATE)) / RATE
    knock = np.sin(2 * np.pi * freq * t * (1 - 0.3 * t)) * np.exp(-t * decay)
    click = band(RNG.standard_normal(len(t)), 1500, 6000) * np.exp(-t * 120) * 0.5
    return knock + click


def place(track, sound, at, gain=1.0):
    start = int(at * RATE)
    end = min(len(track), start + len(sound))
    if start < len(track):
        track[start:end] += sound[:end - start] * gain


def total():
    return int((SECONDS + 1.0) * RATE)


def bench():
    n = total()
    track = band(RNG.standard_normal(n), 200, 3000) * 0.01
    at = 0.2
    while at < SECONDS + 0.8:
        kind = RNG.random()
        if kind < 0.55:
            # A wrench set down or tapping a bolt
            place(track, ring(0.5, [2400 * (0.8 + RNG.random() * 0.5), 3900, 6100], 18), at, 0.3 + RNG.random() * 0.4)
            at += 0.25 + RNG.random() * 0.7
        elif kind < 0.8:
            # A ratchet: a run of clicks
            clicks = RNG.integers(5, 12)
            for c in range(clicks):
                place(track, band(RNG.standard_normal(int(0.012 * RATE)), 2500, 9000) * np.hanning(int(0.012 * RATE)), at + c * 0.055, 0.6)
            at += clicks * 0.055 + 0.4 + RNG.random() * 0.6
        else:
            # Parts rattling in a tray
            for c in range(RNG.integers(3, 7)):
                place(track, ring(0.15, [3200 + RNG.random() * 2500, 7000], 40), at + c * (0.03 + RNG.random() * 0.05), 0.25)
            at += 0.6 + RNG.random() * 0.8
    return level(seamless(track, 0.5), LOOP_RMS_DB)


def wash():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    strokes = 0.5 + 0.5 * np.sin(2 * np.pi * 2.6 * t * wander(SECONDS + 1.0, 0.5, 0.15)) ** 2
    brush = band(white, 1500, 9000) * strokes * 0.8
    slosh = band(white, 120, 900) * (0.3 + 0.3 * np.sin(2 * np.pi * 0.7 * t)) * 0.6
    bubbles = np.zeros(n)
    for _ in range(int(SECONDS * 6)):
        f = 300 + RNG.random() * 700
        place(bubbles, np.sin(2 * np.pi * f * np.arange(int(0.04 * RATE)) / RATE * (1 + np.linspace(0, 0.6, int(0.04 * RATE)))) * np.hanning(int(0.04 * RATE)),
              RNG.random() * SECONDS, 0.15)
    return level(seamless(brush + slosh + bubbles, 0.6), LOOP_RMS_DB)


def hone():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    speed = wander(SECONDS + 1.0, 0.8, 0.03)
    phase = np.cumsum(2 * np.pi * 36 * speed / RATE)
    motor = 0.35 * np.sin(phase) + 0.2 * np.sin(2 * phase) + 0.1 * np.sin(17 * phase)
    whine = 0.06 * np.sin(np.cumsum(2 * np.pi * 760 * speed / RATE))
    stroke = 0.55 + 0.45 * np.sin(2 * np.pi * 1.4 * t)
    stones = band(white, 900, 7000) * stroke * 0.9
    return level(seamless(motor + whine + stones, 0.6), LOOP_RMS_DB)


def lap():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    twirl = np.abs(np.sin(2 * np.pi * 1.25 * t))
    # Lifted off its seat for fresh compound every couple of seconds: the twirl stops and the valve taps the seat
    rest = np.ones(n)
    taps = []
    at = 1.6
    while at < SECONDS + 0.8:
        start, length = int(at * RATE), int(0.45 * RATE)
        rest[start:start + length] = 0.05
        taps.append(at + 0.35)
        at += 1.8 + RNG.random() * 0.9
    track = band(white, 2500, 11000) * twirl * rest + band(white, 400, 1800) * twirl * rest * 0.3
    for tap in taps:
        place(track, ring(0.3, [1900, 3100, 5200], 25), tap, 0.5)
    return level(seamless(track, 0.5), LOOP_RMS_DB)


def air():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    track = np.zeros(n)
    at = 0.3
    while at < SECONDS + 0.5:
        length = 0.4 + RNG.random() * 0.9
        m = int(length * RATE)
        tt = np.arange(m) / RATE
        buzz = np.sign(np.sin(2 * np.pi * 42 * tt)) * 0.3 + band(RNG.standard_normal(m), 700, 5000) * 0.8
        env = np.minimum(1, tt / 0.02) * np.minimum(1, (length - tt) / 0.05)
        place(track, buzz * env, at, 0.6)
        place(track, band(RNG.standard_normal(int(0.25 * RATE)), 3000, 12000) * np.linspace(1, 0, int(0.25 * RATE)), at + length, 0.25)
        at += length + 0.5 + RNG.random() * 1.2
    chug = np.sin(2 * np.pi * 22 * t) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 11 * t)))
    compressor = band(chug + band(white, 60, 300) * 0.5, 40, 600) * 0.25
    return level(seamless(track + compressor, 0.5), LOOP_RMS_DB)


def grind():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    whine = 0.12 * np.sin(2 * np.pi * 2950 * t) + 0.08 * np.sin(2 * np.pi * 59 * t) + band(white, 2000, 5000) * 0.15
    bite = np.zeros(n)
    at = 0.4
    while at < SECONDS + 0.5:
        length = 0.6 + RNG.random() * 1.4
        m = int(length * RATE)
        env = np.hanning(m) ** 0.5
        place(bite, band(RNG.standard_normal(m), 1500, 12000) * env * (0.7 + 0.3 * np.sin(2 * np.pi * 7 * np.arange(m) / RATE)), at)
        at += length + 0.3 + RNG.random() * 0.8
    return level(seamless(whine + bite, 0.5), LOOP_RMS_DB)


def press():
    n = total()
    track = band(RNG.standard_normal(n), 200, 2500) * 0.01
    at = 0.3
    while at < SECONDS + 0.5:
        blows = RNG.integers(2, 6)
        pitch = 1 + RNG.random() * 0.3
        for b in range(blows):
            place(track, thud(0.25, 140 * pitch, 30), at, 0.7)
            place(track, ring(0.6, [1500 * pitch, 2700 * pitch, 4400 * pitch], 9), at, 0.35)
            at += 0.32 + RNG.random() * 0.08
            pitch *= 1.04  # it goes home: the drift rings higher as it seats
        at += 0.8 + RNG.random() * 1.0
    return level(seamless(track, 0.5), LOOP_RMS_DB)


def road():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    speed = wander(SECONDS + 1.0, 0.3, 0.08)
    phase = np.cumsum(2 * np.pi * 38 * speed / RATE)
    engine = 0.3 * np.sin(phase) + 0.2 * np.sin(2 * phase) + 0.12 * np.sin(4 * phase)
    tyres = band(white, 150, 1400) * 0.5 * speed
    wind = band(white, 400, 3500) * 0.15 * wander(SECONDS + 1.0, 0.5, 0.3)
    return level(seamless(band(engine, 25, 400) + tyres + wind, 1.0), LOOP_RMS_DB)


def bore():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    speed = wander(SECONDS + 1.0, 0.4, 0.02)
    phase = np.cumsum(2 * np.pi * 24 * speed / RATE)
    motor = 0.3 * np.sin(phase) + 0.18 * np.sin(2 * phase) + 0.1 * np.sin(3 * phase)
    # The tool goes down the bore over about three seconds, chattering, then comes back up quiet
    track = np.zeros(n)
    at = 0.2
    while at < SECONDS + 0.5:
        down = 2.6 + RNG.random() * 0.8
        m = int(down * RATE)
        tt = np.arange(m) / RATE
        chatter = band(RNG.standard_normal(m), 1800, 7000) * (0.6 + 0.4 * np.sin(2 * np.pi * 31 * tt)) * np.minimum(1, tt / 0.15)
        squeal = 0.05 * np.sin(2 * np.pi * (1320 + 40 * tt) * tt)
        place(track, (chatter + squeal) * np.minimum(1, (down - tt) / 0.2), at, 0.7)
        at += down + 1.0 + RNG.random() * 0.6
    return level(seamless(band(motor, 20, 500) + band(white, 300, 1200) * 0.08 + track, 0.6), LOOP_RMS_DB)


def crank():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    roar = band(white, 700, 4500) * (0.75 + 0.25 * np.sin(2 * np.pi * 0.9 * t)) * 0.7
    wheel = 0.08 * np.sin(2 * np.pi * 1760 * t) + 0.12 * np.sin(2 * np.pi * 29 * t)
    # The journal turning: a slow beat as the throw comes round, and the coolant splashing
    beat = band(white, 150, 600) * (0.5 + 0.5 * np.sin(2 * np.pi * 1.6 * t) ** 8) * 0.4
    coolant = band(white, 2500, 10000) * 0.12 * wander(SECONDS + 1.0, 2, 0.4)
    return level(seamless(roar + wheel + beat + coolant, 0.6), LOOP_RMS_DB)


def mill():
    n = total()
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    speed = wander(SECONDS + 1.0, 0.5, 0.02)
    whirr = 0.1 * np.sin(np.cumsum(2 * np.pi * 410 * speed / RATE)) + 0.06 * np.sin(np.cumsum(2 * np.pi * 820 * speed / RATE))
    track = np.zeros(n)
    at = 0.3
    while at < SECONDS + 0.5:
        length = 1.8 + RNG.random() * 1.2
        m = int(length * RATE)
        tt = np.arange(m) / RATE
        # Each tooth of the cutter bites: a fast flutter of noise
        flutter = 0.55 + 0.45 * np.sign(np.sin(2 * np.pi * 54 * tt))
        place(track, band(RNG.standard_normal(m), 1200, 9000) * flutter * np.hanning(m) ** 0.3, at, 0.6)
        at += length + 0.6 + RNG.random() * 0.8
    return level(seamless(whirr + band(white, 100, 700) * 0.1 + track, 0.5), LOOP_RMS_DB)


def machine_shop():
    """The shop's air round the counter: 20 seconds of machines out back, an air hose now and then, a radio far off"""
    seconds = 20.0
    n = int((seconds + 1.0) * RATE)
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    hum = 0.08 * np.sin(2 * np.pi * 60 * t) + 0.05 * np.sin(2 * np.pi * 120 * t) + band(white, 80, 400) * 0.15
    machines = band(white, 600, 3500) * 0.18 * wander(seconds + 1.0, 0.15, 0.5)
    track = np.zeros(n)
    at = 1.0
    while at < seconds:
        kind = RNG.random()
        if kind < 0.4:
            m = int((0.3 + RNG.random() * 0.6) * RATE)
            place(track, band(RNG.standard_normal(m), 2500, 11000) * np.hanning(m), at, 0.25)
        elif kind < 0.7:
            place(track, ring(0.8, [1100 + RNG.random() * 800, 2300, 3900], 7), at, 0.12)
        else:
            m = int(2.5 * RATE)
            tt = np.arange(m) / RATE
            radio = band(np.sin(2 * np.pi * 220 * tt) * np.sin(2 * np.pi * 3 * tt) + RNG.standard_normal(m) * 0.3, 400, 2500)
            place(track, radio * np.hanning(m), at, 0.05)
        at += 1.2 + RNG.random() * 2.5
    return level(seamless(hum + machines + track, 1.0), -27.0)


SOUNDS = {"bench": bench, "wash": wash, "hone": hone, "lap": lap, "air": air, "grind": grind, "press": press, "road": road,
          "bore": bore, "crank": crank, "mill": mill}

def lift():
    """An in-ground lift on the move: the pump's motor humming low, the oil hissing through the valve, a faint whine"""
    n = total()
    t = np.arange(n) / RATE
    hum = (np.sin(2 * np.pi * 58 * t) + 0.5 * np.sin(2 * np.pi * 116 * t) + 0.2 * np.sin(2 * np.pi * 174 * t)) * wander(SECONDS + 1, 2, 0.08)
    whine = np.sin(2 * np.pi * 410 * t + 3 * np.sin(2 * np.pi * 0.7 * t)) * 0.06
    oil = band(RNG.standard_normal(n), 300, 2200) * 0.35 * wander(SECONDS + 1, 3, 0.25)
    return level(seamless(band(hum, 30, 600) + whine + oil, 0.6), LOOP_RMS_DB)


def lift_stop():
    """The lift at its stop: the valve shutting with a hiss let out, and the saddle's lock dropping in with a clunk"""
    n = int(1.2 * RATE)
    track = np.zeros(n)
    t = np.arange(n) / RATE
    track += band(RNG.standard_normal(n), 800, 5000) * np.exp(-t * 9) * 0.25
    place(track, thud(0.8, 70, 9), 0.12, 1.0)
    place(track, ring(0.6, [520, 1310, 2140], 14), 0.12, 0.35)
    return level(track, -18.0)


# Made after the work's, so a sound added here leaves the others as they were (they share one random draw)
AMBIENCE = {"machine_shop": machine_shop}

# And after those, the lift's
LIFT = {"lift": lift, "lift_stop": lift_stop}


def write(path, x):
    """An OGG written a block at a time: libsndfile falls over writing a long one in one go"""
    with sf.SoundFile(path, "w", RATE, 1, format="OGG", subtype="VORBIS") as f:
        for start in range(0, len(x), 16384):
            f.write(x[start:start + 16384].astype(np.float32))


def main(out):
    folder = os.path.join(out, "work")
    os.makedirs(folder, exist_ok=True)
    for name, make in SOUNDS.items():
        write(os.path.join(folder, name + ".ogg"), make())
    ambience = os.path.join(out, "ambience")
    os.makedirs(ambience, exist_ok=True)
    for name, make in AMBIENCE.items():
        write(os.path.join(ambience, name + ".ogg"), make())
    for name, make in LIFT.items():
        write(os.path.join(folder, name + ".ogg"), make())
    print(folder)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(OUT))
