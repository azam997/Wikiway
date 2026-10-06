# Dalamud API and game-struct usage

This doc outlines every place Wikiway touches the Dalamud API or FFXIV game memory (FFXIVClientStructs), with where and why. Only `Wikiway.Plugin` touches either; `Wikiway.Core` and `Wikiway.GameData` are Dalamud-free (`Wikiway.GameData` reads sheets through the Lumina `GameData` handle that `Plugin` passes in). Line numbers are current as of 2026-09-01. This does not include imGui.

AI assisted in generation, used as a tool to help myself keep track of and review stress points.

## Service injection

All Dalamud services arrive via `[PluginService]` static properties on `Plugin` (`src/Wikiway.Plugin/Plugin.cs:26-35`): `IDalamudPluginInterface`, `ICommandManager`, `IDataManager`, `IGameGui`, `IPluginLog`, `IContextMenu`, `IClientState`, `INotificationManager`, `ITextureProvider`, `IAetheryteList`. Everything below reaches them as `Plugin.<Service>`.

## IDalamudPluginInterface

| Call | Location | Why |
|---|---|---|
| `GetPluginConfig()` | `Plugin.cs:206` | Load the saved `Configuration` at startup, inside a try/catch: Dalamud deserializes with no error handling of its own, so an unreadable file falls back to defaults instead of failing the constructor. |
| `ConfigFile` | `Plugin.cs:220` | Copy an unreadable config file to `.corrupt` before the next save replaces it with defaults. |
| `UiBuilder.FontAtlas` | `Plugin.cs:67` | Hand the atlas to `Fonts` so the plugin can build its own font sizes. |
| `GetPluginConfigDirectory()` | `Plugin.cs:70` | Root the on-disk wiki response cache under the plugin's config dir. |
| `UiBuilder.Draw += / -=` | `Plugin.cs:145`, `Plugin.cs:166` | Drive `WindowSystem.Draw` every frame (also the plugin's main-thread anchor). |
| `UiBuilder.OpenMainUi += / -=` | `Plugin.cs:146`, `Plugin.cs:167` | Open the main window from the plugin installer's button. |
| `UiBuilder.OpenConfigUi += / -=` | `Plugin.cs:147`, `Plugin.cs:168` | Open the settings window from the installer's cog. |
| `SavePluginConfig(this)` | `Configuration.cs:25` | Persist settings whenever a config value changes. |
| `AssemblyLocation.DirectoryName` | `MainWindow.cs:172` | Locate the bundled `images/logo64.png` next to the plugin assembly. |
| `GetIpcSubscriber<uint, byte, bool>("Teleport")` | `GameIntegration/TeleportService.cs:19` | Subscribe to the teleport gate the Teleporter plugin provides (see the IPC section). Subscribing to an absent gate is legal; only invoking it throws. |
| `InstalledPlugins` (`IExposedPlugin.IsLoaded`, `.InternalName`) | `TeleportService.cs:35` | Decide whether a Teleport button can appear at all: Teleporter must be loaded. Re-read every ~2s from the draw loop, not per button. |

`Configuration` implements `IPluginConfiguration` (`Configuration.cs:7`) so Dalamud can serialize it.

## IPC (Dalamud.Plugin.Ipc)

Wikiway provides no IPC; it only consumes Teleporter's teleport gate, a `Func<uint aetheryteId, byte subIndex, bool>`, and never calls the game's own teleport function.

| Call | Location | Why |
|---|---|---|
| `ICallGateSubscriber<uint, byte, bool>.InvokeFunc(id, 0)` | `TeleportService.cs:86` | Ask Teleporter, via its `Teleport` gate, to teleport to a result's nearest aetheryte. |
| `catch (IpcNotReadyError)` | `TeleportService.cs:89` | The gate has no provider; toast that Teleporter is needed. |
| `catch (TargetInvocationException)` | `TeleportService.cs:93` | Dalamud invokes the provider through `DynamicInvoke` and wraps nothing, so a throw inside Teleporter arrives as this, not as an `IpcError`. Logged; the provider counts as present and has reported the failure itself. |
| `catch (IpcError)` | `TeleportService.cs:69` | Any other IPC failure (type mismatch after a provider update) is logged, never rethrown into the draw loop. |
| `catch (Exception)` | `TeleportService.cs:73` | Last resort: this runs inside a draw-loop click handler and nothing may escape into the frame. |

## ICommandManager

| Call | Location | Why |
|---|---|---|
| `AddHandler(name, new CommandInfo(OnCommand))` | `Plugin.cs:234` (called for `/wikiway`, `/wway`, `/ww` at `Plugin.cs:139-143`) | Register the chat commands, handle conflicts |
| `RemoveHandler` ×3 | `Plugin.cs:170-172` | Unregister on dispose. |

## IDataManager (Lumina sheets)

| Call | Location | Why |
|---|---|---|
| `DataManager.GameData` | `Plugin.cs:84` | Hand the Lumina handle to `LuminaGameDataStore`. Every sheet read in `Wikiway.GameData` (items, NPCs, quests, duties, acquisition sources - shops, gathering, fishing, GC seal shops, retainer ventures - the collection kinds: mounts/minions, orchestrion rolls, Triple Triad cards, and emotes with their teaching items via the reverse ItemAction index - the item usage block: reverse ingredient counts, custom deliveries, collectable turn-ins, treasure map spots, materia stats, and food/medicine effects - the standalone kinds: sightseeing vistas, hunt marks, per-zone aether current quests, FATEs, and levequests - plus the nearest-aetheryte join: `MapMarker` subrows keyed by `Map.MapMarkerRange` for aetheryte and aethernet-shard positions, `TerritoryType.Aetheryte` as the per-zone fallback) flows through this. |
| `GetExcelSheet<Quest>()` | `GameIntegration/ActiveQuestReader.cs:32` | Resolve journal quest ids (`0x10000 + QuestId`) to display names and levels. |
| `GetExcelSheet<Quest>()` | `GameIntegration/QuestProgressTracker.cs:59` | Enumerate all quest row ids once, to scan the completion bitmap against. |

## FFXIVClientStructs (unsafe game memory)

Game structs are read in two places, both on the game main thread (see the thread-model note in `MapLinkOpener.cs`): `FFXIVClientStructs.FFXIV.Client.Game.QuestManager` by the two quest readers, which run from the draw loop, and five UI agents by `ContextMenuIntegration`, which runs from the context-menu callback. All reads are read-only.

| Call | Location | Why |
|---|---|---|
| `QuestManager.Instance()` | `GameIntegration/ActiveQuestReader.cs:21` | Get the live journal to list active quests for the quest picker. |
| `manager->TrackedQuests` | `ActiveQuestReader.cs:26` | Mark which journal quests the player has focused, so they sort first. |
| `manager->NormalQuests` | `ActiveQuestReader.cs:33` | Enumerate accepted quests (id, hidden flag) for the picker list. |
| `QuestManager.Addresses.IsQuestComplete.Value` | `GameIntegration/QuestProgressTracker.cs:38` | Null-check the generated `[MemberFunction]` pointer before calling it: an unresolved signature after a game patch would be a jump to address 0. A null snapshot fails gating open, with a one-time warning. |
| `QuestManager.Instance()` | `QuestProgressTracker.cs:52` | Null/logged-out guard before snapshotting progress. |
| `QuestManager.IsQuestComplete(ushort)` (static) | `QuestProgressTracker.cs:74` | Read the completed-quest bitmap to build the spoiler-gating snapshot. Missing from the ClientStructs XML docs; verified present on this build by reflection and in-game. |
| `manager->NormalQuests` | `QuestProgressTracker.cs:79` | Record accepted (in-journal) quests, so their names aren't treated as spoilers. |
| `AgentRecipeNote.Instance()->ContextMenuResultItemId` | `GameIntegration/ContextMenuIntegration.cs:75-76` | Item behind a right-click in the crafting log (`RecipeNote`). Its menu carries no item and recipe rows show no tooltip, so `HoveredItem` can't stand in. |
| `AgentRecipeItemContext.Instance()->ResultItemId` | `ContextMenuIntegration.cs:78-79` | Same, for the recipe tree, material list and product list windows. |
| `AgentGatheringNote.Instance()->ContextMenuItemId` | `ContextMenuIntegration.cs:81-82` | Same, for the gathering log. |
| `AgentContentsTimer.Instance()->ContextMenuItemId` | `ContextMenuIntegration.cs:84-85` | Same, for the timers window (`ContentsInfoDetail`). |
| `AgentDailyQuestSupply.Instance()->ContextMenuItemId` | `ContextMenuIntegration.cs:87-88` | Same, for custom deliveries. All five agent fields were checked by reflection against the installed FFXIVClientStructs build; each `Instance()` is null-checked, and the ids go through the same HQ/collectable/event-item decoding as `HoveredItem` (`:94-100`), so an id the Item sheet can't name adds no entry. |

## IClientState

| Call | Location | Why |
|---|---|---|
| `IsLoggedIn` | `ActiveQuestReader.cs:18`, `QuestProgressTracker.cs:53`, `MapLinkOpener.cs:17`, `TeleportService.cs:39`, `:54` | Skip journal/progress reads while logged out (gating fails open); skip map-link opens at character select and the DC-travel lobby, where the map agent isn't initialized; treat the aetheryte list as empty and refuse teleport calls while logged out. |
| `TerritoryChanged += / -=` | `GameIntegration/SoloDutyNotifier.cs:20`, `:25` | Detect zoning into a solo duty (fires at zone-in, before the commence dialog unlike `IDutyState.DutyStarted`). |
| `Logout += / -=` | `Windows/MainWindow.cs:54`, `:59` | Clear per-character spoiler reveals; the next login may be an alt. |

## IAetheryteList

| Call | Location | Why |
|---|---|---|
| `Length` + indexer, `IAetheryteEntry.AetheryteId` | `TeleportService.cs:41-45` | Snapshot the aetherytes the character has attuned to (the Teleport window's list) every ~2s from the draw loop. A Teleport button only appears for attuned aetherytes, so a click can't fail on an unvisited one. Housing entries share their district aetheryte's id and are not distinguished. |

## IContextMenu

| Call | Location | Why |
|---|---|---|
| `OnMenuOpened += / -=` | `GameIntegration/ContextMenuIntegration.cs:31`, `:34` | Hook every game context menu to offer "Look up on Wikiway". |
| `args.Target` matching (`MenuTargetInventory`, `MenuTargetDefault`, `ObjectKind.EventNpc`, `IBattleNpc.BattleNpcKind == BattleNpcSubKind.Combatant`) | `ContextMenuIntegration.cs:41-60` | Decide which menus get the entry: inventory items, items in the windows listed under FFXIVClientStructs (picked by `args.AddonName`, `:70-91`), event NPCs, and enemies. |
| `args.AddMenuItem(new MenuItem {...})` | `ContextMenuIntegration.cs:107` | Insert the lookup entry with the `W` prefix glyph. |

## IGameGui

| Call | Location | Why |
|---|---|---|
| `HoveredItem` | `ContextMenuIntegration.cs:73` | The chat context menu target doesn't carry the item id; the hovered-item state does (with HQ/collectable offsets folded in, decoded at `:94-100`). |
| `OpenMapWithMapLink(MapLinkPayload)` | `GameIntegration/MapLinkOpener.cs:21` | Open the in-game map with a flag at a result's location. The `MapLinkPayload` (`Dalamud.Game.Text.SeStringHandling.Payloads`) is built at `MapLinkOpener.cs:20`. The territory id in it is the map's public territory, not a quest-scene copy (`LuminaGameDataStore.PublicTerritory`), so the flag also renders in-world when the player is in that zone. |

## INotificationManager

| Call | Location | Why |
|---|---|---|
| `AddNotification` + `Click` + `DismissNow` | `SoloDutyNotifier.cs:40`, `:48`, `:27`/`:52`/`:56` | Solo-duty toast; click opens the duty lookup, a new toast dismisses an overlapped predecessor, and dispose dismisses so a stale toast can't click into a disposed window. |
| `AddNotification` | `Windows/ConfigWindow.cs:180` | Confirm "Clear wiki cache" completed (async, so a toast rather than inline text). |
| `AddNotification` | `TeleportService.cs:61` | Tell the user a Teleport click found no provider (only reachable if Teleporter unloads between the button appearing and the click). |

## ITextureProvider

| Call | Location | Why |
|---|---|---|
| `GetFromFile(LogoPath).GetWrapOrDefault()` | `MainWindow.cs:180` | Draw the bundled logo in the brand strip; skipped gracefully if missing. |
| `GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty()` | `Windows/MainWindow.Results.cs:2326` | Render game icons (items, mounts, minions, and the teaching items on orchestrion/Triple Triad rows) in result rows. |

## IPluginLog

Warnings and errors only: unreadable stored config (`Plugin.cs:66`, `:210`, `:226`), cache-store warnings (`Plugin.cs:71`), warm-up failure (`Plugin.cs:122`), command-name collision (`Plugin.cs:235`), draw-loop crash recovery (`MainWindow.cs:164`), failed search (`MainWindow.cs:467`), unresolved `IsQuestComplete` signature (`QuestProgressTracker.cs:43`), teleport IPC or provider failure (`TeleportService.cs:71`, `:76`, `:99`).

## Windowing (Dalamud.Interface.Windowing)

| Usage | Location | Why |
|---|---|---|
| `WindowSystem("Wikiway")` + `AddWindow` ×3 + `RemoveAllWindows` | `Plugin.cs:47`, `:129-131`, `:174` | Standard Dalamud window management for the main, settings, and tutorial windows. |
| `MainWindow : Window` (with `SizeConstraints`, `OnOpen`, `PreDraw`/`PostDraw` theme push/pop) | `MainWindow.cs:20`, `:46`, `:77`, `:79-114` | Main search UI. |
| `ConfigWindow : Window` | `ConfigWindow.cs:12` | Settings UI. |
| `TutorialWindow : Window` (`OnClose` marks the tour seen) | `TutorialWindow.cs:8`, `:58` | First-run tour. |

## ImGui internals (Dalamud.Bindings.ImGui.ImGuiP)

ImGui drawing itself is out of scope here, but two internal-API calls are load-bearing for crash safety:

| Call | Location | Why |
|---|---|---|
| `ImGuiP.GetCurrentWindow()` | `Ui/ImGuiUnwind.cs:16`, `:25` | Mark the window `MainWindow.Draw` was entered in, and find popups or children still open when it throws. |
| `ImGuiP.ErrorCheckEndWindowRecover(null)` | `Ui/ImGuiUnwind.cs:29`, `:36` | Pop every colour, style var, font, group, tab bar and ID pushed since the window began. Dalamud's `WindowHost` catches a throwing `Draw` but restores nothing, and its ImGui build never runs end-of-frame recovery, so without this the strays would leak into every window drawn afterwards, other plugins included, until reload. Called from `MainWindow.cs:167`. |
