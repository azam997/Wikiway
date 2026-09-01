---
version: "0.1.3" # version of this declaration, not of the plugin
level: copilot
processes:
  design: pair
  implementation: copilot
  unit testing: copilot
  in game testing: none
  documentation: copilot
  code review: pair
---

## Notes

- Developed interactively with Claude Code (Anthropic). The AI implements whole tasks from human prompts and asks for clarification when needed; direction, feature decisions, and acceptance are human.
- Design conventions (UI behavior, data-sourcing rules, workflow) are set by the maintainer.
- Tests (including the canary suite) are largely AI-written and human-reviewed; all changes are reviewed and verified in-game by the maintainer before release.
- Every change is human-reviewed with extra scrutiny on threading, network requests (wiki calls), Dalamud API calls, and reads of game memory, and the canary suite revalidates game-data and wiki assumptions after every patch. (I have limited prior C# experience, which is why implementation leans on AI writing the code pretty heavily.)
