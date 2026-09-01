# Submitting to the official Dalamud repo

Checklist for a PR against
[goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17).

## Before every submission

1. `scripts\run-tests.ps1` passes.
2. `scripts\run-canaries.ps1` passes.
3. In-game smoke test: the query list in [patch-checklist.md](patch-checklist.md),
   step 5, plus opening the config window and a clean install/uninstall.
4. Version bumped in `src\Wikiway.Plugin\Wikiway.Plugin.csproj` (a fixed
   version per commit - never a timestamp or build counter).
5. Everything committed and pushed; the manifest pins the exact commit hash.

## Technical criteria (verified 2026-09-01)

- `images/icon.png` between 64x64 and 512x512 - ours is 512x512.
- Regular windows use the Dalamud Windowing API - all three windows are
  registered through `WindowSystem`.
- Version is fixed per commit, not timestamp- or counter-based.

## The PR

New plugins go to the testing channel first. In a fork of
DalamudPluginsD17 (or the GitHub web editor):

1. Create `testing/live/Wikiway/manifest.toml`:

   ```toml
   [plugin]
   repository = "https://github.com/azam997/Wikiway.git"
   commit = "<full hash of the pinned commit>"
   owners = ["azam997"]
   project_path = "src/Wikiway.Plugin"
   changelog = "<user-facing summary of what changed>"
   ```

2. Copy `src/Wikiway.Plugin/images/icon.png` to
   `testing/live/Wikiway/images/icon.png`.
3. Open the PR with the description below. The AI disclosure in the PR body
   is required by the
   [Dalamud AI Usage Policy](https://dalamud.dev/plugin-publishing/ai-policy) -
   the repo-side [AI-DECLARATION.md](../AI-DECLARATION.md) does not replace it.

## PR description template

```markdown
Wikiway - look up NPCs, quests, items, mounts, minions and achievements
without leaving the game. Answers come from local game data first and
ffxiv.consolegameswiki.com second; every result shows its source.

Network access: wiki lookups only (the search term and nothing else -
no character/account data, no telemetry), throttled to one request per
second, cached on disk, and can be disabled entirely in the config window.

AI usage disclosure: developed with AI assistance at the **Copilot** level
(Claude Code): the AI wrote most of the code and unit tests; design
direction, code review, in-game verification and final responsibility are
the maintainer's. No assets are AI-generated. Full breakdown:
[AI-DECLARATION.md](https://github.com/azam997/Wikiway/blob/master/AI-DECLARATION.md).
```

## After acceptance

- Retire the custom testing repo: stop publishing to `repo.json` and tell
  testers to remove the custom repo URL from `/xlsettings`, then update from
  the official repo. Two sources with the same internal name conflict in the
  plugin installer.
- Update [testing-install.md](testing-install.md) to point at the official
  testing channel.
