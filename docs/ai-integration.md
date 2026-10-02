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
  - ads: `AiWriter` is `AdBlurbs.Writer` (`IAdWriter`): an ad's blurb is offered when the paper first shows it
    (`AdBlurbs.Polish`, the section on show only), not when the ad is made, since most ads are never read; it comes
    back livelier, replaces the ad's `Description` and marks it `AiPolished`, so it is not offered again;
  - a seller's word on a car (`DealerTakeAsync`): the car viewing screen's "What do you make of it?" button, shown
    only while the AI is on, kept per listing in `GameState.AiWords`;
  - portraits for new faces (`NewFace`): painted with ComfyUI, saved under
    `%AppData%\StreetCorsa\AiPortraits\<save>\`, shown instead of the rival's own (`RacersOut.PortraitOf`).
- Logging category `Ai`; every failure falls back quietly.

## Data

- Prompts: the mods' `ai/prompts.json` (`AiPrompts`): a persona and one template per use (`talk`, `street_talk`,
  `news`, `dealer_take`, `ad`, `portrait_look`, `test`). A game mod gives its own voice by replacing them.
- The ComfyUI workflow: `ai/comfy_portrait.json` (which nodes get filled, the checkpoint as a field).
- Figures: the rules part `ai` (`Rules/AiRules.cs`, `rules/ai.json`): waits, timeouts, queue, chances, how much is kept.
- Words of the Settings section: `text/ui_ai.json`.

See `docs/modding.md`, "ai".

## Known limits

- Small local models make things up (a price, an engine). The prompts forbid it; a bigger model does better.
- Portraits of a deleted save stay in `AiPortraits`.
- Ads past the queue's cap keep the game's own words, and are not offered again that session.
