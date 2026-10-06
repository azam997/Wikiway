using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Map positions: converts sheet coordinates to the in-game map's coordinates,
// fixes up quest-scene territory copies so map flags work, and finds the
// nearest aetheryte or aethernet shard for the Teleport button.
public sealed partial class LuminaGameDataStore
{
    // MapMarker.DataType: 3 keys an Aetheryte row, 4 an aethernet shard's
    // PlaceName. Aetheryte.Level[] is empty for every teleport aetheryte, so
    // the map markers are the only sheet-side source of their coordinates
    // (probed 7.3: 107/107 named aetherytes marked on their own map).
    private const byte MapMarkerAetheryte = 3;
    private const byte MapMarkerAethernet = 4;

    // A Level row (NPC, quest start, vista...) as map coordinates.
    private MapLocation? ToMapLocation(Level level)
    {
        var map = level.Map.ValueNullable;
        var territory = level.Territory.ValueNullable;
        if (map == null || territory == null)
            return null;

        // World Z is the north-south axis; map Y comes from it, not from world Y (height).
        var mapX = MapCoordConverter.ToMapCoord(level.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(level.Z, map.Value.SizeFactor, map.Value.OffsetY);

        return Locate(territory.Value, map.Value, mapX, mapY);
    }

    // Shared final step for every location: zone name, public territory id for
    // the map flag, and the nearest aetheryte.
    private MapLocation Locate(TerritoryType territory, Map map, float mapX, float mapY)
    {
        var zone = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        var territoryId = PublicTerritory(territory, map);
        return new MapLocation(territoryId, map.RowId, mapX, mapY, zone, NearestAetheryte(territoryId, map.RowId, mapX, mapY));
    }

    // Quest scenes place NPCs in private copies of a zone: separate
    // TerritoryType rows that share the public map and its place name (probed
    // 7.3: 405 of 446 mismatched NPC placements). A flag carrying the copy's
    // id never matches the territory the player stands in, so it is swapped
    // for the map's own territory when the names agree; sub-areas with their
    // own name (the Sanctum of the Twelve inside East Shroud) keep theirs.
    private static uint PublicTerritory(TerritoryType territory, Map map)
    {
        var owner = map.TerritoryType.ValueNullable;
        if (owner == null || owner.Value.RowId == 0 || owner.Value.RowId == territory.RowId)
            return territory.RowId;

        var placed = territory.PlaceName.ValueNullable?.Name.ExtractText();
        var owned = owner.Value.PlaceName.ValueNullable?.Name.ExtractText();
        return placed != null && placed == owned ? owner.Value.RowId : territory.RowId;
    }

    // Nearest point on the same map by map-coordinate distance; maps without
    // any marker (interiors, sub-zones like the Steps of Thal) fall back to
    // the zone's designated aetheryte, which TerritoryType names for every
    // town and field territory.
    private NearestAetheryte? NearestAetheryte(uint territoryId, uint mapId, float mapX, float mapY)
    {
        var zoneAetheryte = Sheet<TerritoryType>().GetRowOrDefault(territoryId)?.Aetheryte.ValueNullable;
        var zoneName = zoneAetheryte is { IsAetheryte: true } za ? za.PlaceName.ValueNullable?.Name.ExtractText() : null;
        var zoneRowId = zoneName is { Length: > 0 } ? zoneAetheryte!.Value.RowId : 0;

        if (!aetherytesByMap.Value.TryGetValue(mapId, out var markers))
            return zoneRowId == 0 ? null : new NearestAetheryte(zoneName!, false, zoneRowId, zoneName!);

        AetheryteMarker? best = null;
        var bestDistance = float.MaxValue;
        AetheryteMarker? bestAetheryte = null;
        var bestAetheryteDistance = float.MaxValue;
        foreach (var marker in markers)
        {
            var dx = marker.MapX - mapX;
            var dy = marker.MapY - mapY;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }

            if (!marker.Aethernet && distance < bestAetheryteDistance)
            {
                bestAetheryte = marker;
                bestAetheryteDistance = distance;
            }
        }

        if (best is not { } nearest)
            return zoneRowId == 0 ? null : new NearestAetheryte(zoneName!, false, zoneRowId, zoneName!);

        if (!nearest.Aethernet)
            return new NearestAetheryte(nearest.Name, false, nearest.AetheryteRowId, nearest.Name);

        // A shard is reached by teleporting to the zone's aetheryte first.
        var (teleportId, teleportName) = zoneRowId != 0
            ? (zoneRowId, zoneName!)
            : bestAetheryte is { } fallback ? (fallback.AetheryteRowId, fallback.Name) : (0u, "");
        return new NearestAetheryte(nearest.Name, true, teleportId, teleportName);
    }

    // Map -> the aetheryte and aethernet-shard markers drawn on it, in map
    // coordinates.
    private Dictionary<uint, List<AetheryteMarker>> BuildAetheryteMarkers()
    {
        var byMap = new Dictionary<uint, List<AetheryteMarker>>();
        var markers = SubrowSheet<MapMarker>();
        var aetherytes = Sheet<Aetheryte>();
        var places = Sheet<PlaceName>();
        foreach (var map in Sheet<Map>())
        {
            var range = markers.GetRowOrDefault(map.MapMarkerRange);
            if (range == null)
                continue;

            foreach (var marker in range.Value)
            {
                string name;
                var aetheryteRowId = 0u;
                if (marker.DataType == MapMarkerAetheryte)
                {
                    var aetheryte = aetherytes.GetRowOrDefault(marker.DataKey.RowId);
                    if (aetheryte is not { IsAetheryte: true } a ||
                        a.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty)
                        continue;

                    name = place.Name.ExtractText();
                    aetheryteRowId = a.RowId;
                }
                else if (marker.DataType == MapMarkerAethernet)
                {
                    var place = places.GetRowOrDefault(marker.DataKey.RowId);
                    if (place == null || place.Value.Name.IsEmpty)
                        continue;

                    name = place.Value.Name.ExtractText();
                }
                else
                {
                    continue;
                }

                if (!byMap.TryGetValue(map.RowId, out var list))
                    byMap[map.RowId] = list = [];

                list.Add(new AetheryteMarker(
                    aetheryteRowId,
                    name,
                    aetheryteRowId == 0,
                    MapCoordConverter.FromMapPixel(marker.X, map.SizeFactor),
                    MapCoordConverter.FromMapPixel(marker.Y, map.SizeFactor)));
            }
        }

        return byMap;
    }

    private readonly record struct AetheryteMarker(
        uint AetheryteRowId, string Name, bool Aethernet, float MapX, float MapY);
}
