# Submitting to the official plugin repository

Everything here follows the rules in
[DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17) and
[dalamud.dev/plugin-publishing](https://dalamud.dev/plugin-publishing/).
Submission is a PR against DalamudPluginsD17 that points at a commit in this
repo; the plugin itself is built by their cloud builder, in an isolated
environment with no internet access.

## What the repo already satisfies

- Git repo that clones over plain HTTP with no authentication. The repo is
  private for now; flip it to public before opening the submission PR, since the
  cloud builder clones it anonymously.
- Open source (AGPL-3.0-or-later, `LICENSE` at the root). Closed-source plugins
  are never accepted.
- `Dalamud.NET.Sdk/15.0.0` - the latest SDK - in `Wikiway.Plugin.csproj`,
  which pulls in DalamudPackager.
- `packages.lock.json` committed for every project the plugin build touches
  (`Wikiway.Plugin`, `Wikiway.Core`, `Wikiway.GameData`), so the offline
  builder resolves the same packages we did.
- Fixed `<Version>` (`0.1.0.0`), not a timestamp or an auto-incrementing build
  number: the same commit always produces the same version.
- All windows go through the Dalamud Windowing API (`WindowSystem`).
- Release build is clean and `latest.zip` contains only plugin assemblies.

## Still to do before opening the PR

- **`icon.png`** - required, 1:1, between 64x64 and 512x512. It goes in the
  `images/` folder next to the manifest in the DalamudPluginsD17 PR (keep a
  copy in this repo too). The AI policy asks for hand-made icons and says a
  crude MS Paint icon is preferred over an AI-generated one, so draw it
  yourself. Optionally add `image1.png`..`image5.png` screenshots.
- **Soak time.** New plugins go to the testing channel first; the approval
  group tests them by hand. Have the plugin working in-game over a few play
  sessions before submitting.
- **Be ready to explain the code.** Reviewers do an informal code review, and
  the AI policy explicitly requires that you understand, can test, and can
  explain what was written.

## Manifest

New plugins go to the testing channel, never straight to stable. In your fork
of DalamudPluginsD17, create `testing/live/Wikiway/manifest.toml`:

```toml
[plugin]
repository = "https://github.com/azam997/Wikiway.git"
commit = "<full sha of the commit you want built>"
owners = ["azam997"]
maintainers = ["azam997"]
project_path = "src/Wikiway.Plugin"
changelog = "Initial release."
```

and put the icon at `testing/live/Wikiway/images/icon.png`.

## PR description

AI use above plain autocomplete must be disclosed in the PR description.
Undisclosed AI use in clearly AI-written code is a ban; an entirely
AI-generated submission is auto-rejected. State the level plainly:

> **AI usage disclosure:** Copilot - AI wrote most of the code; I planned the
> work, reviewed the output, tested it in-game, and can explain the
> implementation. No AI-generated assets. (Levels per
> https://dalamud.dev/plugin-publishing/ai-policy)

Then describe what the plugin does, that it is informational only, and that the
only network traffic is search terms to `ffxiv.consolegameswiki.com`.

## Restrictions this plugin is measured against

Wikiway is a lookup tool, which keeps it clear of most of the
[restrictions](https://dalamud.dev/plugin-publishing/restrictions): it performs
no automation, takes no actions on the player's behalf, touches no combat data,
does no damage parsing or logging, collects no account IDs, and talks to no
game server. Two things to keep true as the plugin grows:

- The unimplemented `IAnswerSynthesizer` seam is where an LLM layer would go.
  If that ever ships it must be opt-in, use the user's own key, send nothing
  about the player, and is worth raising in the Dalamud Discord first.
- Any future in-game automation - auto-opening maps, moving the character,
  interacting with NPCs - crosses the automation line. Map links opened from a
  result are user-initiated, which is why they are fine.

## Updates after approval

Open a new branch on your DalamudPluginsD17 fork, change `commit` in the
manifest to the new sha, and open a PR. Updates need only one reviewer.
`bleatbot, rebuild` as a PR comment re-triggers a build.
