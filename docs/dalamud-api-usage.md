# Dalamud API and game-struct usage

This doc outlines every place Wikiway touches the Dalamud API or FFXIV game memory (FFXIVClientStructs), with where and why. Only `Wikiway.Plugin` touches either; `Wikiway.Core` and `Wikiway.GameData` are Dalamud-free (`Wikiway.GameData` reads sheets through the Lumina `GameData` handle that `Plugin` passes in). Locations are given as file and member (method, property or field) rather than line numbers, so they stay valid as the code moves; paths are relative to `src/Wikiway.Plugin/`. This does not include imGui. For how these pieces fit together, see [how-it-works.md](how-it-works.md).

AI assisted in generation, used as a tool to help myself keep track of and review stress points.

## Service injection

All Dalamud services arrive via `[PluginService]` static properties on `Plugin` (`Plugin.cs`, top of the class): `IDalamudPluginInterface`, `ICommandManager`, `IDataManager`, `IGameGui`, `IPluginLog`, `IContextMenu`, `IClientState`, `INotificationManager`, `ITextureProvider`, `IAetheryteList`. Everything below reaches them as `Plugin.<Service>`.

## IDalamudPluginInterface

| Call | Location | Why |
|---|---|---|
| `GetPluginConfig()` | `Plugin.cs` → `LoadConfiguration` | Load the saved `Configuration` at startup, inside a try/catch: Dalamud deserializes with no error handling of its own, so an unreadable file falls back to defaults instead of failing the constructor. |
| `ConfigFile` | `Plugin.cs` → `BackUpConfigFile` | Copy an unreadable config file to `.corrupt` before the next save replaces it with defaults. |
| `UiBuilder.FontAtlas` | `Plugin.cs` → constructor | Hand the atlas to `Fonts` so the plugin can build its own font sizes. |
| `GetPluginConfigDirectory()` | `Plugin.cs` → constructor | Root the on-disk wiki response cache under the plugin's config dir. |
| `UiBuilder.Draw += / -=` | `Plugin.cs` → constructor / `Dispose` | Drive `WindowSystem.Draw` every frame. This runs on the game's main thread, which is why the plugin's game reads can happen in UI code without extra marshalling. |
| `UiBuilder.OpenMainUi += / -=` | `Plugin.cs` → constructor / `Dispose` | Open the main window from the plugin installer's button. |
| `UiBuilder.OpenConfigUi += / -=` | `Plugin.cs` → constructor / `Dispose` | Open the settings window from the installer's cog. |
| `SavePluginConfig(this)` | `Configuration.cs` → `Save` | Persist settings whenever a config value changes. |
| `AssemblyLocation.DirectoryName` | `Windows/MainWindow.cs` → `LogoPath` | Locate the bundled `images/logo64.png` next to the plugin assembly. |
| `GetIpcSubscriber<uint, byte, bool>("Teleport")` | `GameIntegration/TeleportService.cs` → `teleporter` field | Subscribe to the teleport gate the Teleporter plugin provides (see the IPC section). Subscribing to an absent gate is legal; only invoking it throws. |
| `InstalledPlugins` (`IExposedPlugin.IsLoaded`, `.InternalName`) | `TeleportService.cs` → `RefreshIfStale` | Decide whether a Teleport button can appear at all: Teleporter must be loaded. Re-read every ~2s from the draw loop, not per button. |

`Configuration` implements `IPluginConfiguration` (`Configuration.cs`) so Dalamud can serialize it.

## IPC (Dalamud.Plugin.Ipc)

Wikiway provides no IPC; it only consumes Teleporter's teleport gate, a `Func<uint aetheryteId, byte subIndex, bool>`, and never calls the game's own teleport function.

