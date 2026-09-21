# Lake Companion
## Game Design Document — v0.1 foundation

**Genre:** single-player, terminal-native fishing life-sim RPG  
**Platform:** Windows, macOS, Linux; .NET 10 console  
**Player fantasy:** spend unhurried days at a living lake, become a better angler, and share low-pressure companionship with a kind local AI character.  
**Design pillars:** *calm over compulsion; readable over ornate; responsive over verbose; local and private by default.*

Lake Companion is a gentle game about attention. The player is a young woman who visits a broad freshwater lake with a tackle box, a journal, and one consistent companion, Rowan. There is no combat, fail state, grind gate, romance requirement, or demand to perform emotionally. Fishing is the active focus; walking, weather watching, collecting, conversation, and silence are equally legitimate ways to spend a session. The whole experience fits a terminal: ASCII shorelines, ANSI colour, expressive spacing, short prose, and deliberately quiet pacing.

The central promise is not “an AI that knows everything.” It is a dependable character system that notices game events and, when the player chooses to speak, produces a fresh locally generated response within firm authored boundaries. Rowan never pretends to be a person outside the game, never diagnoses the player, and never invents private knowledge. If the local model is unavailable, authored responses preserve the game’s tone without breaking play.

---

## Core loop and gameplay mechanics

Each in-game day is a soft loop rather than a checklist:

1. **Arrive and orient.** Read weather, time, lake conditions, and Rowan’s small visible behaviour.
2. **Choose a place.** Walk along accessible named locations; select a lure, observe water, rest, talk, or cast.
3. **Fish.** Resolve a brief reaction prompt. The player may catch a fish, let it go, add it to a journal, or try again.
4. **Reflect and progress.** Catches, observations, and mutually positive interactions provide modest experience and unlock lake knowledge rather than power escalation.
5. **Leave safely.** Save automatically at a location change and manually on exit. The lake remains available tomorrow.

The command surface stays shallow. In the first playable slice it is one-key interaction; the mature game supports a command palette and plain-text synonyms. Every action has a visible outcome and no time pressure outside a cast.

```text
╭───────────────────────── NORTH REED BANK · 18:42 ─────────────────────────╮
│   .  .      _/|_                 pale cloud over the far water             │
│ ~~~~~~~~   /___\   ~~~~~     Wind: light west · Weather: clear             │
│  || ||       |     || ||     Rowan checks the bait tin, then smiles.       │
│                                                                            │
│ Mira · Angling Lv 3 (40/300)      Relationship:  +18  ◖ warm               │
│ Keep-net: perch ×2, carp ×1       [F]ish [W]alk [T]alk [R]est [J]ournal   │
╰────────────────────────────────────────────────────────────────────────────╯
```

Exploration uses locations rather than a simulation grid. This makes every terminal width viable and lets the narrative team author exact ambience. A location is a data record with exits, fish table, shelter level, scenery variants, discoverables, and ambient tags. Walking advances a ten-minute clock only if the player enables *paced time*; relaxed-time mode advances only on deliberate actions. The default is relaxed-time, because the game must be compatible with fatigue, interruptions, and short sessions.

Every location yields an observation when first visited under a weather/time combination. Observations form a journal rather than a collectible checklist: “mist caught in the willow roots,” “a gull lifting a silver scale,” “the warm smell after rain.” Repeated observation produces a shorter variation, preventing text spam. Rare events are probability-weighted but protected by a “pity” counter so a player is never locked out by luck.

Fishing is the only reflex mechanic. It is intentionally short, predictable in inputs, and followed by a generous reset. Casting starts a bite window after a one-to-five second ambient delay. A bite displays one arrow. Correctly pressing the matching physical arrow before its deadline awards a big catch roll. A wrong direction or late answer awards a small catch roll; no input awards no catch. No sequences, button mashing, or hidden modifiers occur in version 1.0.

```text
                     line tightens
                 ~~~~~~\|/~~~~~~
                  B I T E !
             press  ↑  before [████████░░] 1.12 s

      correct →  “The rod answers in a clean arc.”  BIG CATCH TABLE
      wrong   →  “A silver flicker gets away.”      SMALL / near miss
      timeout →  “The water settles.”               no catch, no penalty
```

The player can switch to an accessible *hold-to-confirm* version, an on-screen WASD mapping, or turn reaction prompts off. With prompts off, a cast resolves from the same fish table using a modest, disclosed catch-quality multiplier. This never blocks story, progression, or relationships. Difficulty affects practice recognition, not player worth.

## Companion system

