"""
Makes the paint booth's two sounds, synthesised here (no recordings, nothing to credit but the game itself):

  sfx/booth/spray.ogg       the spray gun: a hiss of air and paint, its pressure wandering a little, looping
  sfx/ambience/booth.ogg    the booth: the exhaust fans' hum and rush of air, the shop's compressor far off, looping

  python make_sounds.py [<sfx folder>]   (default: the core's, Street Corsa/Content/streetcorsa/sfx)

Each loop's end is crossfaded into its start, so it goes round without a seam, and levelled as build_sfx.py levels
its loops. Needs numpy and soundfile.
"""
import os
import sys

import numpy as np
import soundfile as sf

RATE = 44100
RNG = np.random.default_rng(1966)
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Street Corsa", "Content", "streetcorsa", "sfx")
LOOP_RMS_DB = -26.0


def one_pole(x, cutoff):
    """A gentle low-pass: one pole"""
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


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


def spray(seconds=6.0):
    n = int((seconds + 1.0) * RATE)
    white = RNG.standard_normal(n)
    hiss = band(white, 1800, 14000) * wander(seconds + 1.0, 3.0, 0.12)
    body = band(white, 300, 2200) * 0.35 * wander(seconds + 1.0, 1.5, 0.2)
    # The paint's spatter: a faint crackle in the hiss
    crackle = (RNG.random(n) < 0.0008) * RNG.standard_normal(n) * 1.5
    crackle = band(crackle, 2000, 9000)
    return level(seamless(hiss + body + crackle, 1.0), -18.0)


def ambience(seconds=12.0):
    n = int((seconds + 2.0) * RATE)
    t = np.arange(n) / RATE
    white = RNG.standard_normal(n)
    rush = band(white, 120, 2500) * 0.6 * wander(seconds + 2.0, 0.4, 0.08)
    # The fans: their motors' hum and the blades' beat
    hum = 0.25 * np.sin(2 * np.pi * 59.7 * t) + 0.12 * np.sin(2 * np.pi * 119.4 * t) + 0.05 * np.sin(2 * np.pi * 179.1 * t)
    beat = 0.4 + 0.6 * (0.5 + 0.5 * np.sin(2 * np.pi * 11.6 * t))
    blades = band(white, 300, 900) * 0.25 * beat
    # The compressor in the shop, now and then, far off
    compressor = np.zeros(n)
    start = int(3.0 * RATE)
    length = int(5.0 * RATE)
    env = np.concatenate([np.linspace(0, 1, int(0.6 * RATE)), np.ones(length - int(1.4 * RATE)), np.linspace(1, 0, int(0.8 * RATE))])
    chug = np.sin(2 * np.pi * 24 * t[:len(env)]) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 12 * t[:len(env)])))
    compressor[start:start + len(env)] = one_pole(chug + band(white[:len(env)], 80, 400) * 0.6, 500) * env * 0.35
    return level(seamless(rush + hum * 0.4 + blades + compressor, 2.0), LOOP_RMS_DB)


def main(out):
    os.makedirs(os.path.join(out, "booth"), exist_ok=True)
    os.makedirs(os.path.join(out, "ambience"), exist_ok=True)
    sf.write(os.path.join(out, "booth", "spray.ogg"), spray().astype(np.float32), RATE, format="OGG", subtype="VORBIS")
    sf.write(os.path.join(out, "ambience", "booth.ogg"), ambience().astype(np.float32), RATE, format="OGG", subtype="VORBIS")
    print(out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(OUT))
