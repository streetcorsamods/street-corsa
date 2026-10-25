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

The key belongs to the provider and address it was typed for (`AiSettings.KeyProvider`, `KeyEndpoint`), and
`AiKeys.KeyFor` gives it only while both are the ones asked: switching from Anthropic or OpenRouter to another provider
or address does not take the key along (type it again there). Ollama is never sent a key (Settings has no box for one).
A key never goes over plain `http://` to another machine (`AiProviders.KeySafeAt`): Save refuses one, and a provider
made for such an address is made without it. A settings.json from before keys were tied loads as it was: its key is
the saved provider's and address's (`AiKeys.Tie`, done at the next Save), never Ollama's. Test tries a key typed in
the box, else the saved one only when it matches.

### What is sent

With a provider that is not on this PC (any address that is not localhost, 127.x or ::1), everything a request holds
leaves the machine, and the Settings section says so under the address (`settings.ai.sent_away`): the persona and the
prompts, and with them the game's facts of the career (the player's name, their cars and parts, money and prices,
races and results, the people met and what they remember of the player, the town and its market), the street talk and
the paper's pieces told again, and in a talk every line the player types. The Talk detail setting sets how much of the
fact sheet goes with each line. The key goes in the provider's own header. ComfyUI is sent the portrait's prompt (a
new rival's looks, name, age). Nothing else (no files, no save) is sent.

## Code

`Services/Ai`:
- `AiHub`: started in `App.xaml.cs` (`AiHub.Start`), `AiHub.Shared` for the screens: `Text`, `Images`, `Writer`.
- `AiTextService` (`IAiText`): the adapters (`AiProviders`), one shared `HttpClient` (`AiHttp`), a queue with a
  concurrency limit, spacing, a cap on what waits, a pause after a failure; prompts filled from the mods' templates.
  The cap drops background work only: the player's own request (`AiPriority.Now`) waits ahead of it, refused only
  when that many of the player's own wait already.
- `ComfyImageService` (`IAiImages`): a mod's workflow JSON with the nodes it names filled (prompt, seed, size, checkpoint).
  A graph may use only ComfyUI's own picture nodes (`ComfyWorkflow.CoreNodes`); one that names any other node (a custom
  node can run scripts or write files) is left out with a problem line, and every save node saves under the game's
  own name (`ComfyImageService.Prefix`). A picture is taken only when it is a PNG or a JPEG.
