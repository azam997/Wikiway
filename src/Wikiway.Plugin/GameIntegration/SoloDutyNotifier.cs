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

    public SoloDutyNotifier(MainWindow mainWindow, IGameDataStore store, Configuration config)
    {
        this.mainWindow = mainWindow;
        this.store = store;
        this.config = config;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose() => Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;

    // Fires at zone-in, before the commence dialog - unlike IDutyState.DutyStarted,
    // which only fires once the barrier drops.
    private void OnTerritoryChanged(uint territoryId)
    {
        if (!config.SoloDutyToastEnabled)
            return;

        var duty = store.FindDutyByTerritory(territoryId);
        if (duty is not { Solo: true })
            return;

        var notification = Plugin.NotificationManager.AddNotification(new Notification
        {
            Title = "Wikiway",
            Content = $"{duty.Name} — click to open the duty guide.",
            Type = NotificationType.Info,
            InitialDuration = TimeSpan.FromSeconds(10),
        });

        notification.Click += args =>
        {
            mainWindow.SubmitQuery(duty.Name, SearchCategory.Duties);
            mainWindow.IsOpen = true;
            args.Notification.DismissNow();
        };
    }
}
