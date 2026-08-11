using Wikiway.GameData;
using Xunit;

namespace Wikiway.Canary.Tests;

public sealed class GameDataFixture
{
    public Lumina.GameData? GameData { get; }
    public LuminaGameDataStore? StoreOrNull { get; }

    public GameDataFixture()
    {
        var sqpack = GameInstallLocator.FindSqpackPath();
        if (sqpack == null)
            return;

        GameData = new Lumina.GameData(sqpack);
        StoreOrNull = new LuminaGameDataStore(GameData);
    }

    public LuminaGameDataStore Store()
    {
        if (StoreOrNull == null)
            Assert.Skip("FFXIV install not found - set FFXIV_GAME_PATH to run the game data canaries.");
        return StoreOrNull;
    }
}

[CollectionDefinition("gamedata")]
public class GameDataCollection : ICollectionFixture<GameDataFixture>;