- Answers are read up to a size (`AiHttp.ReadBytesAsync`): 4 MB of JSON, 16 MB for a picture; more is no answer.
- `AiBackground`: the work nobody waits on (a portrait, a memory, a summary, a line written ahead), tracked
  (`WhenIdle`) and logged when it fails; `AiBackoff`: the rest after a provider could not be reached.
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
    words (made while the AI was off) are offered when the paper opens (`AdBlurbs.Polish`). The desk takes
    `ai.adDeskMost` (24) at a time; the rest wait with `AdBlurbs` (their facts not even worked out) and are handed
    over in their order as the desk empties (`IAdWriter.RoomAgain`, `AdBlurbs.OfferWaiting`), the player's own ad
    first; an ad still at the desk when the AI is turned off is let go unwritten (its rewrite told null) and waits again;
  - talking to people: see "Talking to people" below (it took the place of step 25's one-off seller's word);
  - portraits for new faces (`NewFace`): painted with ComfyUI, saved under
    `%AppData%\StreetCorsa\AiPortraits\<save>\`, shown instead of the rival's own (`RacersOut.PortraitOf`).
- Logging category `Ai`; every failure falls back quietly.

## Data

- Prompts: the mods' `ai/prompts.json` (`AiPrompts`): a persona and one template per use (`talk`, `street_talk`,
  `news`, `ad`, `portrait_look`, `test`; and the talk's `chat_*`). The core has a plain persona and `test` only: a
  setting gives its voice, the persona and every other prompt, and a use with no prompt is not asked.
- The ComfyUI workflow: `ai/comfy_portrait.json` (which nodes get filled, the checkpoint as a field).
- Figures: the rules part `ai` (`Rules/AiRules.cs`, `rules/ai.json`): waits, timeouts, queue, chances, how much is kept.
- Words of the Settings section: `text/ui_ai.json`.

See `docs/modding.md`, "ai".

## Talking to people

**The part shop's clerk** (step 28, idea 7): behind every part shop's counter while the AI is on
(`TalkDesk.ForShop`, `PersonKind.Clerk`, `TalkPlace.Shop`, prompt `chat_clerk`). The game does the search and the AI
only voices it: each line the player types is first read by `clerk_search` as a JSON search (`IAiText.JsonAsync`,
`ClerkSearch.Parse`: a kind the shop lists, fits my car, a price limit, a name, power or cheap; a kind or a price read
makes it an ask whatever the model's `asks` says, and the `chat.fit_words` make it one for what fits). The screen
(`IShopCounter`, the part shop's view model) applies it to the real stock, so the list's filters change where the player
sees them, picks the best five (power: what each part does to the player's engine on the dyno, `EngineTuner.PowerWith`;
cheap: the price; else fit, gain, price) and marks them first in the list as the clerk's picks; the clerk is told them
(`chat.fact.stock.*`, a plain line when none of them fits) and recommends among them. Small talk searches nothing.
Clerks never haggle (a junkyard keeps its Haggle button); what the player buys over the counter is an event they
remember. Measured on `streetcorsa` (llama3.1:8b, 2026-10-04): the search and the answer each about 1-2 s; the 8B often
says `asks: false` while filling in a kind, hence the rule above.

**The machine shop's machinist**: the same desk (`TalkDesk.ForMachineShop`, a `Clerk` at
`TalkPlace.MachineShop`, prompts `chat_machinist` and `machinist_search`). The search's kinds are the kinds of part the
player has to bring; the screen (`MachineShopScreenViewModel`) matches it against every machine job on the player's
parts at the shop's prices, runs the jobs on a whole engine through the dyno (power: the most gained first, then speed
work), marks the best five in the list, and the machinist is told them (`chat.fact.work.*`: the job, the part, the
price, the days, what the engine gains) with what the player has at the shop and could bring.

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
  (`chat.fact.lot_price_warning`), and the prompts ask for prices in figures, as the facts give them. A warning goes
  last in the sheet under a heading of its own (`chat.section.warning`).
- **The town and the market**: everybody hears the diner's street talk (`ai.talk.streetTalkTold`, 6) and reads the
  paper (`newsTold`, 3), the newest first, in the AI's words when it rewrote them in the player's language. The player
  is told in words (`RacerStats.GetReputationTier`, a rival's own standing too), with the car they drive (the one picked
  in the garage); a rival also sizes it up part by part. A seller or buyer knows the other cars for sale in town
  (`marketCarsTold`, 4: the same model first, then the nearest in price), told they are theirs and never this one's
  price; the price guard does not count those sums as known, so a seller who names one as their own price is caught.
- **What everybody knows** (`ai/lore.json`, `AiPrompts.Lore`, `TalkFacts.Lore`): topics of the game's time and place
  (the town, street words, speed parts, racing, selling, new cars, what things cost, the radio, the news), each for some
  kinds of person and some years of the game's calendar. A person is told `ai.talk.loreTold` (8) lines of the topics
  they know, a line of each in turn, drawn the same all day (seeded by who they are and the date); the section is the
  first left out when the sheet is too long, and its figures are not sums the price guard trusts. The prompts let people
  color in small talk of the time and place (the weather, the radio, their work and family, what things cost) but never
  a car's facts, a price, a race or a person the facts do not give. Measured with the 8B: it uses the lore well (the
  hits on the radio, the price of gas, the strip's new class), and still embellishes a car now and then (a radio, the
  paint), as it did before the prompts were softened.
- **Example exchanges** (`examples` on a chat prompt in `prompts.json`): a few lines in each kind's voice, written into
  the prompt as `{examples}` under `chat.examples` (voice only, nothing in them happened), so every provider gets them;
  they carry no figures (a model copies them) and show the person not knowing what the facts do not say. The game's
  Ollama model's own examples (`ai/ollama.json`) are now in no one kind's voice; "Make the game's model" again in
  Settings takes them in.
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
  word on it (`chat.fact.price_warning`); a second time, the game says the price itself (`chat.deal.held`). The sums
  of the market, the town's talk, the paper, past events, the lore and the talk's summary are no price the person may
  come down to. Small talk is no price: a sum under `offerLowest` (20%) of the price (on the lot, of the cheapest car),
  or with a `chat.unit_words` word after it ("$300 down", "a gallon", "a month").
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
- **Memories**: when a talk with something said by the player ends, `chat_memory` writes a note of two or three
  sentences in the background (what the player wanted, the prices named, how it went, what the person thinks of them),
  kept with the game date (`PersonMemory`); the newest `memoriesGiven` (10) are told next time, forgotten after
  `forgetDays*` (seller 14, salesman 30, rival 90).
- **A long talk**: the newest `turnsKept` (40) turns are sent (and fitted to the window); the older ones not summed up
  yet are still sent, up to twice `turnsKept`. When they are near that (`turnsKept` less `summaryTurns` of them, at
  least `summaryTurns`), `chat_summary` sums them up with the summary before them, in the background, ending after one
  of the person's turns, and the summary goes with the facts (section `earlier`, near the end as it changes). Late and
  in one go: the summary takes the AI's one place, and its prompt makes a local model read the talk's whole prompt again.
- **What happened, and feelings** (`Person.Events`, `TalkPeople.Happened`): the game keeps, whatever the AI says, what
  happened between a person and the player: a car or part bought from them (`CarPurchaseService`,
  `NewspaperPartsService.BuyUsed`), a car sold to them (`CarSaleService.AcceptOfferAsync`), a talk's insulting offer or a
  buyer pushed until they walked, its last offer that came to nothing, and a rival's races with the player and pink
  slips (`RaceResultProcessor`). Private sellers and buyers keep it only when the player has talked to them; rivals
  always. It is told as a section (`events`, `chat.fact.event.*`, the newest first, `eventsKept` 12) and is forgotten as
  the memories are. How the person feels about the player (-2 sore .. +2 friendly) is what they still remember, added up
  by the rules' `feeling*` weights (insulted -1, bought +1, sold +1, beaten -1, beat the player 0, pink slip lost -2,
  won +1), told in words (`chat.fact.feeling.*`); it changes only how they talk, never a price.
- **Chat**: `AiTextRequest.Turns` carries the talk; Ollama and OpenAI-compatible get the system prompt as their first
  message, Anthropic as `system`. `IAiText.ChatAsync` is always the player's priority. Stage lines ("[A customer walks
  onto your lot.]", text `chat.stage.*`) go as the player's turns; two of the player's in a row go as one.
- **UI**: `Controls/TalkPanel` over `Screens/Shared/TalkViewModel` (one request at a time; a car picked on the lot is
  asked about after `pitchDelaySeconds`, only the last one picked), in `Dialogs/Talk` or docked on the lot and car
  viewing screens. The panel shows the price now and what it came down from.

### Salesmen's portraits

A setting may paint them off the game, once, and ship them as content: a dealer's entry in its `dealers.json` names
its salesman (a name, an age, a sex, a temperament from `ai/people.json`) and his portrait (`dealers/salesmen/<id>.png`,
say, painted with ComfyUI). The game reads him with the dealers (`DealerSalesman`), makes the talk's person him (`TalkPeople.Salesman`), and
shows the portrait at the top of the talk. It never calls ComfyUI for them.

### The game's model in Ollama

No training: what a Modelfile does. Settings' AI section has "Make the game's model" while the provider is Ollama: it
makes `streetcorsa` in Ollama (`POST /api/create`) from `ai/ollama.json`: a base model (`from`, llama3.1:8b; or the
model typed in the box), parameters (temperature, top_p, repeat_penalty, num_ctx) and, from the setting in play, its
own system prompt and a few example exchanges in the setting's voice, then puts its name in the model box to Save. It asks Ollama's list
(`/api/tags`) first: a base model not pulled yet would be downloaded by the create, gigabytes long past the wait, so
the player is told to `ollama pull` it instead. Every Ollama request is also sent
the rules' `ai.ollama`: `num_ctx` (16384, room for a talk and a full fact sheet), `keep_alive` (10m: the next line does
not wait for the model to load, and it is out of the GPU soon after the player goes racing; Ollama's own is 5m) and
`repeat_penalty`. The Settings test sends them too, so testing does not make Ollama load the model again.
Races (`ai.ollama.unloadForRaces`, on): when a race starts (`AssettoCorsaLauncher.ExecutionStarted`, `AiHub.SetRacing`)
the model lets go of the GPU (an empty `/api/chat` with `keep_alive` 0) if it is on it (`/api/ps`). Nothing is asked of
it while the race runs (a request then would load it again): the player's own requests get the game's words, the
background's (the race's own news and street talk, written before `ExecutionEnded`) wait for the end of the race, and
one already in line when the race starts is held at its turn. When the race is over it is loaded again in the
background (an empty chat with the same `num_ctx` and `keep_alive`, so the first line does not load it once more), only
if it was on the GPU when the race began. Measured on 2026-10-03: unloaded at once, loaded again in 2.2 s. Other providers are not touched.

Shows (`ai.holdForShows`, on): a car on the move in a scene (the drive into the garage, up to the pump, into the diner's
lot, the booth, a rival up the street's lane, the flow's scripts: `D3DViewportBase.ShowsChanged`, `AiHub.SetShowing`,
`AiShows`) is drawn on the card an AI on this machine works on, and the drive lost frames to the writing (2026-10-19:
the paper's ads, a hundred or two in a new game, written one after the other right across every drive-in; with the AI
off the drive was smooth). While a show plays nothing of the background's is asked of an AI at a loopback address
(Ollama, LM Studio at the OpenAI-compatible provider's usual address, a ComfyUI on this machine): a request (the ads,
street talk, news) waits for the show's end, one already in line is held at its turn, a portrait waits before it is
painted, and the model's load after a race waits too; the player's own requests go on, and the model stays loaded. A
provider over the network is never held. A show lasts from a script's start until the script is over, in every
viewport (`D3DViewportBase.ShowWhile`, which the onboard viewports' scripts go through too); a car arriving as a
screen loads is held from the screen's start (`D3DViewportBase.HoldShow`: the garage with a car arriving, a drive-in
viewport loading with the car waiting to arrive), so the desk never slips in between the lot's drive-off and the
garage's arrival. A frame that comes late while a car moves ("Slow frame on the move" in the log: 30 ms between frames
and over one and a half of the display's own frame, or 20 ms drawing; twelve a show said one by one, the rest counted
at its end) says when an AI on this machine was writing or painting then (`D3DViewportBase.SlowFrameNotes`,
`AiHub.IsWriting`, `AiHub.IsPainting`).

Talk detail (Settings' AI section, `AiSettings.TalkDetail`, `TalkDetail`): how much a talk sends with each line, for a paid
provider billed by the token or a slow one. Light (a fact sheet of at most 1,500 tokens, 12 turns, 4 memories told),
normal (3,000, 24, 6) or full (the rules as they are, the default); the caps are the rules' `detailLight` and
`detailNormal`. It caps only what is told (`TalkDesk.Told`):
what is kept (memories, deals, events) always goes by the rules, so a lighter detail forgets nothing. The section shows
the most a line sends at the detail picked, by the game's guess (`TalkDetail.MostTokensALine`: the persona, the longest
talk prompt with its examples, a full sheet and twice the turns kept at `turnTokens` (45) each; most lines send far less). The answer's length stays the
prompts' own.

Tried live on 2026-10-01 (RTX 3060 12 GB): `streetcorsa` from llama3.1:8b answers in 0-3 s, stays in character, says it
does not remember a race that never was, haggles as the rules say, and writes good memories; it still adds a small
detail now and then (a paint colour). llama3.2:3b mixes the prices up (turns down an offer above the price). Advise 8B
or bigger.

## Known limits

- Small local models make things up (a price, an engine). The prompts forbid it; a bigger model does better.
- Portraits of a deleted save stay in `AiPortraits`.
- An ad whose request failed keeps the game's own words until the paper is opened in a later session.
