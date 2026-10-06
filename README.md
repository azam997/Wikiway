# Wikiway

A Dalamud plugin for FFXIV that answers lookup questions in-game: where NPCs are,
what a quest needs, how to get an item, mounts, minions, achievements.

Type `/wikiway where is momodi` (or just `/wikiway momodi`). Answers come from
local game data first, and from [ffxiv.consolegameswiki.com](https://ffxiv.consolegameswiki.com/wiki/FF14_Wiki)
when the game data alone isn't enough. Every result shows where it came from.

Local results follow your client language; the wiki is English, so wiki results
work best with English terms.

For a walkthrough of the code, see [docs/how-it-works.md](docs/how-it-works.md).

## AI assistance

Wikiway was written with AI assistance at the **Copilot** level as defined by
the [Dalamud AI Usage Policy](https://dalamud.dev/plugin-publishing/ai-policy):
the AI wrote most of the code and unit tests, while planning, code review,
in-game verification and final responsibility for the result are the
maintainer's. See [AI-DECLARATION.md](AI-DECLARATION.md) for the full
breakdown. No assets in this repository are AI-generated.

## Network Requests

Local game data answers most questions with no network access at all. When the
wiki is consulted, the plugin sends the search term to
`ffxiv.consolegameswiki.com` and nothing else: no character name, no world, no
account identifier, no telemetry. Requests are throttled to one per second,
carry a descriptive User-Agent, and are cached on disk under the plugin's
config directory. Wiki lookups can be turned off entirely in the config window,
which leaves the plugin fully offline.

## Building

Requires .NET 10 SDK and a Dalamud dev install (comes with XIVLauncher after
launching the game once).

```
dotnet build
```

The plugin project resolves Dalamud assemblies from
`%AppData%\XIVLauncher\addon\Hooks\dev\` (override with the `DALAMUD_HOME`
environment variable).

## Loading the dev plugin

1. Launch the game through XIVLauncher.
2. `/xlsettings` → Experimental → Dev Plugin Locations → add the full path to
   `src\Wikiway.Plugin\bin\Debug\Wikiway.dll`, save.
3. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable Wikiway.
4. After a rebuild, reload the plugin from the same list. Logs are in `/xllog`.

## Tests

```
scripts\run-tests.ps1      # fast unit tests, run anytime
scripts\run-canaries.ps1   # run after every FFXIV patch (see below)
```

The canary suite is the early-warning system for things that break under us:

- **GameData** canaries load the real game files (standalone, outside the game)
  and check that the Excel sheets and joins we depend on still resolve. If a
  patch renames or renumbers a column, these fail before you notice in-game.
- **WikiLive** canaries hit the real wiki API and check response shapes.

## After a patch or Dalamud API bump

Follow [docs/patch-checklist.md](docs/patch-checklist.md): rebuild, align the
Lumina pins, run both test scripts, then try a few queries in-game.

## Packaging

`dotnet build src\Wikiway.Plugin -c Release` produces the distributable zip at
`src\Wikiway.Plugin\bin\Release\Wikiway\latest.zip` via DalamudPackager.

## Layout

- `src/Wikiway.Core` - all the logic. No Dalamud, no Lumina; plain .NET.
- `src/Wikiway.GameData` - Lumina-facing data access. Works in-game *and*
  standalone against a game install, which is what makes the canaries possible.
- `src/Wikiway.Plugin` - the only project that touches Dalamud (UI, commands, wiring).
- `tests/` - unit tests and the canary suite.
- `docs/`
  - [how-it-works.md](docs/how-it-works.md) - a walkthrough of the code for
    reviewers.
  - [dalamud-api-usage.md](docs/dalamud-api-usage.md) - every Dalamud API call
    and game-memory read, with where and why.
  - [patch-checklist.md](docs/patch-checklist.md) - what to do after a game
    patch or Dalamud update.
  - [testing-install.md](docs/testing-install.md) - installing a testing build
    from the custom repo.

## License

AGPL-3.0-or-later. See [LICENSE](LICENSE).
