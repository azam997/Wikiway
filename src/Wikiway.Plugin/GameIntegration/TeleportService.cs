using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace Wikiway.Plugin.GameIntegration;

// Teleporting goes through another plugin's IPC rather than the game's Telepo
// directly: Teleporter ("Teleport") and Lifestream ("Lifestream.Teleport")
// both expose (aetheryteId, subIndex) -> bool. Lifestream's gate name and
// signature were verified by reflection over its shipped assembly (EzIPC
// prefixes the method name with the plugin name).
internal sealed class TeleportService
{
    private const string TeleporterInternalName = "TeleporterPlugin";
    private const string LifestreamInternalName = "Lifestream";
    private const long StaleMs = 2000;

    private readonly ICallGateSubscriber<uint, byte, bool> teleporter =
        Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");
    private readonly ICallGateSubscriber<uint, byte, bool> lifestream =
        Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");

    private HashSet<uint> attuned = [];
    private bool providerLoaded;
    private long lastRefresh = -StaleMs;

    public bool CanTeleportTo(uint aetheryteId) =>
        providerLoaded && attuned.Contains(aetheryteId);

    // Draw-loop only (game main thread): the aetheryte list is client state.
    public void RefreshIfStale()
    {
        if (Environment.TickCount64 - lastRefresh < StaleMs)
            return;

        lastRefresh = Environment.TickCount64;
        providerLoaded = Plugin.PluginInterface.InstalledPlugins.Any(p =>
            p.IsLoaded && p.InternalName is TeleporterInternalName or LifestreamInternalName);

        var set = new HashSet<uint>();
        if (Plugin.ClientState.IsLoggedIn)
        {
            var list = Plugin.AetheryteList;
            for (var i = 0; i < list.Length; i++)
            {
                if (list[i] is { } entry)
                    set.Add(entry.AetheryteId);
            }
        }

        attuned = set;
    }

    public void TeleportTo(uint aetheryteId)
    {
        if (!Plugin.ClientState.IsLoggedIn)
            return;

        try
        {
            if (!Invoke(teleporter, aetheryteId) && !Invoke(lifestream, aetheryteId))
            {
                Plugin.NotificationManager.AddNotification(new Notification
                {
                    Title = "Wikiway",
                    Content = "Teleporting needs the Teleporter or Lifestream plugin.",
                    Type = NotificationType.Warning,
                });
            }
        }
        catch (IpcError e)
        {
            Plugin.Log.Warning(e, "Teleport IPC call failed.");
        }
    }

    // False only when the gate has no provider; the provider's own result is
    // not surfaced because both plugins already report failures in chat.
    private static bool Invoke(ICallGateSubscriber<uint, byte, bool> gate, uint aetheryteId)
    {
        try
        {
            gate.InvokeFunc(aetheryteId, 0);
            return true;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
    }
}
