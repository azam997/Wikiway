---
version: "0.1.2"
level: copilot
processes:
  design: pair
  implementation: copilot
  testing: copilot
  documentation: copilot
  review: pair
---

## Notes

- Developed interactively with Claude Code (Anthropic). The AI implements whole tasks from human prompts and asks for clarification when needed; direction, feature decisions, and acceptance are human.
- Design conventions (UI behavior, data-sourcing rules, workflow) are set by the maintainer; the AI works within them.
- Tests (including the canary suite) are largely AI-written and human-reviewed; all changes are reviewed and verified in-game by the maintainer before release.
- I lack experience in C#, hence the heavy reliance on AI coding. I have done my best to adhere to best practices and review all code (especially threading, network requests (wiki calls), Dalamud calls, and calls to game memory).
