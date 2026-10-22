# Writes Fixtures/goldens (roadmap step 38): the few real rated builds EngineGoldenTests holds the engine model to, with
# only their own parts, as data (no models). Copied once from the engine mods' data when the tests' copies of the mods
# went (2026-10-22); a figure in a mod fixed later is not followed here on purpose: these are the model's yardstick.
# Run with the source folder of the mods' parts data: python -I make_goldens.py <parts folder with engine_builds.json>
import json, os, sys, glob, shutil

GOLDENS = ["rated/chrysler-la-340-275-hp", "rated/chrysler-426-hemi-425-hp", "rated/ford-221-132-hp"]
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "goldens")

src = sys.argv[1]
parts = {}
for f in glob.glob(os.path.join(src, "**", "*.json"), recursive=True):
    try:
        d = json.load(open(f, encoding="utf-8"))
    except ValueError:
        continue
    if isinstance(d, dict) and "id" in d: parts[d["id"]] = (f, d)

builds = [b for b in json.load(open(os.path.join(src, "engine_builds.json"), encoding="utf-8")) if b["id"] in GOLDENS]
assert len(builds) == len(GOLDENS), [b["id"] for b in builds]
shutil.rmtree(OUT, ignore_errors=True)
copied = set()
for b in builds:
    for pid in b["parts"]:
        if pid not in parts or pid in copied: continue   # the base game's own (generic carbs, the battery) stay the base game's
        f, d = parts[pid]
        d.pop("model", None)
        rel = os.path.relpath(f, src)
        os.makedirs(os.path.join(OUT, os.path.dirname(rel)), exist_ok=True)
        with open(os.path.join(OUT, rel), "w", encoding="utf-8", newline="\n") as out:
            json.dump(d, out, indent=2)
            out.write("\n")
        copied.add(pid)
with open(os.path.join(OUT, "engine_builds.json"), "w", encoding="utf-8", newline="\n") as out:
    json.dump(builds, out, indent=2)
    out.write("\n")
print(len(copied), "parts,", len(builds), "builds")
