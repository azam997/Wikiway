using Dalamud.Game.Text.SeStringHandling.Payloads;
using Wikiway.Core.Models;

namespace Wikiway.Plugin.GameIntegration;

internal static class MapLinkOpener
{
    // Thread model, stated once for the whole plugin: UiBuilder.Draw runs on
    // the game main thread - the same thread as Framework.Update - so game
    // calls made from click handlers need no RunOnFrameworkThread marshalling.
    // ActiveQuestReader and QuestProgressTracker rely on the same invariant
    // for their unsafe QuestManager reads.
    public static void Open(MapLocation location)
    {
        var link = new MapLinkPayload(location.TerritoryTypeId, location.MapId, location.MapX, location.MapY);
        Plugin.GameGui.OpenMapWithMapLink(link);
    }
}
