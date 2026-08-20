using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.ContextMenu;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Plugin.Windows;

namespace Wikiway.Plugin.GameIntegration;

internal sealed class ContextMenuIntegration : IDisposable
{
    // GameGui.HoveredItem is a raw id with item flags folded in: collectables
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

            case MenuTargetDefault when args.AddonName == "ChatLog":
            {
                // The chat context menu target doesn't carry the item link; the
                // hovered-item state does, with the HQ or collectable flag
                // folded into the id. Event items live in a different sheet.
                var hovered = Plugin.GameGui.HoveredItem;
                if (hovered is not 0 and < EventItemIdBase)
                {
                    var baseId = (uint)(hovered switch
                    {
                        >= HqItemIdOffset => hovered - HqItemIdOffset,
                        >= CollectableItemIdOffset => hovered - CollectableItemIdOffset,
                        _ => hovered,
                    });
                    AddLookup(args, store.GetItemName(baseId), SearchCategory.Items);
                }

                break;
            }

            case MenuTargetDefault { TargetObject: { ObjectKind: ObjectKind.EventNpc } npc }:
                AddLookup(args, store.GetNpcName(npc.BaseId) ?? npc.Name.TextValue, SearchCategory.Npcs);
                break;
        }
    }

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
                mainWindow.SubmitQuery(name, category);
                mainWindow.IsOpen = true;
            },
        });
    }
}
