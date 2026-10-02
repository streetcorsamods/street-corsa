"""
Paints the base game's dealers' salesmen with ComfyUI, off the game, and writes them into the dealers' data.

For every dealer in Content/streetcorsa/dealers/dealers.json without a salesman (or every one, with --force), it:
  - gives him who he is, the same every run for the same dealer: a name from the text pools names.salesmen.men/women
    (none used twice), an age, a sex, a temperament for salesmen from ai/people.json (by weight);
  - asks Ollama, when it answers, how he looks (one line of visual details, from his temperament); else a plain line;
  - paints his portrait with ComfyUI (comfy_salesman.json next to this script: the base game's portrait graph with a
    car lot's look) and saves it as Content/streetcorsa/dealers/salesmen/<dealer id>.png (512 x 512, as the racers');
  - writes "salesman": { name, age, sex, temperament, portrait } into the dealer's entry.

The game then shows the portrait at the top of the talk with him and never calls ComfyUI for it. A salesman already in
the data keeps his name, age, sex and temperament with --force; only his picture is painted again.

    python tools/SalesmanPortraits/paint_salesmen.py [--comfy http://127.0.0.1:8000] [--ollama http://localhost:11434]
        [--model streetcorsa] [--only downtown_motors,sunset_motors] [--force] [--checkpoint name.safetensors]

Needs ComfyUI running with the checkpoint the graph names (juggernautXL_ragnarokBy.safetensors) or --checkpoint.
"""
import argparse
import json
import random
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CONTENT = ROOT / "Street Corsa" / "Content" / "streetcorsa"
DEALERS = CONTENT / "dealers" / "dealers.json"
PORTRAITS = CONTENT / "dealers" / "salesmen"
NAMES = CONTENT / "text" / "names.json"
PEOPLE = CONTENT / "ai" / "people.json"
WORKFLOW = HERE / "comfy_salesman.json"

WOMEN_SHARE = 0.15
AGES = (28, 57)


def read_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def write_json(path, data):
    raw = path.read_bytes()
    newline = "\r\n" if b"\r\n" in raw else "\n"
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    path.write_bytes(text.replace("\n", newline).encode("utf-8"))


def http_json(url, body=None, timeout=60):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def draw_temperament(temperaments, rng):
    fitting = [(key, t) for key, t in temperaments.items()
               if not key.startswith("_") and (not t.get("for") or "salesman" in t["for"]) and t.get("weight", 1) > 0]
    roll = rng.random() * sum(t.get("weight", 1) for _, t in fitting)
    for key, t in fitting:
        roll -= t.get("weight", 1)
        if roll < 0:
            return key
    return fitting[-1][0]


def who(dealer, pools, temperaments, taken):
    """The salesman of a dealer: the same every run (seeded by the dealer's id), a name nobody else has"""
    rng = random.Random(dealer["id"])
    sex = "woman" if rng.random() < WOMEN_SHARE else "man"
    pool = pools["names.salesmen.women" if sex == "woman" else "names.salesmen.men"]
    free = [n for n in pool if n not in taken]
    if not free:
        raise SystemExit(f"names.salesmen.{'women' if sex == 'woman' else 'men'} has no name left for {dealer['id']}: add some")
    name = free[rng.randrange(len(free))]
    return {
        "name": name,
        "age": rng.randint(*AGES),
        "sex": sex,
        "temperament": draw_temperament(temperaments, rng),
    }


def role(salesman):
    return "saleswoman" if salesman["sex"] == "woman" else "salesman"


def look_of(salesman, words, dealer, ollama, model):
    plain = f"{words.split(':')[0].split('.')[0].lower()}, sport coat and tie"
    if not ollama:
        return plain
    prompt = (f"Describe for a photographer, in English, how {salesman['name']} looks: a {salesman['age']}-year-old "
              f"{salesman['sex']}, a used car {role(salesman)} at {dealer['name']}, a small-town lot in the early 1960s. {words}\n"
              "One line of comma-separated visual details: face, hair, expression, clothes (sport coat, tie, hat), pose. "
              "No names, no sentences.")
    try:
        answer = http_json(f"{ollama.rstrip('/')}/api/chat", {
            "model": model, "stream": False,
            "messages": [{"role": "user", "content": prompt}],
            "options": {"temperature": 1.0, "num_predict": 90}
        }, timeout=180)
        line = answer["message"]["content"].strip().splitlines()[0].strip().strip('"')
        return line or plain
    except Exception as ex:  # Ollama is a help, not a need
        print(f"  (no look from Ollama: {ex}; a plain one)")
        return plain


