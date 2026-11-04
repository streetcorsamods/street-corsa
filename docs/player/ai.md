---
title: People who talk back
description: Turn on your own AI and talk to the sellers, salesmen, rivals and clerks. What it does and how to set it up.
weight: 2
---

# People who talk back

Street Corsa can use **your own AI** to give the town a voice. You type what you say, and the person answers in their
own words. It's optional and off by default. Without it the game plays the same, only nobody talks back: the junkyard's
**Haggle** and the buyers who answer your ad still bargain, by the game's own rules.

## What it does

- **Sellers.** Go to see a car from the paper and the seller comes out to the curb. On a part ad, **Call the seller**.
  Ask about it, then haggle.
- **Salesmen.** On a dealer's lot the salesman comes over as you arrive, talks up the lot and tells you about each car
  you pick.
- **Rivals.** **Talk** to a rival at the Diner or at the Cruise curb. They know your car part by part, their own, and
  every race you've had.
- **Buyers.** When someone answers your ad, **Call the buyer** and talk them up. Push too hard and they walk.
- **Clerks and machinists.** Ask the parts shop for "something cheap that fits my car" and the clerk finds it on the
  shelves. Ask the machinist which work gains the most.
- **The town's words.** The ads in the paper, its stories and the Diner's street talk get rewritten in the
  setting's voice.
- **Portraits.** With [ComfyUI](https://www.comfy.org), tick **Paint new rivals' portraits with ComfyUI** and each new
  rival gets a picture of their own.

People **remember you**: what you talked about, the prices you named, what you bought from them and sold them, the
races you won and lost, a pink slip. Insult a seller and they'll be sore next time. Beat a rival and they'll bring it up.

### The game stays in charge

The AI only speaks. **The game decides every fact and every price.** When you make an offer, the game's rules answer it:
take it, meet you halfway, stand firm or take offence. Every seller has their own limit, and some won't come down at all.
Once you agree, the talk shows **Buy it for $X**.

The game never waits on the AI. The paper, its stories, the street talk and the rivals' lines have the game's own words
to fall back on. A talk doesn't: when the AI is slow you get **No answer. Try again.**, and a seller at a viewing who never
answers leaves you with the car's card and its Buy, as with the AI off.

With Ollama, the game unloads the model when a race starts, so Assetto Corsa gets the graphics card, and loads it again
after. Other providers keep theirs loaded.

## Set it up with Ollama (free, on your PC)

1. Install [Ollama](https://ollama.com).
2. Pull a model. In a terminal: `ollama pull llama3.1:8b`
3. In the game: **Settings → AI**, tick **Use my AI for what people say and write**, pick **Ollama**.
4. Press **Make the game's model**. It builds the game's own model from the one you pulled, tuned to talk like its town.
5. **Save**, then **Test**.

Works best on a graphics card with **12 GB**. On an RTX 3060 12 GB, answers take 0 to 3 seconds.
Use a model of **8B or bigger**: smaller ones mix up the prices.

## Other providers

| Provider | Address | Key |
|---|---|---|
| Ollama | `http://localhost:11434` | none |
| OpenAI-compatible: LM Studio, OpenRouter, OpenAI | `http://127.0.0.1:1234/v1` (LM Studio) | yours, if the service needs one |
| Anthropic (Claude) | `https://api.anthropic.com` | yours |

Pick the provider in **Settings → AI**, type the address, the model (empty: the provider's first one) and your key.
**Save**, then **Test** to see whether it answers. Only Save keeps them: close Settings without it and the game goes on
with the provider it had.

The key is saved encrypted for your Windows user, never in plain text, and is never sent over plain `http://` to another
machine.

### Talk detail

A paid service bills by the token. **Talk detail** sets how much each line of a talk sends:

- **Light**: the main facts and the last few lines
- **Normal**: most facts, a fair stretch of the talk
- **Full**: everything the game knows (the default)

Settings shows the most a line can send. Most send far less. A lighter detail makes people forget nothing: it only
tells them less at a time.

## What gets sent

With the text AI on your PC (Ollama, LM Studio), nothing it writes leaves it. With a provider on another machine or a
cloud service, each request sends the facts of your career the talk is about and what you type. Settings says so under
the address.

ComfyUI portraits are a second address: each new rival's look, name, nickname, age and short story go to it. On your
PC that stays there too.

More in [Privacy](https://streetcorsamods.github.io/about/#privacy).

## If it goes wrong

- **Test says it did not work.** Check that Ollama is running and the address is right.
- **Make the game's model says to pull it first.** Run `ollama pull llama3.1:8b`, then press it again.
- **People make things up** (a paint colour, a radio). Small models do that sometimes. A bigger model does better.
  Prices are never theirs to make up.
