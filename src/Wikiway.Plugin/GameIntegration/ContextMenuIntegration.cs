using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.ContextMenu;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Plugin.Windows;

namespace Wikiway.Plugin.GameIntegration;

internal sealed class ContextMenuIntegration : IDisposable
{
    private const ulong HqItemIdOffset = 1_000_000;

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
                AddLookup(args, store.GetItem(item.BaseItemId)?.Name, SearchCategory.Items);
                break;

            case MenuTargetDefault when args.AddonName == "ChatLog":
            {
                // The chat context menu target doesn't carry the item link; the
                // hovered-item state does, with the HQ flag folded into the id.
                var hovered = Plugin.GameGui.HoveredItem;
                if (hovered != 0)
                {
                    var baseId = (uint)(hovered >= HqItemIdOffset ? hovered - HqItemIdOffset : hovered);
                    AddLookup(args, store.GetItem(baseId)?.Name, SearchCategory.Items);
                }

                break;
            }

            case MenuTargetDefault { TargetObject: { ObjectKind: ObjectKind.EventNpc } npc }:
                AddLookup(args, store.GetNpc(npc.BaseId)?.Name ?? npc.Name.TextValue, SearchCategory.Npcs);
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
