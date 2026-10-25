# Writes Fixtures/goldens (roadmap step 38): the few real rated builds EngineGoldenTests holds the engine model to, with
# only their own parts, as data (no models). Copied from the engine mods' data when the tests' copies of the mods went
# (2026-10-22); a figure in a mod fixed later is not followed here on purpose: these are the model's yardstick. Run it
# again only to move the yardstick, and say why.
#
# The builds are in the mods' mod.json ("builds"), each part a .json of its own under the mod's parts folder (flat, beside
# its model); a part is written to goldens/<its id>.json. Run with the folder the engine mods are in (the owner's
# D:/JUEGOS/STREET CORSA/mods/engines when none is given): python -I make_goldens.py [<engine mods folder>]
import json, os, sys, glob, shutil

# Each golden build and the mod it is in
GOLDENS = [("chrysler_v8", "rated/chrysler-la-340-275-hp"), ("chrysler_v8", "rated/chrysler-426-hemi-425-hp"),
           ("ford_six", "rated/ford-221-132-hp")]
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "goldens")

src = sys.argv[1] if len(sys.argv) > 1 else "D:/JUEGOS/STREET CORSA/mods/engines"
mods = sorted({mod for mod, _ in GOLDENS})
parts, mod_builds = {}, {}
for mod in mods:
    folder = os.path.join(src, mod)
    if not os.path.isfile(os.path.join(folder, "mod.json")):
        sys.exit(f"no mod {mod} in {src}: give the folder the engine mods are in")
    with open(os.path.join(folder, "mod.json"), encoding="utf-8") as f:
        mod_builds[mod] = {b["id"]: b for b in json.load(f).get("builds", [])}
    for file in glob.glob(os.path.join(folder, "parts", "**", "*.json"), recursive=True):
        try:
            with open(file, encoding="utf-8") as f:
                d = json.load(f)
        except ValueError:
            continue
        if isinstance(d, dict) and "id" in d: parts[d["id"]] = d

missing = [bid for mod, bid in GOLDENS if bid not in mod_builds[mod]]
if missing: sys.exit(f"builds not in their mods: {missing}")
builds = [mod_builds[mod][bid] for mod, bid in GOLDENS]

# A part the builds take from elsewhere than their mods (a battery, the generic carbs that went with their models,
# 2026-10-22) is kept as the copy already here: the yardstick does not move
kept = {}
for b in builds:
    for pid in b["parts"]:
        here = os.path.join(OUT, *pid.split("/")) + ".json"
        if pid not in parts and os.path.isfile(here):
            with open(here, encoding="utf-8") as f:
                kept[pid] = json.load(f)
missing = [pid for b in builds for pid in b["parts"] if pid not in parts and pid not in kept]
if missing: sys.exit(f"parts in neither the mods nor the goldens: {sorted(set(missing))}")
parts.update(kept)

shutil.rmtree(OUT, ignore_errors=True)
copied = set()
for b in builds:
    for pid in b["parts"]:
        if pid in copied: continue
        d = dict(parts[pid])
        d.pop("model", None)
        path = os.path.join(OUT, *pid.split("/")) + ".json"
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as out:
            json.dump(d, out, indent=2)
            out.write("\n")
        copied.add(pid)
with open(os.path.join(OUT, "engine_builds.json"), "w", encoding="utf-8", newline="\n") as out:
    json.dump(builds, out, indent=2)
    out.write("\n")
print(len(copied), "parts,", len(builds), "builds")