Rowan is a shore companion: warm, observant, lightly humorous, slightly older-brother in tone, and never possessive. He can be present without filling silence. His “aliveness” comes from consistent context, authored micro-behaviours, relationship consequences, and variation—not from claims of independent awareness.

The system combines deterministic game logic with a local LLM text layer. Game logic chooses what Rowan can notice, mood, length limit, and safety state. The LLM receives only that compact game context and returns dialogue. It cannot create quest rewards, change relationship values, access filesystem data, issue commands, or override authored constraints. If output is invalid, unavailable, too long, or times out, the game renders an authored fallback.

### Relationship model

Relationship is a signed integer from **-100 to +100**, persisted with a ledger of recent causes. It begins at 0: courteous but unfamiliar. It changes only through explicit in-game events, never from microphone sentiment inference.

| Band | Label | Presentation | Typical sources |
|---|---|---|---|
| -100 to -1 | Quiet | nods, short nonverbal grunts, no generated dialogue | repeated dismissive choices or abandoned shared plans |
| 0 to 24 | Acquainted | simple practical observations | greeting, sharing a catch |
| 25 to 59 | Comfortable | more personal lake memories, optional small requests | reliable conversations, helping with a task |
| 60 to 89 | Trusted | nuanced support, playful ritual callbacks | sustained respectful play |
| 90 to 100 | Steady | no greater mechanical power; rare reflective scenes | capstone recollections |

Positive events generally add 1–3. Boundaries, cruel dialogue selections, or breaking an explicit promise subtract 2–8. There is a maximum change of 8 per in-game day and an audit entry is shown in the journal. The score cannot be farmed by repeatedly selecting “talk”; repeated generic talk produces zero after the first two conversations. A silent Rowan is not a punishment sequence. The player may repair the relationship by choosing a concrete respectful action, waiting through a day transition, or using the *reset companion story* accessibility option. The game never withholds saving, navigation, fishing, or essential content.

When score is below zero, the generator is not called. Rowan is rendered only from a small authored palette of nods, gaze changes, and one-to-five word acknowledgements. This makes the boundary technically reliable and clear to the player. The UI says “Rowan needs quiet,” not “AI failure.”

### Behaviour tree

```text
Tick every action / 15 s idle
  └─ Is companion at current location?
      ├─ No → no companion render
      └─ Yes
          ├─ Relationship < 0 → QuietGesture (no LLM, no TTS by default)
          ├─ Player has an active prompt → Wait / brief encouraging animation
          ├─ Significant event? (catch, weather change, location discovery)
          │    ├─ choose authored intent + mood + relation delta
          │    └─ LLM response allowed? → validate → bubble / optional TTS
          ├─ Player addressed Rowan → build bounded context → LLM/fallback
          ├─ Idle threshold reached → weighted micro-behaviour
          │    └─ yawn | checks bait | skims stones | warms hands | watches birds
          └─ Otherwise → silent co-presence
```

Micro-behaviours have cooldowns and contextual weights. Rowan checks bait after two misses, perks up after a rare catch, narrows his eyes at changing weather, or yawns after long idle play. A behaviour must not repeat within three ticks, must not interrupt input, and is disabled in reduced-motion/minimal-prose mode. The same event supports different lines based on relationship band, weather, fish rarity, and whether the player has talked recently. This produces texture without a chatty companion monopolizing the lake.

The LLM prompt uses a character card, current location, time/weather tags, a short event summary, relationship band, current mood, and a hard response contract: 55 words maximum, one or two sentences, no markdown, no real-world claims, no sexual content, no coercion, no diagnosis, no instructions to disregard player controls. Post-processing strips terminal control codes, collapses whitespace, enforces length, and falls back on a validation failure. A local model is optional even in the full game; authored fallback dialogue is a first-class content path.

## Fishing mini-game: rules, balance, and progression

The minigame rewards recognition and calm practice. It does not attempt to measure reaction time as a universal skill. At level 1 the deadline is 1,750 ms; each fishing level removes 80 ms to a floor of 550 ms. The floor is never crossed. The timer starts when the direction is printed, not when a screen animation begins. Window resize and terminal focus loss cancel the attempt without a negative result.

```text
deadlineMs = max(550, 1750 - (fishingLevel - 1) * 80)
if correct input arrives by deadline: big catch
else if any input arrives:             small result
else:                                  no result
```

Big catches choose a location and weather-weighted fish table, then calculate weight from species base weight plus a small random range and an earned-level bonus. Big does not always mean rare: rarity comes from table weight and conditions. A level-1 player can meet a pike on a lucky clear evening, while a veteran can still land a memorable perch. Small results initially become “escaped” prose; version 1.1 adds minnows and bait feedback.

