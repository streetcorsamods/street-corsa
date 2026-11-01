"""
Builds the core's sound effects (Content/streetcorsa/sfx: the menus' clicks) out of the downloaded candidates
(Street Corsa/Assets/Sound/Candidates, not in the repo: freesound HQ previews, every one CC0 or
credited). Each cue is cut, trimmed and levelled here, so the game only plays what is in sfx.json:

  one-shots  trimmed to where they sound, faded at the ends, mono, levelled by how loud they are while they sound,
             never past -1 dBFS

The mix between cues is sfx.json's "volume", not the files'. Needs numpy and soundfile (pip install -r
tools/requirements.txt).

A build replaces only its own cues in sfx.json and their rows in credits.csv, and keeps any other cue there and the
rows of the files the folder's other cue files play (music.json's theme). A place's sounds (its air, its tools) are
its mod's, not the core's.

  python tools/sfx/build_sfx.py            builds into Content/streetcorsa/sfx
"""
import csv
import io
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


def decoded(path):
    try:
        return sf.read(path, always_2d=True, dtype="float32")[0]
    except Exception:
        return None


def write(rel, x, rate):
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    temp = path + ".new"
    # In blocks: libsndfile's Vorbis encoder crashes the process on a long file written in one go
    with sf.SoundFile(temp, "w", rate, x.shape[1], format="OGG", subtype="VORBIS") as fh:
        for i in range(0, len(x), 8192):
            fh.write(np.ascontiguousarray(x[i:i + 8192]))
    # Vorbis files differ byte for byte on every encode (a random stream serial): a file that already decodes to the
    # same sound is left alone, so a rebuild only changes what it changes
    old, new = decoded(path), decoded(temp)
    if old is not None and new is not None and old.shape == new.shape and np.array_equal(old, new):
        os.remove(temp)
    else:
        os.replace(temp, path)
    return rel.replace("\\", "/")


# ---- the cues ---------------------------------------------------------------------------------------------------

sources = {}  # written file -> candidate files it was made of


def one_shot(rel, candidate, gain_db=0.0):
    path = find(candidate)
    x, rate = load(path)
    y = level(trim(x, rate), rate) * 10 ** (gain_db / 20)
    sources[write(rel, y, rate)] = [path]
    return rel.replace("\\", "/")


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

    kept = merge_cues(cues)
    write_credits(kept)
    print(f"{len(cues)} cues, {len(sources)} files; kept as they were: {', '.join(kept) or 'none'}")


def merge_cues(cues):
    """
    The cues built here into sfx.json, each in its old place (a new one at the end), every other cue there kept as it
    is; the cues kept, by name
    """
    path = os.path.join(OUT, "sfx.json")
    merged = {}
    if os.path.isfile(path):
        with open(path, encoding="utf-8") as fh:
            merged = json.load(fh).get("cues", {})
    kept = {name: entry for name, entry in merged.items() if name not in cues}
    merged.update(cues)
    replace_with(path, json.dumps({"cues": merged}, indent=2) + "\n")
    return kept


def replace_with(path, text):
    """A file written whole or not at all: a temp file next to it, renamed over it"""
    temp = path + ".tmp"
    with open(temp, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    os.replace(temp, path)


def write_credits(kept):
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
            else:
                raise ValueError("no credit for " + path)

    # The rows of the files the kept cues play, as they were, and of the files the other cue files (music.json) play
    out = os.path.join(OUT, "credits.csv")
    theirs = {f for entry in kept.values() for f in entry.get("files", [])}
    for name in sorted(os.listdir(OUT)):
        if name.endswith(".json") and name != "sfx.json":
            with open(os.path.join(OUT, name), encoding="utf-8") as fh:
                theirs |= {f for entry in json.load(fh).get("cues", {}).values() for f in (entry or {}).get("files", [])}
    theirs -= set(sources)
    if os.path.isfile(out):
        with open(out, encoding="utf-8", newline="") as fh:
            for row in csv.DictReader(fh):
                if row["file"] in theirs:
                    rows.append([row["file"], row["title"], row["author"], row["license"], row["source"]])
    rows.sort(key=lambda r: r[0])

    text = io.StringIO()
    w = csv.writer(text, lineterminator="\n")
    w.writerow(["file", "title", "author", "license", "source"])
    w.writerows(rows)
    replace_with(out, text.getvalue())
    by = sorted({r[3] for r in rows})
    if any(l not in ("CC0 1.0", "the game's own") for l in by):
        print("NOTE: not all CC0:", by)


if __name__ == "__main__":
    if not os.path.isdir(CANDIDATES):
        sys.exit("No candidates at " + CANDIDATES)
    build()