def fill(text, values):
    for key, value in values.items():
        text = text.replace("{" + key + "}", str(value))
    return text


def paint(comfy, workflow, values, checkpoint, prefix):
    graph = json.loads(json.dumps(workflow["workflow"]))
    put = workflow["fill"]

    def set_input(what, value):
        if what in put:
            graph[put[what]["node"]]["inputs"][put[what]["input"]] = value

    set_input("positive", fill(workflow["prompt"], values))
    set_input("negative", fill(workflow.get("negative", ""), values))
    set_input("seed", random.randrange(2 ** 48))
    set_input("width", workflow.get("width", 512))
    set_input("height", workflow.get("height", 512))
    if checkpoint or workflow.get("checkpoint"):
        set_input("checkpoint", checkpoint or workflow["checkpoint"])
    set_input("prefix", prefix)

    queued = http_json(f"{comfy}/prompt", {"prompt": graph})
    prompt_id = queued["prompt_id"]
    for _ in range(600):
        time.sleep(1)
        history = http_json(f"{comfy}/history/{prompt_id}")
        if prompt_id not in history:
            continue
        for output in history[prompt_id].get("outputs", {}).values():
            for image in output.get("images", []):
                query = urllib.parse.urlencode({"filename": image["filename"], "subfolder": image.get("subfolder", ""),
                                                "type": image.get("type", "output")})
                with urllib.request.urlopen(f"{comfy}/view?{query}", timeout=60) as response:
                    return response.read()
        raise RuntimeError(f"ComfyUI finished {prompt_id} with no picture: {history[prompt_id].get('status')}")
    raise RuntimeError("ComfyUI took longer than ten minutes")


def main():
    parser = argparse.ArgumentParser(description="Paint the dealers' salesmen with ComfyUI, into the base game's data")
    parser.add_argument("--comfy", default="http://127.0.0.1:8000", help="ComfyUI's address (Desktop: 8000, portable: 8188)")
    parser.add_argument("--ollama", default="http://localhost:11434", help="Ollama's address, for how they look; '' for none")
    parser.add_argument("--model", default="streetcorsa", help="the Ollama model asked")
    parser.add_argument("--only", default="", help="dealer ids, comma separated")
    parser.add_argument("--force", action="store_true", help="paint again those who have a portrait")
    parser.add_argument("--checkpoint", default="", help="the ComfyUI checkpoint, when not the graph's")
    args = parser.parse_args()
    comfy = args.comfy.rstrip("/")

    data = read_json(DEALERS)
    pools = read_json(NAMES)["lines"]
    temperaments = read_json(PEOPLE)["temperaments"]
    workflow = read_json(WORKFLOW)
    only = {d.strip() for d in args.only.split(",") if d.strip()}
    PORTRAITS.mkdir(parents=True, exist_ok=True)

    taken = {d["salesman"]["name"] for d in data["dealers"] if d.get("salesman", {}).get("name")}
    for dealer in data["dealers"]:
        if only and dealer["id"] not in only:
            continue
        salesman = dealer.get("salesman")
        file = PORTRAITS / f"{dealer['id']}.png"
        if salesman and file.exists() and not args.force:
            print(f"{dealer['id']}: {salesman['name']} has his portrait")
            continue

        if not salesman:
            salesman = who(dealer, pools, temperaments, taken)
            taken.add(salesman["name"])
        words = temperaments.get(salesman.get("temperament", ""), {}).get("words", "")
        look = look_of(salesman, words, dealer, args.ollama, args.model)
        print(f"{dealer['id']}: {salesman['name']}, {salesman['age']}, {salesman['temperament']}: {look}")

        values = {"name": salesman["name"], "age": salesman["age"], "sex": salesman["sex"], "look": look, "role": role(salesman),
                  "temperament": words, "dealer": dealer["name"]}
        try:
            picture = paint(comfy, workflow, values, args.checkpoint, f"streetcorsa_salesman_{dealer['id']}")
        except Exception as ex:
            print(f"  could not paint: {ex}")
            continue

        file.write_bytes(picture)
        salesman["portrait"] = f"salesmen/{dealer['id']}.png"
        dealer["salesman"] = salesman
        write_json(DEALERS, data)  # after every one: a run stopped halfway keeps what it painted
        print(f"  -> {file.relative_to(ROOT)}")


if __name__ == "__main__":
    sys.exit(main())
