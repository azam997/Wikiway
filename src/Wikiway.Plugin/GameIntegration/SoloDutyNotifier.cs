using System;
using Dalamud.Interface.ImGuiNotification;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Plugin.Windows;

namespace Wikiway.Plugin.GameIntegration;

internal sealed class SoloDutyNotifier : IDisposable
{
    private readonly MainWindow mainWindow;
    private readonly IGameDataStore store;
    private readonly Configuration config;
    private IActiveNotification? activeNotification;

    public SoloDutyNotifier(MainWindow mainWindow, IGameDataStore store, Configuration config)
    {
        this.mainWindow = mainWindow;
        this.store = store;
        this.config = config;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        // A toast outliving the plugin would click into a disposed window.
        activeNotification?.DismissNow();
    }

    // Fires at zone-in, before the commence dialog - unlike IDutyState.DutyStarted,
    // which only fires once the barrier drops.
    private void OnTerritoryChanged(uint territoryId)
    {
        if (!config.SoloDutyToastEnabled)
            return;

        if (store.FindSoloDutyName(territoryId) is not { } dutyName)
            return;

        var notification = Plugin.NotificationManager.AddNotification(new Notification
        {
            Title = "Wikiway",
            Content = $"{dutyName} — click to look this duty up.",
            Type = NotificationType.Info,
            InitialDuration = TimeSpan.FromSeconds(10),
        });

        notification.Click += args =>
        {
            mainWindow.SubmitQuery(dutyName, SearchCategory.Other);
            mainWindow.IsOpen = true;
            args.Notification.DismissNow();
        };
        // Only the tracked toast is dismissed on Dispose; an overlapped
        // predecessor would leak a click into a disposed window.
        activeNotification?.DismissNow();
        activeNotification = notification;
    }
}
