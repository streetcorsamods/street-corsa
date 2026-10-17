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


SOUNDS = {"bench": bench, "wash": wash, "hone": hone, "lap": lap, "air": air, "grind": grind, "press": press, "road": road}


def main(out):
    folder = os.path.join(out, "work")
    os.makedirs(folder, exist_ok=True)
    for name, make in SOUNDS.items():
        sf.write(os.path.join(folder, name + ".ogg"), make().astype(np.float32), RATE, format="OGG", subtype="VORBIS")
    print(folder)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(OUT))
