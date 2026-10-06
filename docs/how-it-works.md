# How Wikiway works

This is my walkthrough of the code for whoever reviews it. Wikiway was written with a lot of AI help (see [AI-DECLARATION.md](../AI-DECLARATION.md)), but guided and monitored at every step. I tried to create testable code with tests laid out from the start to help be as deterministic as possible.

## The short version

You type a name or a question. Wikiway looks it up in the game's own data files first, and on the wiki (ffxiv.consolegameswiki.com) second. Results show as cards you can expand, and each card says where it came from.

## The three 'projects'

- `Wikiway.Core` - the search itself. Plain .NET, no Dalamud, no Lumina. Unit tested.
- `Wikiway.GameData` - reads the game's Excel sheets with Lumina and turns rows into simple records. It doesn't use Dalamud, so it can run outside the game against a real install.
- `Wikiway.Plugin` - the only part that uses Dalamud: windows, the /wikiway command, right-click menus, quest progress, map flags.

## What happens when you search

1. The query comes in from the search box, /wikiway, a right-click, the solo duty notification, or a link on a card. All of those run on the game's main thread.
2. `MainWindow.RunQuery` takes a snapshot of your quest progress (that has to happen on the main thread) and starts the search on a background task.
3. `QueryNormalizer` reads the text. A prefix like `item:`, `quest:`, `npc:` or `gather:` limits the kind of result. Phrases like "where is" or "how do I get" are noted and removed.
4. Both sources run at the same time:
   - Game data: `FuzzyNameIndex` matches against every name in the game (about 75k), `LuminaGameDataStore` builds a card for each match, and `EntityGrouper` merges duplicates. That's mostly NPCs, since the game has a separate copy of an NPC for each cutscene they're in.
   - Wiki: a normal wiki search. For "how do I get" and "how do I unlock" questions it also pulls the matching sections of the top page.
5. `ResultRanker` sorts everything, with exact game data matches on top. `ResultJoiner` attaches wiki sections to the matching card so the same thing doesn't show twice.
6. Back on the main thread, `MainWindow.Harvest` picks up the result and the window draws it.

## Threads

Everything that touches the game runs on the main thread: drawing, commands, right-click menus, reading quest progress. The search runs on a background thread and never touches the game or Dalamud. The only player info it uses is the quest progress snapshot from step 2.

Clicking a link inside the results doesn't start a search right away, because that would change the list while it's being drawn. It's queued and runs on the next frame (`queuedNavigation` in `MainWindow`).

Some lookups need to scan a whole sheet. Ex: which recipes make an item. Those are built once in the background when the plugin loads (`WarmAll`), so searches and right-click menus don't wait on them.

## Game data

The rule I hold the code to: only show what the game files actually say. If they can't answer something reliably (duty drops, where a story NPC currently is, hunt spawn points), the card leaves it out and the wiki covers it.

When a sheet column doesn't mean what its name suggests, it was checked against the real data with a test query first. The comment says what was checked, and a canary test locks it in, so a patch that changes it fails a test instead of showing wrong info.

`LuminaGameDataStore` is split into one file per feature. The canary tests use the same names.

- Items, equipment, food: `.Items.cs`
- Where to get an item: `.Acquisition.cs`, `.AcquisitionIndex.cs`
- What an item is used for: `.Usage.cs`
- Mounts, minions, orchestrion rolls, Triple Triad cards, emotes: `.Collections.cs`
- Quests and unlock chains: `.Quests.cs`
- Duties and unlockable areas: `.Duties.cs`
- NPCs: `.Npcs.cs`
- Vistas, hunts, aether currents, FATEs, leves, achievements: `.World.cs`
- Map coordinates and nearest aetheryte: `.Locations.cs`

## The wiki

Only the search text is sent. No character, world or account info. Responses are cached on disk (6 hours for searches, 24 for pages), requests are limited to one per second, and it backs off if the wiki says it's busy. Wiki search can be turned off in settings, and then the plugin makes no network requests at all.

## Things that touch the game

Each one is listed with its reason in [dalamud-api-usage.md](dalamud-api-usage.md).

- Right-click "Look up on Wikiway" on items, NPCs and enemies. For some windows the item id is read from the game's UI state.
- Reading the quest journal and completed quests, for the active quest list and spoiler hiding.
- Opening the map with a flag.
- A Teleport button that asks the Teleporter plugin to teleport. Wikiway doesn't teleport on its own.
- A small notification when you enter a solo duty.

All of it is read only, and none of it automates anything.

## Tests

There are two test projects, and they catch different kinds of problems.

### Unit tests

`tests/Wikiway.Core.Tests`, run with `scripts\run-tests.ps1`.

These test `Wikiway.Core`: reading the query, name matching, ranking, merging duplicate NPCs, unlock chains, the wiki client, and the cache and rate limit. They use small hand-written fakes instead of the game files or the wiki, so they run anywhere in under a second. They catch mistakes in the logic.

### Canary tests

`tests/Wikiway.Canary.Tests`, run with `scripts\run-canaries.ps1`.

The name comes from the canary in a coal mine: these are meant to fail first when something outside the plugin changes. Wikiway depends on two things I don't control, the game's data files (which can change every patch) and the wiki's API. Unit tests can't catch changes to either one, because they use fakes.

- GameData canaries (75) open the real game files from a local FFXIV install, outside the game, and look up things with a known answer. Ex: Momodi is in Ul'dah, Iron Ingot is crafted from Iron Ore and sold by vendors for 68 gil, The Bozja Incident has two unlock chains. If a patch renames a column or moves data around, one of these fails and the file it's in names the feature, before anything catastrophic happens.
- WikiLive canaries (5) call the real wiki and check that its responses still look the way the client expects. They skip if there's no network, and fail if the wiki answers with something different.

Each canary has a comment saying what it checks and why. The canary files follow the same split as `LuminaGameDataStore`, so `GameDataCanaryTests.Items.cs` tests `LuminaGameDataStore.Items.cs`.

I will run these after every game patch ([patch-checklist.md](patch-checklist.md)).

## Recommended Review Process

I'd start with `Plugin.cs` (what gets created and how it shuts down), then `QueryOrchestrator.cs` (the whole search, and it's short), then `GameIntegration/` (everything that reads game memory or talks to another plugin).

Most of the line count is in `GameData/` and `Windows/`. GameData is mostly reading sheet rows into records, and the Windows files are ImGui layout (probably a bit overboard on being decorative). The parts I reviewed the hardest are threading, the network code, and the game memory reads.

Known limits:

- If a patch breaks the name index, game data results stay unavailable until the plugin reloads. The wiki still works.
- Wiki results are in English.
- The game files have no loot tables, so "where does this drop" comes from the wiki.
