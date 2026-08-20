# Code style notes (internal)

These rules come from reading real plugin sources (SamplePlugin,
Caraxi/SimpleTweaksPlugin, Ottermandias/GatherBuddy, MidoriKami/DailyDuty) —
measured comment density in shipped community plugins is ~0–1 comments per 100
lines. Explanatory writing belongs here in `docs/`, not in the code.

## Rules

1. Almost no comments: 0–2 per 100 lines. If it's obvious to someone who knows
   Dalamud/C#, no comment.
2. When commenting, explain **why** or a **game-client quirk** (sheet layout
   oddities, polling behavior, workarounds). Never narrate what the next line
   does ("// Create the window" says nothing the code doesn't).
3. No `///` XML doc comments anywhere — none of the surveyed plugins use them,
   even on public API. No file headers, no author/license banners.
4. No `#region`. `#if DEBUG` is fine.
5. File-scoped namespaces. `partial class` to split big UI files.
6. Brace style: **Allman** (goatcorp/Ottermandias style) — chosen for this repo,
   keep it consistent. Braces may be dropped on single-statement guards:
   `if (x == null) return;`
7. `var` everywhere it compiles; guard clauses over nesting.
8. Nullable on; `[PluginService] internal static IFoo Foo { get; private set; } = null!;`
   for services.
9. camelCase private fields (no `_` prefix — pick stays consistent),
   PascalCase everything public. `private const string` for command names.
10. Modern C# freely: collection expressions, `is not`, switch expressions,
    target-typed `new()`, expression-bodied one-liners.
11. ImGui code is a straight-line imperative script; `##id` suffixes on
    widgets; `ImGuiHelpers.GlobalScale` for hardcoded pixel sizes.
12. Tutorial-style comments explaining the Dalamud API itself are exactly what
    SamplePlugin has and shipped plugins delete. Never keep them.

The one place we deliberately deviate from the community norm: this codebase
uses interfaces (`ISearchProvider`, `IGameDataStore`, ...) rather than static
service locators, because the provider seam and the standalone canary tests
depend on them. That's an architecture choice, not a style one.