| Call | Location | Why |
|---|---|---|
| `ICallGateSubscriber<uint, byte, bool>.InvokeFunc(id, 0)` | `TeleportService.cs` → `Invoke` | Ask Teleporter, via its `Teleport` gate, to teleport to a result's nearest aetheryte. |
| `catch (IpcNotReadyError)` | `TeleportService.cs` → `Invoke` | The gate has no provider; toast that Teleporter is needed. |
| `catch (TargetInvocationException)` | `TeleportService.cs` → `Invoke` | Dalamud invokes the provider through `DynamicInvoke` and wraps nothing, so a throw inside Teleporter arrives as this, not as an `IpcError`. Logged; the provider counts as present and has reported the failure itself. |
| `catch (IpcError)` | `TeleportService.cs` → `TeleportTo` | Any other IPC failure (type mismatch after a provider update) is logged, never rethrown into the draw loop. |
| `catch (Exception)` | `TeleportService.cs` → `TeleportTo` | Last resort: this runs inside a draw-loop click handler and nothing may escape into the frame. |

## ICommandManager

| Call | Location | Why |
|---|---|---|
| `AddHandler(name, new CommandInfo(OnCommand))` | `Plugin.cs` → `AddCommand` (called from the constructor for `/wikiway`, `/wway`, `/ww`) | Register the chat commands, handle conflicts |
| `RemoveHandler` ×3 | `Plugin.cs` → `Dispose` | Unregister on dispose. |

## IDataManager (Lumina sheets)

| Call | Location | Why |
|---|---|---|
| `DataManager.GameData` | `Plugin.cs` → constructor | Hand the Lumina handle to `LuminaGameDataStore`. Every sheet read in `Wikiway.GameData` flows through this. That covers items, NPCs, quests and duties; acquisition sources (shops, gathering, fishing, GC seal shops, retainer ventures); the collection kinds (mounts/minions, orchestrion rolls, Triple Triad cards, and emotes with their teaching items via the reverse ItemAction index); the item usage block (reverse ingredient counts, custom deliveries, collectable turn-ins, treasure map spots, materia stats, food/medicine effects); the standalone kinds (sightseeing vistas, hunt marks, per-zone aether current quests, FATEs, levequests); and the nearest-aetheryte join (`MapMarker` subrows keyed by `Map.MapMarkerRange` for aetheryte and aethernet-shard positions, `TerritoryType.Aetheryte` as the per-zone fallback). The store is split into one file per feature; see the table in how-it-works.md. |
| `GetExcelSheet<Quest>()` | `GameIntegration/ActiveQuestReader.cs` → `Read` | Resolve journal quest ids (`0x10000 + QuestId`) to display names and levels. |
| `GetExcelSheet<Quest>()` | `GameIntegration/QuestProgressTracker.cs` → `Refresh` | Enumerate all quest row ids once, to scan the completion bitmap against. |

## FFXIVClientStructs (unsafe game memory)

Game structs are read in two places, both on the game main thread (see the thread-model note in `MapLinkOpener.cs`). `FFXIVClientStructs.FFXIV.Client.Game.QuestManager` is read by the two quest readers, which run from the draw loop. Five UI agents are read by `ContextMenuIntegration`, which runs from the context-menu callback. All reads are read-only.

