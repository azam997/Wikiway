using System.Text.Json;

namespace Wikiway.Canary.Tests;

public static class GameInstallLocator
{
    public static string? FindSqpackPath()
    {
        foreach (var gamePath in Candidates())
        {
            if (gamePath == null)
                continue;

            var sqpack = Path.Combine(gamePath, "game", "sqpack");
            if (Directory.Exists(sqpack))
                return sqpack;
        }

        return null;
    }

    private static IEnumerable<string?> Candidates()
    {
        yield return Environment.GetEnvironmentVariable("FFXIV_GAME_PATH");
        yield return XivLauncherGamePath();
        yield return @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn";
        yield return @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online";
    }

    private static string? XivLauncherGamePath()
    {
        try
        {
            var config = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "launcherConfigV3.json");
            if (!File.Exists(config))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(config));
            return doc.RootElement.TryGetProperty("GamePath", out var path) ? path.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
