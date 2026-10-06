using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Wikiway.Plugin.Windows;

namespace Wikiway.Plugin.GameIntegration;

// Adds "Look up on Wikiway" to the game's right-click menus: items (inventory,
// chat links, crafting and gathering logs, timers, custom deliveries), event
// NPCs and enemies. Menus open on the game thread, so only cheap single-row
// name reads happen here; the search itself runs later, off-thread.
internal sealed class ContextMenuIntegration : IDisposable
{
    // Item ids read from game UI state have item flags folded in: collectables
    // are Item row + 500,000, HQ is row + 1,000,000, and ids of 2,000,000 and
    // up are EventItem (key item) sheet rows the Item sheet can't resolve.
    private const ulong CollectableItemIdOffset = 500_000;
    private const ulong HqItemIdOffset = 1_000_000;
    private const ulong EventItemIdBase = 2_000_000;

    private readonly MainWindow mainWindow;
    private readonly IGameDataStore store;
    private readonly Configuration config;

    public ContextMenuIntegration(MainWindow mainWindow, IGameDataStore store, Configuration config)
    {
        this.mainWindow = mainWindow;
        this.store = store;
        this.config = config;
        Plugin.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => Plugin.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!config.ContextMenuEnabled)
            return;

        switch (args.Target)
        {
            case MenuTargetInventory { TargetItem: { ItemId: not 0 } item }:
                AddLookup(args, store.GetItemName(item.BaseItemId), SearchCategory.Items);
                break;

            case MenuTargetDefault when BaseItemId(ItemIdFromWindow(args.AddonName)) is { } itemId:
                AddLookup(args, store.GetItemName(itemId), SearchCategory.Items);
                break;

            case MenuTargetDefault { TargetObject: { ObjectKind: ObjectKind.EventNpc } npc }:
                AddLookup(args, store.GetNpcName(npc.BaseId) ?? npc.Name.TextValue, SearchCategory.Npcs);
                break;

            // Hunt marks resolve from game data; other enemies fall through
            // to the wiki. Neither has a filter, so the search is unprefixed.
            case MenuTargetDefault { TargetObject: IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant } enemy }:
                AddLookup(args, enemy.Name.TextValue, SearchCategory.All);
                break;
        }
    }

    // These windows' menus carry no item, so the id comes from where the game
    // parks the right-clicked item: the hovered-item state for chat links, the
    // window's agent otherwise. Agent field names are FFXIVClientStructs',
    // checked by reflection against the installed build. Menus open on the
    // game main thread, so the reads need no marshalling (see MapLinkOpener).
    private static unsafe ulong ItemIdFromWindow(string? addonName)
    {
        switch (addonName)
        {
            case "ChatLog":
                return Plugin.GameGui.HoveredItem;
            case "RecipeNote":
                var recipeNote = AgentRecipeNote.Instance();
                return recipeNote == null ? 0 : recipeNote->ContextMenuResultItemId;
            case "RecipeTree" or "RecipeMaterialList" or "RecipeProductList":
                var recipeItem = AgentRecipeItemContext.Instance();
                return recipeItem == null ? 0 : recipeItem->ResultItemId;
            case "GatheringNote":
                var gatheringNote = AgentGatheringNote.Instance();
                return gatheringNote == null ? 0 : gatheringNote->ContextMenuItemId;
            case "ContentsInfoDetail":
                var timers = AgentContentsTimer.Instance();
                return timers == null ? 0 : timers->ContextMenuItemId;
            case "DailyQuestSupply":
                var deliveries = AgentDailyQuestSupply.Instance();
                return deliveries == null ? 0 : deliveries->ContextMenuItemId;
            default:
                return 0;
        }
    }

    private static uint? BaseItemId(ulong raw) => raw switch
    {
        0 or >= EventItemIdBase => null,
        >= HqItemIdOffset => (uint)(raw - HqItemIdOffset),
        >= CollectableItemIdOffset => (uint)(raw - CollectableItemIdOffset),
        _ => (uint)raw,
    };

    // The click fills the search box with a prefixed query ("item: Iron
    // Ingot"), so the user can see what was searched.
    private void AddLookup(IMenuOpenedArgs args, string? name, SearchCategory category)
    {
        if (string.IsNullOrEmpty(name))
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = "Look up on Wikiway",
            PrefixChar = 'W',
            PrefixColor = 539,
            OnClicked = _ =>
            {
                mainWindow.SubmitQuery(QueryNormalizer.Prefixed(category, name));
                mainWindow.IsOpen = true;
            },
        });
    }
}
