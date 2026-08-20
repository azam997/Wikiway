# Dalamud API and game-struct usage

This doc outlines every place Wikiway touches the Dalamud API or FFXIV game memory (FFXIVClientStructs), with where and why. Only `Wikiway.Plugin` touches either; `Wikiway.Core` and `Wikiway.GameData` are Dalamud-free (`Wikiway.GameData` reads sheets through the Lumina `GameData` handle that `Plugin` passes in). Line numbers are current as of 2026-08-20. This does not include imGui.

## Service injection

All Dalamud services arrive via `[PluginService]` static properties on `Plugin` (`src/Wikiway.Plugin/Plugin.cs:26-34`): `IDalamudPluginInterface`, `ICommandManager`, `IDataManager`, `IGameGui`, `IPluginLog`, `IContextMenu`, `IClientState`, `INotificationManager`, `ITextureProvider`. Everything below reaches them as `Plugin.<Service>`.

## IDalamudPluginInterface

| Call | Location | Why |
|---|---|---|
| `GetPluginConfig()` | `Plugin.cs:59` | Load the saved `Configuration` at startup. |
| `UiBuilder.FontAtlas` | `Plugin.cs:63` | Hand the atlas to `Fonts` so the plugin can build its own font sizes. |
| `GetPluginConfigDirectory()` | `Plugin.cs:66` | Root the on-disk wiki response cache under the plugin's config dir. |
| `UiBuilder.Draw += / -=` | `Plugin.cs:140`, `Plugin.cs:149` | Drive `WindowSystem.Draw` every frame (also the plugin's main-thread anchor). |
| `UiBuilder.OpenMainUi += / -=` | `Plugin.cs:141`, `Plugin.cs:150` | Open the main window from the plugin installer's button. |
| `UiBuilder.OpenConfigUi += / -=` | `Plugin.cs:142`, `Plugin.cs:151` | Open the settings window from the installer's cog. |
| `SavePluginConfig(this)` | `Configuration.cs:21` | Persist settings whenever a config value changes. |
| `AssemblyLocation.DirectoryName` | `MainWindow.cs:196` | Locate the bundled `images/logo64.png` next to the plugin assembly. |

`Configuration` implements `IPluginConfiguration` (`Configuration.cs:7`) so Dalamud can serialize it.

## ICommandManager

| Call | Location | Why |
|---|---|---|
| `AddHandler(name, new CommandInfo(OnCommand))` | `Plugin.cs:184` (called for `/wikiway`, `/wway`, `/ww` at `Plugin.cs:134-138`) | Register the chat commands, handle conflicts |
| `RemoveHandler` ×3 | `Plugin.cs:153-155` | Unregister on dispose. |

## IDataManager (Lumina sheets)

| Call | Location | Why |
|---|---|---|
| `DataManager.GameData` | `Plugin.cs:80` | Hand the Lumina handle to `LuminaGameDataStore`. Every sheet read in `Wikiway.GameData` (items, NPCs, quests, duties, etc.) flows through this. |
| `GetExcelSheet<Quest>()` | `GameIntegration/ActiveQuestReader.cs:32` | Resolve journal quest ids (`0x10000 + QuestId`) to display names and levels. |
| `GetExcelSheet<Quest>()` | `GameIntegration/QuestProgressTracker.cs:39` | Enumerate all quest row ids once, to scan the completion bitmap against. |

## FFXIVClientStructs (unsafe game memory)

The only game-struct type used is `FFXIVClientStructs.FFXIV.Client.Game.QuestManager`. Both readers run only from the draw loop, i.e. the game main thread (see threading note).

| Call | Location | Why |
|---|---|---|
| `QuestManager.Instance()` | `GameIntegration/ActiveQuestReader.cs:21` | Get the live journal to list active quests for the quest picker. |
| `manager->TrackedQuests` | `ActiveQuestReader.cs:26` | Mark which journal quests the player has focused, so they sort first. |
| `manager->NormalQuests` | `ActiveQuestReader.cs:33` | Enumerate accepted quests (id, hidden flag) for the picker list. |
| `QuestManager.Instance()` | `GameIntegration/QuestProgressTracker.cs:32` | Null/logged-out guard before snapshotting progress. |
| `QuestManager.IsQuestComplete(ushort)` (static) | `QuestProgressTracker.cs:54` | Read the completed-quest bitmap to build the spoiler-gating snapshot. Missing from the ClientStructs XML docs; verified present on this build by reflection and in-game. |
| `manager->NormalQuests` | `QuestProgressTracker.cs:59` | Record accepted (in-journal) quests, so their names aren't treated as spoilers. |

## IClientState

| Call | Location | Why |
|---|---|---|
| `IsLoggedIn` | `ActiveQuestReader.cs:18`, `QuestProgressTracker.cs:33` | Skip journal/progress reads while logged out; gating fails open. |
| `TerritoryChanged += / -=` | `GameIntegration/SoloDutyNotifier.cs:21`, `:26` | Detect zoning into a solo duty (fires at zone-in, before the commence dialog unlike `IDutyState.DutyStarted`). |
| `Logout += / -=` | `Windows/MainWindow.cs:64`, `:69` | Clear per-character spoiler reveals; the next login may be an alt. |

## IContextMenu

| Call | Location | Why |
|---|---|---|
| `OnMenuOpened += / -=` | `GameIntegration/ContextMenuIntegration.cs:28`, `:31` | Hook every game context menu to offer "Look up on Wikiway". |
| `args.Target` matching (`MenuTargetInventory`, `MenuTargetDefault`, `ObjectKind.EventNpc`) | `ContextMenuIntegration.cs:38-66` | Decide which menus get the entry: inventory items, chat item links, and event NPCs. |
| `args.AddMenuItem(new MenuItem {...})` | `ContextMenuIntegration.cs:75` | Insert the lookup entry with the `W` prefix glyph. |

## IGameGui

| Call | Location | Why |
|---|---|---|
| `HoveredItem` | `ContextMenuIntegration.cs:49` | The chat context menu target doesn't carry the item id; the hovered-item state does (with HQ/collectable offsets folded in, decoded at `:52-57`). |
| `OpenMapWithMapLink(MapLinkPayload)` | `GameIntegration/MapLinkOpener.cs:16` | Open the in-game map with a flag at a result's location. The `MapLinkPayload` (`Dalamud.Game.Text.SeStringHandling.Payloads`) is built at `MapLinkOpener.cs:15`. |

## INotificationManager

| Call | Location | Why |
|---|---|---|
| `AddNotification` + `Click` + `DismissNow` | `SoloDutyNotifier.cs:41`, `:49`, `:28`/`:53` | Solo-duty toast; click opens the duty lookup, and dispose dismisses so a stale toast can't click into a disposed window. |
| `AddNotification` | `Windows/ConfigWindow.cs:120` | Confirm "Clear wiki cache" completed (async, so a toast rather than inline text). |

## ITextureProvider

| Call | Location | Why |
|---|---|---|
| `GetFromFile(LogoPath).GetWrapOrDefault()` | `MainWindow.cs:204` | Draw the bundled logo in the brand strip; skipped gracefully if missing. |
| `GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty()` | `Windows/MainWindow.Results.cs:1240` | Render game icons (items, mounts, minions) in result rows. |

## IPluginLog

Warnings and errors only: unreadable stored config (`Plugin.cs:62`), cache-store warnings (`Plugin.cs:67`), warm-up failure (`Plugin.cs:117`), command-name collision (`Plugin.cs:185`), draw-loop crash recovery (`MainWindow.cs:185`), failed search (`MainWindow.cs:509`).

## Windowing (Dalamud.Interface.Windowing)

| Usage | Location | Why |
|---|---|---|
| `WindowSystem("Wikiway")` + `AddWindow` ×3 + `RemoveAllWindows` | `Plugin.cs:45`, `:124-126`, `:157` | Standard Dalamud window management for the main, settings, and tutorial windows. |
| `MainWindow : Window` (with `SizeConstraints`, `OnOpen`, `PreDraw`/`PostDraw` theme push/pop) | `MainWindow.cs:19`, `:56`, `:93`, `:95-130` | Main search UI. |
| `ConfigWindow : Window` | `ConfigWindow.cs:12` | Settings UI. |
| `TutorialWindow : Window` (`OnClose` marks the tour seen) | `TutorialWindow.cs:8`, `:58` | First-run tour. |
