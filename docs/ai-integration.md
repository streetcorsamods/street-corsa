# The player's AI

Built in step 25 (2026-10-01). The player may connect their own AI. It is off by default, and the game never waits
on it: the game's own words are always there, and what the AI writes is added on top, in the background, and kept in
the save. (The older, much larger plan this file used to hold is in git history; what is below is what was built.)

## Providers

| Provider | Kind | Default address | Notes |
|---|---|---|---|
| Ollama | text | `http://localhost:11434` | native `/api/chat`; the first chat model it lists when none is set |
| OpenAI-compatible | text | `http://127.0.0.1:1234/v1` | LM Studio's default; also OpenRouter, OpenAI, Ollama's `/v1` |
| Anthropic | text | `https://api.anthropic.com` | Messages API, `x-api-key` |

With no model set, a provider asks the first model it lists that can chat (`AiProviders.CanChat` passes over embedding,
speech and picture models). An address typed with the API's own path on it (`.../v1` for Anthropic, `.../api` or `.../v1`
for Ollama) goes without, so the path is not doubled.
| ComfyUI | images | `http://127.0.0.1:8188` | `POST /prompt`, polls `/history`, fetches `/view` |

Settings (`GameSettings.Ai`, `AiSettings`): on/off, text provider, address, model, the key, ComfyUI on/off and its
address. The key is saved only encrypted with DPAPI for the Windows user (`AiKeys`), never in plain text. The Settings
card's AI section has a Test button for each kind; the ticks are saved at once, the provider with its address, model
and key by Save, so the game never asks one provider at another's address.

## Code

`Services/Ai`:
- `AiHub`: started in `App.xaml.cs` (`AiHub.Start`), `AiHub.Shared` for the screens: `Text`, `Images`, `Writer`.
- `AiTextService` (`IAiText`): the adapters (`AiProviders`), one shared `HttpClient` (`AiHttp`), a queue with a
  concurrency limit, spacing, a cap on what waits, a pause after a failure; prompts filled from the mods' templates.
  The cap drops background work only: the player's own request (`AiPriority.Now`) waits ahead of it, refused only
  when that many of the player's own wait already.
