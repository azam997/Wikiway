namespace Wikiway.GameData;

public static class MapCoordConverter
{
    // World position -> the 1-42ish map coordinate players see. Same formula the
    // community maps/tools use; SizeFactor is a percentage, offsets are per-map.
    public static float ToMapCoord(float worldCoord, ushort sizeFactor, short offset)
    {
        var scale = sizeFactor / 100.0f;
        var scaled = (worldCoord + offset) * scale;
        return (41.0f / scale) * ((scaled + 1024.0f) / 2048.0f) + 1.0f;
    }
}
