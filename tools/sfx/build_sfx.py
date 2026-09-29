"""
Builds the base game's sound effects (Content/streetcorsa/sfx) out of the downloaded candidates
(Street Corsa/Assets/Sound/Candidates, not in the repo: freesound HQ previews and Kenney's packs, every one CC0 or
credited). Each cue is cut, trimmed and levelled here, so the game only plays what is in sfx.json:

  one-shots  trimmed to where they sound, faded at the ends, mono, levelled by how loud they are while they sound
             (a dense thud and a single click alike), never past -1 dBFS; a recording of many takes is cut into its
             takes, and the takes that fit are the cue's variations
  loops      a stretch of an ambience recording, its end crossfaded into its start so it loops without a seam,
             levelled to one loudness for all places

The mix between cues is sfx.json's "volume", not the files'. Needs numpy and soundfile (pip install soundfile).

  python tools/sfx/build_sfx.py            builds into Content/streetcorsa/sfx
"""
import csv
import json
import os
import sys

import numpy as np
import soundfile as sf

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CANDIDATES = os.path.join(ROOT, "Street Corsa", "Assets", "Sound", "Candidates")
OUT = os.path.join(ROOT, "Street Corsa", "Content", "streetcorsa", "sfx")

ONE_SHOT_PEAK_DB = -1.0
ONE_SHOT_ACTIVE_RMS_DB = -18.0
LOOP_RMS_DB = -26.0


def find(prefix):
    """A candidate by its freesound id (the start of its file name) or its path under the candidates"""
    direct = os.path.join(CANDIDATES, prefix)
    if os.path.isfile(direct):
        return direct
    for dirpath, _, files in os.walk(CANDIDATES):
        for f in files:
            if f.startswith(prefix + "_") and f.endswith(".ogg"):
                return os.path.join(dirpath, f)
    raise FileNotFoundError(prefix)


def load(path, mono=True):
    data, rate = sf.read(path, always_2d=True, dtype="float32")
    if mono:
        data = data.mean(axis=1, keepdims=True)
    return data, rate


