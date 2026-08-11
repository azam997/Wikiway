using Dalamud.Game.Text.SeStringHandling.Payloads;
using Wikiway.Core.Models;

namespace Wikiway.Plugin.GameIntegration;

internal static class MapLinkOpener
{
    public static void Open(MapLocation location)
    {
        var link = new MapLinkPayload(location.TerritoryTypeId, location.MapId, location.MapX, location.MapY);
        Plugin.Framework.RunOnFrameworkThread(() => Plugin.GameGui.OpenMapWithMapLink(link));
    }
}