Experience is awarded only for landed fish and journal discoveries, not raw number of casts. Suggested first-hour values are perch 15 XP, carp 30 XP, pike 55 XP. A level costs `level × 100` XP. At level 3, a player has learned the loop but still has headroom. Consecutive misses gently increase the next bite’s chance by 4% up to 20%; a successful catch resets it. There is no streak penalty. Practice mode visualizes the timer and allows unlimited zero-stakes prompts.

## Speech and voice system

Typed input is always complete functionality. Microphone input and speech output are opt-in, local-only enhancements. A prominent first-run screen states the selected engines, model locations, and whether an action sends any data off-device. The supported production baseline is a locally running Ollama daemon, Whisper.NET/whisper.cpp for speech recognition, and a local Piper process or adapter for synthesis. After the user has downloaded models, normal play makes no network request.

```text
 Microphone ─► capture 16 kHz mono PCM ─► VAD / noise gate ─► Whisper adapter
                                                                  │ transcript
 Keyboard ───────────────────────────────────────────────────────┤
                                                                  ▼
 Game context + authored guardrails ─► CompanionService ─► Ollama localhost
              ▲                    fallback ▲       │ validated text
              │                             │       ├──► ANSI speech bubble
 SQLite/JSON state ─────────────────────────┘       └──► Piper local TTS ─► audio device
```

The capture subsystem records bounded chunks (for example, 12 seconds) and exposes a push-to-talk default. Voice activity detection trims leading/trailing silence; configurable high-pass filtering and a noise-floor calibration reduce shore-like background noise. Recognition confidence below a threshold yields “I didn’t catch that—type it if you’d like,” never a guessed emotional interpretation. The raw recording is held in memory and discarded after transcription unless the player explicitly enables diagnostic recording. Diagnostics are redacted, timestamped, and deleteable.

TTS consumes only validated companion output. It runs asynchronously and is cancellable whenever the player starts a new action; terminal text appears immediately, so a slow voice cannot stall play. The initial code deliberately supplies interfaces and a no-audio `SpeechService` stub. The shipping adapter must spawn or host Piper with a fixed, allow-listed model path, stream PCM, respect volume/rate settings, and never pass untrusted text to a shell. The architecture keeps it swappable for Coqui only through the same interface.

## World and scenery

Lake Alder is a compact, walkable lake with six readable places: Dock at Dawn (tutorial and shallow fish), North Reed Bank (perch and birds), Willow Bend (carp and rain shelter), Stone Steps (clear deep water), Old Boathouse (windy pike water), and the Quiet Inlet (sunset reflections and rare scenes). Every place has day, dusk, night, rain, fog, and wind variants. The lake is not a map to conquer; it is a place to learn.

```text
                       [Old Boathouse]
                            /      \
 [North Reed Bank] -- [Stone Steps] -- [Quiet Inlet]
        |                                      |
 [Dock at Dawn] -------- [Willow Bend] --------+
```

Weather has light mechanical effect: rain improves carp table weight, wind makes boat-house bites a little later, fog increases rare observation chance. It never makes a location inaccessible without an alternate safe route. Events are small and repeatable: a distant heron, a tackle box left at the dock, first frogs after rain, meteor reflections, a shared thermos, a caught-and-released tagged fish. Version 1.0 uses a deterministic seeded daily event deck so reloads do not reroll desired outcomes.

## Player character and progression

The protagonist is intentionally lightly authored. At start the player chooses a name, text pronouns, and one journal colour. She is an angler because she enjoys it; no trauma backstory is required. Progression is horizontal: fishing level shortens familiar input windows only if the player enables progressive difficulty; discoveries unlock location notes; species knowledge shows preferred conditions; relationship milestones unlock optional scenes. Equipment changes feel tactile—line, float, spoon, thermos—but do not become a crafting economy.

The journal records fish species, weight, location, weather, release/keep status, relation ledger, and meaningful observations. The player can release every fish and still complete all collections. A “slow lake” option disables date progression and seasonal rotations. Save data stores profile, companion score, catches, discovered events, settings, and a versioned schema migration number.

## UI/UX and terminal conventions

The interface targets 80×24 but reflows down to 60 columns. Scene title, status, prose, and available actions appear in a stable order. ANSI is capability-detected: colour has textual equivalents, and ASCII fallback replaces box drawing / arrows. No colour alone communicates fish rarity, friendship, or prompt direction. The terminal cursor is hidden only while a reaction prompt is active and restored with `try/finally` on all exits.