def envelope_db(x, rate, ms=10):
    hop = max(1, rate * ms // 1000)
    n = (len(x) // hop) * hop
    frames = np.abs(x[:n]).max(axis=1).reshape(-1, hop)
    env = np.sqrt((frames ** 2).mean(axis=1))
    return 20 * np.log10(env + 1e-9), hop


def fade(x, rate, in_ms=2, out_ms=40):
    x = x.copy()
    a = min(len(x), rate * in_ms // 1000)
    b = min(len(x), rate * out_ms // 1000)
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)[:, None]
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b)[:, None]
    return x


def trim(x, rate, floor_db=-45.0, pre_ms=5, tail_db=-50.0):
    """From just before it starts to where its tail has died away"""
    env, hop = envelope_db(x, rate)
    peak = env.max()
    on = np.where(env > peak + floor_db)[0]
    if len(on) == 0:
        return x
    tail = np.where(env > peak + tail_db)[0]
    start = max(0, on[0] * hop - rate * pre_ms // 1000)
    end = min(len(x), (tail[-1] + 1) * hop)
    return fade(x[start:end], rate)


def peak_to(x, db):
    peak = np.abs(x).max()
    return x if peak <= 0 else x * (10 ** (db / 20) / peak)


def level(x, rate):
    """To one loudness over the part that sounds (within 30 dB of its loudest), its peak held under the ceiling"""
    env, hop = envelope_db(x, rate)
    active = env > env.max() - 30
    frames = x[: len(env) * hop].reshape(len(env), hop, -1)[active]
    rms = float(np.sqrt((frames ** 2).mean())) if frames.size else 0.0
    if rms <= 0:
        return x
    y = x * (10 ** (ONE_SHOT_ACTIVE_RMS_DB / 20) / rms)
    peak = np.abs(y).max()
    ceiling = 10 ** (ONE_SHOT_PEAK_DB / 20)
    return y * (ceiling / peak) if peak > ceiling else y


def takes(x, rate, gap_ms=120, floor_db=-35.0, min_s=0.15, max_s=3.0):
    """A recording of many takes cut at its silences; each take trimmed, the ones of a usable length kept"""
    env, hop = envelope_db(x, rate)
    peak = env.max()
    loud = env > peak + floor_db
    gap = max(1, gap_ms // 10)
    spans, start, quiet = [], None, 0
    for i, on in enumerate(loud):
        if on:
            if start is None:
                start = i
            quiet = 0
        elif start is not None:
            quiet += 1
            if quiet >= gap:
                spans.append((start, i - quiet + 1))
                start, quiet = None, 0
    if start is not None:
        spans.append((start, len(loud)))
    out = []
    for a, b in spans:
        # A little either side, for the attack and the ring
        seg = x[max(0, a * hop - rate // 100): min(len(x), b * hop + rate // 5)]
        seg = trim(seg, rate)
        if min_s <= len(seg) / rate <= max_s:
            out.append(seg)
    return out


def loudest(segments, count):
    """The strongest takes, in the order they were recorded"""
    ranked = sorted(range(len(segments)), key=lambda i: -float(np.sqrt((segments[i] ** 2).mean())))[:count]
    return [segments[i] for i in sorted(ranked)]


def loop(x, rate, start_s, seconds, xfade_s=3.0):
    """A seamless loop: the stretch's last few seconds crossfaded (equal power) into its first"""
    a = int(start_s * rate)
    n = int(seconds * rate)
    f = int(xfade_s * rate)
    seg = x[a: a + n + f]
    if len(seg) < n + f:
        raise ValueError("recording too short for the loop")
    body = seg[:n].copy()
    t = np.linspace(0, np.pi / 2, f)[:, None]
    body[:f] = seg[:f] * np.sin(t) + seg[n: n + f] * np.cos(t)
    return body


def rms_to(x, db, ceiling_db=-1.0):
    rms = float(np.sqrt((x ** 2).mean()))
    x = x * (10 ** (db / 20) / max(rms, 1e-9))
    ceiling = 10 ** (ceiling_db / 20)
    # A soft ceiling for the odd bang in a room tone, rather than turning the whole loop down for it
    return np.tanh(x / ceiling) * ceiling


def write(rel, x, rate):
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # In blocks: libsndfile's Vorbis encoder crashes the process on a long file written in one go
    with sf.SoundFile(path, "w", rate, x.shape[1], format="OGG", subtype="VORBIS") as fh:
        for i in range(0, len(x), 8192):
            fh.write(np.ascontiguousarray(x[i:i + 8192]))
    return rel.replace("\\", "/")


# ---- the cues ---------------------------------------------------------------------------------------------------

sources = {}  # written file -> candidate files it was made of


def one_shot(rel, candidate, gain_db=0.0):
    path = find(candidate)
    x, rate = load(path)
    y = level(trim(x, rate), rate) * 10 ** (gain_db / 20)
    sources[write(rel, y, rate)] = [path]
    return rel.replace("\\", "/")


def takes_of(folder, candidate, count, **kw):
    path = find(candidate)
    x, rate = load(path)
    got = loudest(takes(x, rate, **kw), count)
    files = []
    for i, seg in enumerate(got):
        rel = f"{folder}/{os.path.basename(folder)}_{candidate.split('/')[-1][:8]}_{i + 1}.ogg"
        sources[write(rel, level(seg, rate), rate)] = [path]
        files.append(rel)
    return files


def ambience(rel, candidate, start_s, seconds):
    path = find(candidate)
    x, rate = load(path, mono=False)
    y = rms_to(loop(x, rate, start_s, seconds), LOOP_RMS_DB)
    sources[write(rel, y, rate)] = [path]
    return rel


def kenney(rel, name):
    return one_shot(rel, "kenney/impact-sounds/Audio/" + name + ".ogg")


def build():
    cues = {}

    def cue(name, bus, files, volume, pitch=0.0, loop=False, cooldown=0.0):
        entry = {"bus": bus, "volume": volume, "files": files}
        if pitch:
            entry["pitch"] = pitch
        if loop:
            entry["loop"] = True
        if cooldown:
            entry["cooldown"] = cooldown
        cues[name] = entry

    # Menus: the player's picks
    cue("ui.click", "interface", [one_shot("ui/click.ogg", "423957")], 0.45, pitch=0.04, cooldown=0.03)
    cue("ui.lights", "interface", [one_shot("ui/lights.ogg", "508071")], 0.55, pitch=0.02)
    cue("ui.purchase", "interface", [one_shot("ui/purchase.ogg", "209578")], 0.6)

    # Parts coming off and going on: a socket wrench, or the air gun for the heavy drivetrain bolts
    ratchets = [one_shot("parts/ratchet_1.ogg", "551497"), one_shot("parts/ratchet_2.ogg", "319996"),
                one_shot("parts/ratchet_3.ogg", "616628")]
    ratchets += takes_of("parts/ratchet_takes", "591529", 2, min_s=0.7, max_s=1.8)
    cue("part.ratchet", "effects", ratchets, 0.55, pitch=0.05)

    impacts = [one_shot(f"parts/impact_{i + 1}.ogg", c) for i, c in enumerate(["386311", "386315", "386319", "386321", "386327"])]
    cue("part.impact", "effects", impacts, 0.45, pitch=0.03)

    # A part set down in its place
    cue("part.clank", "effects", [kenney(f"parts/clank_{i + 1}.ogg", f"impactMetal_light_00{i}") for i in range(5)], 0.4, pitch=0.06)

    # The engine hoist: chain rattling as it takes the weight, and the block landing
    chains = takes_of("engine/chain", "453383", 3, gap_ms=250, floor_db=-30.0, min_s=1.5, max_s=4.0)
    chains += takes_of("engine/chain_light", "637356", 3, gap_ms=250, floor_db=-30.0, min_s=1.5, max_s=3.0)
    cue("engine.hoist", "effects", chains, 0.5, pitch=0.04)
    cue("engine.land", "effects", [one_shot("engine/land_1.ogg", "640204"), one_shot("engine/land_2.ogg", "693581"),
                                   one_shot("engine/land_3.ogg", "693582")], 0.6, pitch=0.04)

    # The stand's crank turning the engine over
    cranks = takes_of("engine/crank", "588466", 2, min_s=0.3, max_s=2.0) + takes_of("engine/crank_b", "108849", 2, min_s=0.3, max_s=2.0)
    cue("engine.turn", "effects", cranks, 0.5, pitch=0.04)

    # Places
    cue("ambience.garage", "ambience", [ambience("ambience/garage.ogg", "667765", 20, 90)], 0.55, loop=True)
    cue("ambience.warehouse", "ambience", [ambience("ambience/warehouse.ogg", "144043", 5, 90)], 0.5, loop=True)
    cue("ambience.lot", "ambience", [ambience("ambience/lot.ogg", "484190", 0, 58)], 0.4, loop=True)
    cue("ambience.junkyard", "ambience", [ambience("ambience/junkyard.ogg", "667662", 120, 120)], 0.55, loop=True)
    cue("ambience.shop", "ambience", [ambience("ambience/shop.ogg", "244309", 2, 34)], 0.45, loop=True)
    cue("ambience.station", "ambience", [ambience("ambience/station.ogg", "643342", 10, 90)], 0.5, loop=True)
    cue("ambience.diner", "ambience", [ambience("ambience/diner.ogg", "627079", 15, 90)], 0.5, loop=True)
    cue("ambience.street", "ambience", [ambience("ambience/street.ogg", "455834", 0, 26)], 0.35, loop=True)

    # The driveway bell as a car rolls over the hose
    bells = takes_of("station/bell", "465381", 2, gap_ms=300, floor_db=-30.0, min_s=0.3, max_s=3.0)
    cue("station.bell", "effects", bells, 0.5)

    with open(os.path.join(OUT, "sfx.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump({"cues": cues}, fh, indent=2)
        fh.write("\n")
    write_credits()
    print(f"{len(cues)} cues, {len(sources)} files")


def write_credits():
    credits = {}
    with open(os.path.join(CANDIDATES, "credits.csv"), encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            credits[os.path.normcase(os.path.join(CANDIDATES, row["file"]))] = row
    rows = []
    for rel, paths in sorted(sources.items()):
        for path in paths:
            row = credits.get(os.path.normcase(path))
            if row:
                rows.append([rel, row["title"], row["user"], row["license"], row["url"]])
            elif "kenney" in path.lower():
                rows.append([rel, os.path.basename(path), "Kenney (kenney.nl)", "CC0 1.0", "https://kenney.nl/assets/impact-sounds"])
            else:
                raise ValueError("no credit for " + path)
    with open(os.path.join(OUT, "credits.csv"), "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(["file", "title", "author", "license", "source"])
        w.writerows(rows)
    by = sorted({r[3] for r in rows})
    if any(l != "CC0 1.0" for l in by):
        print("NOTE: not all CC0:", by)


if __name__ == "__main__":
    if not os.path.isdir(CANDIDATES):
        sys.exit("No candidates at " + CANDIDATES)
    build()