| Call | Location | Why |
|---|---|---|
| `QuestManager.Instance()` | `GameIntegration/ActiveQuestReader.cs` → `Read` | Get the live journal to list active quests for the quest picker. |
| `manager->TrackedQuests` | `ActiveQuestReader.cs` → `Read` | Mark which journal quests the player has focused, so they sort first. |
| `manager->NormalQuests` | `ActiveQuestReader.cs` → `Read` | Enumerate accepted quests (id, hidden flag) for the picker list. |
| `QuestManager.Addresses.IsQuestComplete.Value` | `GameIntegration/QuestProgressTracker.cs` → `Refresh` | Null-check the generated `[MemberFunction]` pointer before calling it: an unresolved signature after a game patch would be a jump to address 0. If it's unresolved, there's no progress snapshot, so spoiler hiding turns off and a one-time warning is logged. |
| `QuestManager.Instance()` | `QuestProgressTracker.cs` → `Refresh` | Null/logged-out guard before snapshotting progress. |
| `QuestManager.IsQuestComplete(ushort)` (static) | `QuestProgressTracker.cs` → `Refresh` | Read the completed-quest bitmap to build the progress snapshot used for spoiler hiding. Missing from the ClientStructs XML docs; verified present on this build by reflection and in-game. |
| `manager->NormalQuests` | `QuestProgressTracker.cs` → `Refresh` | Record accepted (in-journal) quests, so their names aren't treated as spoilers. |
| `AgentRecipeNote.Instance()->ContextMenuResultItemId` | `GameIntegration/ContextMenuIntegration.cs` → `ItemIdFromWindow` | Item behind a right-click in the crafting log (`RecipeNote`). Its menu carries no item and recipe rows show no tooltip, so `HoveredItem` can't stand in. |
| `AgentRecipeItemContext.Instance()->ResultItemId` | `ContextMenuIntegration.cs` → `ItemIdFromWindow` | Same, for the recipe tree, material list and product list windows. |
| `AgentGatheringNote.Instance()->ContextMenuItemId` | `ContextMenuIntegration.cs` → `ItemIdFromWindow` | Same, for the gathering log. |
| `AgentContentsTimer.Instance()->ContextMenuItemId` | `ContextMenuIntegration.cs` → `ItemIdFromWindow` | Same, for the timers window (`ContentsInfoDetail`). |
| `AgentDailyQuestSupply.Instance()->ContextMenuItemId` | `ContextMenuIntegration.cs` → `ItemIdFromWindow` | Same, for custom deliveries. All five agent fields were checked by reflection against the installed FFXIVClientStructs build. Each `Instance()` is null-checked, and the ids go through the same HQ/collectable/event-item decoding as `HoveredItem` (`BaseItemId`), so an id the Item sheet can't name adds no entry. |

## IClientState

| Call | Location | Why |
|---|---|---|
| `IsLoggedIn` | `ActiveQuestReader.cs` → `Read`; `QuestProgressTracker.cs` → `Refresh`; `MapLinkOpener.cs` → `Open`; `TeleportService.cs` → `RefreshIfStale`, `TeleportTo` | Skip journal/progress reads while logged out (spoiler hiding turns off). Skip map-link opens at character select and the DC-travel lobby, where the map agent isn't initialized. Treat the aetheryte list as empty and refuse teleport calls while logged out. |
| `TerritoryChanged += / -=` | `GameIntegration/SoloDutyNotifier.cs` → constructor / `Dispose` | Detect zoning into a solo duty (fires at zone-in, before the commence dialog unlike `IDutyState.DutyStarted`). |
| `Logout += / -=` | `Windows/MainWindow.cs` → constructor / `Dispose` | Clear per-character spoiler reveals; the next login may be an alt. |

## IAetheryteList