- `ComfyImageService` (`IAiImages`): a mod's workflow JSON with the nodes it names filled (prompt, seed, size, checkpoint).
- `AiWriter`: what the game asks for, and where it goes:
  - rivals' lines: `Services/Talk/AiTalkService` wraps `StaticTalkService`, says a line written ahead at once, else
    waits at most `ai.talkWaitSeconds` and says the game's, keeping the late one for the next time; the Diner
    prepares greetings ahead (`ITalkService.Prepare`);
  - the Diner's street talk and the paper's pieces told again (`StreetTalkAdded`, `NewsAdded`), kept next to the
    originals (`AiText`, `AiHeadline`, `AiBody`, with the language they were written in); screens show them through
    `AiWriter.Said`, `Headline`, `Body`, which give the original in any other language;
  - ads: `AiWriter` is `AdBlurbs.Writer` (`IAdWriter`): an ad's blurb is offered as the ad is made (`AdBlurbs.Describe`;
    a new game's first market and part ads are made with the game, `FirstMarket`, then the daily tasks turn them
    over). The ads wait at a desk of their own in `AiWriter` and go to the AI one at a time as background work, so
    a hundred new ads never fill the shared queue. The answer replaces the ad's `Description` and marks it
    `AiPolished`, never offered again; an open paper keeps the words it was opened with. Ads still in the game's
    words (made while the AI was off) are offered when the paper opens (`AdBlurbs.Polish`);
  - talking to people: see "Talking to people" below (it took the place of step 25's one-off seller's word);
  - portraits for new faces (`NewFace`): painted with ComfyUI, saved under
    `%AppData%\StreetCorsa\AiPortraits\<save>\`, shown instead of the rival's own (`RacersOut.PortraitOf`).
- Logging category `Ai`; every failure falls back quietly.

## Data

- Prompts: the mods' `ai/prompts.json` (`AiPrompts`): a persona and one template per use (`talk`, `street_talk`,
  `news`, `ad`, `portrait_look`, `test`; and the talk's `chat_*`). A game mod gives its own voice by replacing them.
- The ComfyUI workflow: `ai/comfy_portrait.json` (which nodes get filled, the checkpoint as a field).
- Figures: the rules part `ai` (`Rules/AiRules.cs`, `rules/ai.json`): waits, timeouts, queue, chances, how much is kept.
- Words of the Settings section: `text/ui_ai.json`.

See `docs/modding.md`, "ai".

## Talking to people

Built in step 26 (2026-10-01). While the AI is on, the player can talk to the people of the game, typing what they say:
"Call the seller" on a part ad's preview, "Talk to the seller" on the car viewing screen, "Talk" with the rival picked at
the Diner and with the one pulled up at the Cruise curb. A dealer's salesman does not wait: on his lot (and on the car
viewing screen for a dealer's car) he comes over as the player arrives, talks up the lot, and goes on about each car the
player picks, unasked; coming back to a car, he says something new.

The AI only speaks: the game decides every fact and every price.
- **People** (`Services/Talk/TalkPeople`, `GameState.People`): a private seller, a salesman per dealer, a rival (their
  `Opponent` plus a temperament). Made the first time from a seed of who they are, so an ad keeps its seller; a name from
  the `names.people.*` / `names.salesmen.*` pools (a part's seller keeps the ad's name), a temperament from the mods'
  `ai/people.json` (a rival's by their aggression).
- **The fact sheet** (`TalkFacts`): sent with every turn as `{facts}`: today, the player, the thing for sale and its
  price now, the lot's cars, who a rival is, the player's races with the person (`ProcessedRaceSession` by name), a
  rival's races with other rivals (`GameState.RivalRaces`, now recorded by `RaceSimulatorTask`), what the person
  remembers, what a salesman already said about the car, and the answer to an offer. The prompts say to use only these
  facts, to say "don't know" otherwise, and to speak only as the person.
- **The sheet's sections and budget** (`FactSection`, `TalkFacts.Sheet`): the sheet goes in sections under headings
  (`chat.section`, `chat.section.*`: now, who you are, what the talk is about, the lot, races with the player, races
  with other racers, memories, what was said, the deal), what stays the same through a talk first and the deal last;
  the prompts put `{facts}` at their end. A local model then reads again only what changed from the end of the last
  request on (Ollama keeps the start it already read). The sheet keeps to `ai.talk.factTokens` (5000), and to half the
  context window when the game sets one: past it, the least needed lines go first (other racers' races, the rest of the
  lot once a car is picked, old races, old memories, what was said), a list's head line only with the whole section;
  who the person is, the thing and the deal never go. Tokens are the game's guess (`AiTokens`, 3 characters a token:
  measured about 25% over Ollama's own count, never under); every Ollama answer logs its real count against the guess,
  and a warning when a prompt filled the window. The talk's turns are fitted too (`AiTextService.Fit`): when the
  system prompt, the turns and the answer would not fit the window, the oldest turns are left out, from a player's turn
  on.
- **What is on a car** (`TalkFacts.Build`): the car talked about (an ad's, a dealer's, the player's own when a buyer
  calls) and a rival's own car are told part by part from their part trees: a line a kind (the order of
  `part_groups.json`), the same part on every corner once with how many and the worst condition, and for the engine's
  parts whether they came from the factory (`IUsedCarMarketService.FactoryParts`, looked up once a talk) or were put on
  since. A kind of more than `ai.talk.buildPartsPerKind` (3) different parts (an engine's small accessories) is one
  line, but for those not from the factory. A salesman's lot tells each car's engine, miles and condition too, up to
  `lotCarsTold` (30). A salesman going over his lot who names a sum that is no car's on it is asked again once
  (`chat.fact.lot_price_warning`), and the prompts ask for prices in figures, as the facts give them.
- **Haggling** (`Haggle`): a number the player types reads as an offer when it has a currency sign or "k", or an offer
  word (`chat.offer_words`) is in the line, and it is within reason of the price. The rules (`ai.talk`) decide: take it,
  meet halfway, stand firm, or take offence; the floor comes from the price first asked (`PersonDeal.Asked`), times the
  kind's most discount and the temperament's `give`, more for a broke rival or a thing long on sale. A hard floor holds
  over all of it: the ad keeps the price it was first offered at (`FirstAsked` on the listing, the rival's ad, the part
  ad) and nobody goes more than `mostDiscount` (25%) under it, however often and whoever is asked (a forgotten seller
  made again starts from it too). Under that, each thing's seller draws their own limit once (`MostOff` on the ad):
  none at all for `firmShare` (20%) of them, else anything up to 25%, so the bottom is never the same twice. The new price is the
  ad's own (the listing's, the rival's ad's, the part ad's), so every way of buying pays it. The AI is told the answer.
  Words of agreement (`chat.agree_words` anywhere in the line, `chat.agree_alone` as the whole line: "you got a deal",
  "I'll take it", "deal") shake on the price as it stands. Once agreed (an offer taken or a shake), the talk shows
  "Buy it for $X", which runs the screen's own purchase (the car viewing screen's, the lot's, the part preview's).
  An answer that names a lower price than the one standing, that no fact or player line gives, is asked again with a
  word on it (`chat.fact.price_warning`); a second time, the game says the price itself (`chat.deal.held`).
- **The buyer who answered the player's ad** (ideas batch 6): "Call the buyer" on the player's own ad in the paper
  (`Dialogs/PlayerAd`, `TalkDesk.ForBuyer`, `BuyerSubject`). The player sells: the person is the caller (`PersonKind.Buyer`,
  named as they gave it, `chat_buyer`; a rival who called is the rival, `chat_rival`, the fact sheet saying they buy),
  told the car as a seller would be (`TalkFacts.PlayersCar`). A number the player types (with `chat.offer_words` or
  `chat.ask_words`) is a price asked of them, decided by `Haggle.DecideAsk`: no more than they offered and they keep their
  offer; up to their ceiling and they pay it; else halfway up on round figures, or firm; far over the ceiling
  (`insultMargin`) or pushed past `maxCounters` and they walk away (the offer leaves the ad). The ceiling is hard and
  drawn once per offer, the seller's floor turned round: `FirstAmount` and `MostOver` on the `CarOffer` (none for
  `firmShare` of buyers, else up to `mostRaise`, 12%, times the temperament's `give`), never over what the ad asks nor a
  rival's money. A raised offer is the `CarOffer.Amount`, saved; once agreed the talk shows "Sell it for $X", the
  window's own Take. An answer offering more than stands by itself is asked again (`chat.fact.offer_warning`), then held
  (`chat.deal.buyer.held`). The player's own ads get their words as the others do (`AdBlurbs` on `CarSaleAd`, offered to
  the AI the first time the paper shows them; the rewrite shows the next time it opens).
- **Memories**: when a talk with something said by the player ends, `chat_memory` writes one line in the background, kept
  with the game date (`PersonMemory`); the newest few are told next time, forgotten after `forgetDays*` (seller 14,
  salesman 30, rival 90).
- **Chat**: `AiTextRequest.Turns` carries the talk; Ollama and OpenAI-compatible get the system prompt as their first
  message, Anthropic as `system`. `IAiText.ChatAsync` is always the player's priority. Stage lines ("[A customer walks
  onto your lot.]", text `chat.stage.*`) go as the player's turns; two of the player's in a row go as one.
- **UI**: `Controls/TalkPanel` over `Screens/Shared/TalkViewModel` (one request at a time; a car picked on the lot is
  asked about after `pitchDelaySeconds`, only the last one picked), in `Dialogs/Talk` or docked on the lot and car
  viewing screens. The panel shows the price now and what it came down from.

### Salesmen's portraits

Painted off the game, once, and shipped as content: `tools/SalesmanPortraits/paint_salesmen.py` gives every dealer in
the base game's `dealers.json` a salesman (a name from `names.salesmen.*` used once, an age, a sex, a salesman's
temperament from `ai/people.json`, all the same every run for the same dealer), asks Ollama how he looks (if it
answers), paints him with ComfyUI (`comfy_salesman.json` next to the script, the rivals' portrait graph with a car lot's
look; ComfyUI Desktop's address, 8000, by default) into `dealers/salesmen/<id>.png`, and writes him into the dealer's
entry. The game reads him with the dealers (`DealerSalesman`), makes the talk's person him (`TalkPeople.Salesman`), and
shows the portrait at the top of the talk. It never calls ComfyUI for them.

### The game's model in Ollama

No training: what a Modelfile does. Settings' AI section has "Make the game's model" while the provider is Ollama: it
makes `streetcorsa` in Ollama (`POST /api/create`) from `ai/ollama.json`: a base model (`from`, llama3.1:8b; or the
model typed in the box), its own system prompt, parameters (temperature, top_p, repeat_penalty, num_ctx) and a few
example exchanges in the game's voice, then puts its name in the model box to Save. Every Ollama request is also sent
the rules' `ai.ollama`: `num_ctx` (16384, room for a talk and a full fact sheet), `keep_alive` (10m: the next line does
not wait for the model to load, and it is out of the GPU soon after the player goes racing; Ollama's own is 5m) and
`repeat_penalty`. The Settings test sends them too, so testing does not make Ollama load the model again.

Tried live on 2026-10-01 (RTX 3060 12 GB): `streetcorsa` from llama3.1:8b answers in 0-3 s, stays in character, says it
does not remember a race that never was, haggles as the rules say, and writes good memories; it still adds a small
detail now and then (a paint colour). llama3.2:3b mixes the prices up (turns down an offer above the price). Advise 8B
or bigger.

## Known limits

- Small local models make things up (a price, an engine). The prompts forbid it; a bigger model does better.
- Portraits of a deleted save stay in `AiPortraits`.
- An ad whose request failed keeps the game's own words until the paper is opened in a later session.