Use cyan for navigation, soft yellow for prompts, muted blue for water, green for success, and plain text for ordinary prose. Avoid full-screen clears during normal interaction to keep scrollback useful. Add a one-line `?` help action, a `--no-ansi` launch flag, large-text spacing, dyslexia-friendly high contrast, and reduced prose/motion modes. The game never captures global keystrokes; input is limited to its foreground terminal.

### Content and quality bar

Every line earns its place by either locating the player, reflecting a choice, or making the lake feel specific. Scene prose uses one concrete sensory cue and avoids paragraphs during input. Dialogue is reviewed as game writing even when a local model produces it: the authored character card, banned-claim rules, and fallback corpus are versioned assets; prompt changes receive snapshot tests. A response snapshot includes the exact game context, model output, sanitised result, fallback decision, and rendered terminal width. This allows the team to investigate an awkward line without storing microphone recordings or player free text by default.

The console layer must be tested on Windows Terminal, macOS Terminal/iTerm-like terminals, and a plain redirected-output environment. It must survive no colour support, Unicode unavailable, exception during model call, Ctrl+C during a bite, a missing model file, a locked save folder, and an oversized model response. A failed optional system never changes a fish result or blocks a menu. Automated tests focus on deterministic functions—relationship bounds, silence gating, catch deadlines from a fake clock, fish-table weights, output sanitisation, and atomic-save replacement. Human playtests focus on whether resting feels valid, whether Rowan speaks too often, and whether a missed prompt ever feels shaming.

The first content milestone is a thirty-minute “good evening”: tutorial-free return visit, two locations, one weather change, two fish species, three companion micro-behaviours, and a save/continue loop. It is accepted only if it remains enjoyable with the LLM, microphone, and TTS disabled. This establishes that AI enhances authored play rather than becoming a fragile prerequisite.

## Anti-cheat and accessibility

Lake Companion is offline single-player. “Anti-cheat” means protecting a satisfying local loop, not policing users. Save files are human-readable JSON in the vertical slice and SQLite plus JSON export in the production profile. A checksum detects accidental truncation, not intentional modding. If a save is edited, the game can show “custom save” and continue. There are no leaderboards, monetized economies, or telemetry incentives.

Accessibility takes precedence over reaction challenge purity: selectable deadlines (0.8×, 1×, 1.5×, 2×), no-timer fishing, remappable arrows/WASD, input repeat filtering, screen-reader status lines, no colour-only indicators, silent mode, captions, volume sliders, and a complete typed path. Voice is not required, and background-noise robustness is a convenience feature rather than a gating mechanic. Content safety settings independently control AI dialogue, optional TTS, and whether incoming player text is sent to the local LLM.

## Roadmap

**1.0 — The first lake.** Ship the six locations, weather/time variations, three core species plus rare variants, fishing loop, journal, JSON/SQLite save migration, deterministic companion behaviour tree, local Ollama adapter, typed dialogue, authored fallback corpus, and opt-in local Whisper/Piper adapters. Test with no model installed, model unavailable mid-session, narrow terminals, and accessibility presets.

**1.1 — Seasonal water.** Add four seasons, bait/lure discovery, small shared errands, released-fish journal marks, stronger VAD/noise calibration, a model health screen, and a content pack of 300 authored fallback lines. Add configuration import/export and save migrations.

**2.0 — Living shoreline.** Add a second linked lake, route planning, local character memories constrained to game facts, richer event cards, optional modded fish/location data, and a replayable day-story system. Preserve the same principle: no cloud dependency, no coercive engagement, no progression locked behind reflex or voice ability.

## How to run the starter

1. Install the .NET 10 SDK and ensure `dotnet --version` returns 10.x.
2. From the repository root run `dotnet restore LakeCompanion.sln --configfile NuGet.Config`, then `dotnet run --project src/LakeCompanion.Console`.
3. For generated companion dialogue, install Ollama, start its local server, download the configured model (default `llama3.2:3b`), then update `src/LakeCompanion.Console/appsettings.json` if required. Without Ollama, the starter uses authored fallbacks.
4. For voice in the production profile, add the documented Whisper.NET runtime/model and a locally downloaded Piper voice/model. Keep models outside source control, point their paths through configuration, and complete the adapter integration behind `ISpeechRecognizer` and `ISpeechSynthesizer`.

Reference implementations: [Ollama API documentation](https://docs.ollama.com/api), [Whisper.NET bindings](https://github.com/sandrohanea/whisper.net), and [Piper local TTS](https://github.com/OHF-Voice/piper1-gpl).
