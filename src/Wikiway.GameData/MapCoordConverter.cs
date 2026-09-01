namespace Wikiway.GameData;

public static class MapCoordConverter
{
    // World position -> the 1-42ish map coordinate players see. Same formula the
    // community maps/tools use; SizeFactor is a percentage, offsets are per-map.
    public static float ToMapCoord(float worldCoord, ushort sizeFactor, short offset)
    {
        // Blank Map rows carry SizeFactor 0; treating it as 100% beats
        // feeding Infinity into a map link.
        var scale = sizeFactor == 0 ? 1.0f : sizeFactor / 100.0f;
        var scaled = (worldCoord + offset) * scale;
        return (41.0f / scale) * ((scaled + 1024.0f) / 2048.0f) + 1.0f;
    }

    // FishingSpot.X/Z and SpearfishingNotebook.X/Y are map-image pixels
    // (0-2048), not world units - probe-verified against Limsa Lominsa Lower
    // Decks (7.7, 12.2), Costa del Sol (32.9, 29.3) and the Silver Bazaar
    // (14.6, 29.5) across SizeFactor 100 and 200 maps.
    public static float FromMapPixel(float pixel, ushort sizeFactor)
    {
        var scale = sizeFactor == 0 ? 1.0f : sizeFactor / 100.0f;
        return (41.0f / scale) * (pixel / 2048.0f) + 1.0f;
    }
}