| Call | Location | Why |
|---|---|---|
| `Length` + indexer, `IAetheryteEntry.AetheryteId` | `TeleportService.cs` → `RefreshIfStale` | Snapshot the aetherytes the character has attuned to (the Teleport window's list) every ~2s from the draw loop. A Teleport button only appears for attuned aetherytes, so a click can't fail on an unvisited one. Housing entries share their district aetheryte's id and are not distinguished. |

## IContextMenu

| Call | Location | Why |
|---|---|---|
| `OnMenuOpened += / -=` | `GameIntegration/ContextMenuIntegration.cs` → constructor / `Dispose` | Hook every game context menu to offer "Look up on Wikiway". |
| `args.Target` matching (`MenuTargetInventory`, `MenuTargetDefault`, `ObjectKind.EventNpc`, `IBattleNpc.BattleNpcKind == BattleNpcSubKind.Combatant`) | `ContextMenuIntegration.cs` → `OnMenuOpened` | Decide which menus get the entry: inventory items, items in the windows listed under FFXIVClientStructs (picked by `args.AddonName` in `ItemIdFromWindow`), event NPCs, and enemies. |
| `args.AddMenuItem(new MenuItem {...})` | `ContextMenuIntegration.cs` → `AddLookup` | Insert the lookup entry with the `W` prefix glyph. |

## IGameGui

| Call | Location | Why |
|---|---|---|
| `HoveredItem` | `ContextMenuIntegration.cs` → `ItemIdFromWindow` | The chat context menu target doesn't carry the item id; the hovered-item state does (with HQ/collectable offsets folded in, decoded in `BaseItemId`). |
| `OpenMapWithMapLink(MapLinkPayload)` | `GameIntegration/MapLinkOpener.cs` → `Open` | Open the in-game map with a flag at a result's location. The `MapLinkPayload` (`Dalamud.Game.Text.SeStringHandling.Payloads`) is built in the same method. The territory id in it is the map's public territory, not a quest-scene copy (`LuminaGameDataStore.PublicTerritory` in `Wikiway.GameData/LuminaGameDataStore.Locations.cs`), so the flag also renders in-world when the player is in that zone. |

## INotificationManager

| Call | Location | Why |
|---|---|---|
| `AddNotification` + `Click` + `DismissNow` | `SoloDutyNotifier.cs` → `OnTerritoryChanged`, `Dispose` | Solo-duty toast. A click opens the duty lookup, a new toast dismisses an overlapped predecessor, and dispose dismisses so a stale toast can't click into a disposed window. |
| `AddNotification` | `Windows/ConfigWindow.cs` → `Draw` | Confirm "Clear wiki cache" completed (async, so a toast rather than inline text). |
| `AddNotification` | `TeleportService.cs` → `TeleportTo` | Tell the user a Teleport click found no provider (only reachable if Teleporter unloads between the button appearing and the click). |

## ITextureProvider

| Call | Location | Why |
|---|---|---|
| `GetFromFile(LogoPath).GetWrapOrDefault()` | `Windows/MainWindow.cs` → `DrawBrandStrip` | Draw the bundled logo in the brand strip; skipped gracefully if missing. |
| `GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty()` | `Windows/MainWindow.RowParts.cs` → `DrawIconTile` | Render game icons (items, mounts, minions, and the teaching items on orchestrion/Triple Triad rows) in result rows. |

## IPluginLog

Warnings and errors only:

- `Plugin.cs`:
  - unreadable stored config (constructor, `LoadConfiguration`, `BackUpConfigFile`);
  - cache-store warnings (constructor, passed to `FileCacheStore`);
  - warm-up failure (constructor);
  - command-name collision (`AddCommand`).
- `MainWindow.cs`: draw-loop crash recovery (`Draw`) and failed search (`Harvest`).
- `QuestProgressTracker.cs` → `Refresh`: unresolved `IsQuestComplete` signature.
- `TeleportService.cs` → `TeleportTo`, `Invoke`: teleport IPC or provider failure.
- `BrowserOpener.cs` → `Open`: browser launch failure.

## Windowing (Dalamud.Interface.Windowing)

| Usage | Location | Why |
|---|---|---|
| `WindowSystem("Wikiway")` + `AddWindow` ×3 + `RemoveAllWindows` | `Plugin.cs` → `WindowSystem` field, constructor, `Dispose` | Standard Dalamud window management for the main, settings, and tutorial windows. |
| `MainWindow : Window` (with `SizeConstraints`, `OnOpen`, `PreDraw`/`PostDraw` theme push/pop) | `Windows/MainWindow.cs` | Main search UI. The result drawing is split across the `MainWindow.*.cs` partials. |
| `ConfigWindow : Window` | `Windows/ConfigWindow.cs` | Settings UI. |
| `TutorialWindow : Window` (`OnClose` marks the tour seen) | `Windows/TutorialWindow.cs` | First-run tour. |

## ImGui state (Dalamud.Interface.Utility.Raii)

ImGui drawing itself is out of scope here, but how pushed state is undone matters for crash safety:

| Usage | Location | Why |
|---|---|---|
| `ImRaii` scopes (`PushColor`, `PushStyle`, `PushId`, `PushIndent`, `Group`, `Disabled`, `TabBar`/`TabItem`, `Popup`, `Child`) and `using` on `IFontHandle.Push()` | `Windows/*`, `Ui/Widgets.cs` | Every color, style var, font, ID, group, tab bar and popup is pushed inside a `using`, so it is undone when the scope ends, including when a draw throws. Dalamud's `WindowHost` catches a throwing `Draw` but restores nothing, so manual pushes left behind would leak into every window drawn afterwards, other plugins included. The one draw-list channel split (`MainWindow.Results.cs` → `DrawRow`) is merged in a `finally`. |
