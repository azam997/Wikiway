namespace Wikiway.GameData;

// Eorzea-time (ET) clock values as the sheets store them, shared by gathering
// node windows and sightseeing vistas.
//
// Sheet times are Eorzean clock values (900 = 9:00). Durations count the
// same way but carry past 60 minutes (300 = 3h ARR windows, 160 = 2h HW
// windows) - checked against Spruce Log 9:00-12:00 and Chysahl Greens
// 8:00-10:00/20:00-22:00; 65535 marks an unused slot.
internal static class EorzeaClock
{
    public const ushort Unused = 65535;
    public const int Day = 24 * 60;

    public static int ToMinutes(ushort clock) => ((clock / 100) * 60) + (clock % 100);

    // Exactly 24:00 stays as written, so a window ending at midnight reads
    // "20:00-24:00" rather than wrapping to "20:00-0:00".
    public static string Label(int minutes)
    {
        if (minutes != Day)
            minutes %= Day;
        return $"{minutes / 60}:{minutes % 60:00}";
    }
}
