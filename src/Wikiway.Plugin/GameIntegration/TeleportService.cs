using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace Wikiway.Plugin.GameIntegration;

// Teleporting goes through the Teleporter plugin's IPC rather than the game's
// Telepo directly: its "Teleport" gate exposes (aetheryteId, subIndex) -> bool.
internal sealed class TeleportService
{
    private const string TeleporterInternalName = "TeleporterPlugin";
    private const long StaleMs = 2000;

    private readonly ICallGateSubscriber<uint, byte, bool> teleporter =
        Plugin.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Teleport");

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
            p.IsLoaded && p.InternalName is TeleporterInternalName);

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
            if (!Invoke(teleporter, aetheryteId))
            {
                Plugin.NotificationManager.AddNotification(new Notification
                {
                    Title = "Wikiway",
                    Content = "Teleporting needs the Teleporter plugin.",
                    Type = NotificationType.Warning,
                });
            }
        }
        catch (IpcError e)
        {
            Plugin.Log.Warning(e, "Teleport IPC call failed.");
        }
        catch (Exception e)
        {
            // This is a draw-loop click handler; nothing may escape it.
            Plugin.Log.Error(e, "Teleport call failed unexpectedly.");
        }
    }

    // False only when the gate has no provider; the provider's own result is
    // not surfaced because Teleporter already reports failures in chat.
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
        catch (TargetInvocationException e)
        {
            // Dalamud calls the provider through DynamicInvoke and wraps
            // nothing, so a throw inside Teleporter arrives as this rather
            // than as an IpcError. The provider exists and has reported the
            // failure itself.
            Plugin.Log.Warning(e.InnerException ?? e, "Teleport provider threw.");
            return true;
        }
    }
}
