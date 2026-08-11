# Wikiway

A Dalamud plugin for FFXIV that answers lookup questions in-game: where NPCs are,
what a quest needs, how to get an item, mounts, minions, achievements.

Type `/wikiway where is momodi` (or just `/wikiway momodi`). Answers come from
local game data first, and from [ffxiv.consolegameswiki.com](https://ffxiv.consolegameswiki.com/wiki/FF14_Wiki)
when the game data alone isn't enough. Every result shows where it came from.

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

1. If the build breaks after a Dalamud update: bump the `Dalamud.NET.Sdk`
   version pin in `src\Wikiway.Plugin\Wikiway.Plugin.csproj` and fix what the
   compiler reports.
2. Check the `Lumina.dll` / `Lumina.Excel.dll` file versions in
   `%AppData%\XIVLauncher\addon\Hooks\dev\` and align the NuGet pins in
   `src\Wikiway.GameData\Wikiway.GameData.csproj` and
   `tests\Wikiway.Canary.Tests\Wikiway.Canary.Tests.csproj`.
3. `scripts\run-tests.ps1`, then `scripts\run-canaries.ps1`.
4. Load in-game and try a couple of queries.

## Packaging

`dotnet build src\Wikiway.Plugin -c Release` produces the distributable zip at
`src\Wikiway.Plugin\bin\Release\Wikiway\latest.zip` via DalamudPackager. Clean
the bin folder first if you've built Release before - stale packager output
gets swept into the zip otherwise. Submission to the official repo goes
through a PR against [goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17)
once the plugin has had some real-world soak time.

## Layout

- `src/Wikiway.Core` - all the logic. No Dalamud, no Lumina; plain .NET.
- `src/Wikiway.GameData` - Lumina-facing data access. Works in-game *and*
  standalone against a game install, which is what makes the canaries possible.
- `src/Wikiway.Plugin` - the only project that touches Dalamud (UI, commands, wiring).
- `tests/` - unit tests and the canary suite.
- `docs/` - design notes and internal documentation.
