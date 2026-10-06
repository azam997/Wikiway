using Wikiway.Core.Abstractions;
using Xunit;

namespace Wikiway.Canary.Tests;

// Achievements, vistas, hunt marks, aether currents, FATEs and leves (store
// .World.cs).
public partial class GameDataCanaryTests
{
    [Fact]
    public void AchievementSampleHasDescription()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().First(n => n.Kind == EntityKind.Achievement);
        var achievement = store.GetAchievement(entry.RowId);

        Assert.NotNull(achievement);
        Assert.False(string.IsNullOrEmpty(achievement.Description));
    }

    // Checks the Adventure sheet reads: hint vs lore column pick, Level -> map
    // coords, emote text command, and the ET window rendering (800-1159 in the
    // sheet is the 8:00-12:00 window).
    [Fact]
    public void BarracudaPiersVistaResolvesFromSheets()
    {
        var store = fixture.Store();

        var vista = store.GetVista(2162688);
        Assert.NotNull(vista);
        Assert.Equal("Barracuda Piers", vista.Name);
        Assert.Contains("warships", vista.Hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Crimson Fleet", vista.Lore);
        Assert.Equal("Limsa Lominsa", vista.Region);
        Assert.Equal("/lookout", vista.Emote);
        Assert.Equal("8:00-12:00 ET", vista.TimeWindow);

        Assert.NotNull(vista.Location);
        Assert.Contains("Limsa", vista.Location.ZoneName, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(vista.Location.MapX, 9, 11);
        Assert.InRange(vista.Location.MapY, 7, 9);
    }

    // A window wrapping midnight (The Astalicia, 1800-459) must render as
    // 18:00-5:00, not fold into nonsense.
    [Fact]
    public void AstaliciaVistaWindowWrapsMidnight()
    {
        var store = fixture.Store();

        var vista = store.GetVista(2162689);
        Assert.NotNull(vista);
        Assert.Equal("18:00-5:00 ET", vista.TimeWindow);
    }

    // Rank byte -> letter (probed: 1=B, 2=A, 3=S) and the hunt-bill zone join.
    // Only billed marks carry a zone; Laideronnette has been the East Shroud
    // S rank since 2.0 and sits on no bill.
    [Fact]
    public void HuntMarkRanksAndZonesResolveFromSheets()
    {
        var store = fixture.Store();

        var laideronnette = store.GetHuntMark(3);
        Assert.NotNull(laideronnette);
        Assert.Equal("Laideronnette", laideronnette.Name);
        Assert.Equal("S", laideronnette.Rank);
        Assert.Equal("", laideronnette.ZoneName);

        var whiteJoker = store.GetAllNames()
            .First(n => n.Kind == EntityKind.HuntMark && n.Name == "white joker");
        var mark = store.GetHuntMark(whiteJoker.RowId);
        Assert.NotNull(mark);
        Assert.Equal("B", mark.Rank);
        Assert.Equal("Central Shroud", mark.ZoneName);
    }

    // Checks AetherCurrentCompFlgSet -> AetherCurrent.Quest with quest levels
    // and start-flag locations. Every flying zone since Heavensward grants five
    // currents from named quests.
    [Fact]
    public void KholusiaAetherCurrentsResolveTheirFiveQuests()
    {
        var store = fixture.Store();

        var zone = store.GetAllNames()
            .First(n => n.Kind == EntityKind.AetherCurrentZone && n.Name == "kholusia aether currents");
        var currents = store.GetAetherCurrentZone(zone.RowId);
        Assert.NotNull(currents);
        Assert.Equal("Kholusia", currents.Name);
        Assert.Equal(5, currents.QuestCurrents.Count);
        Assert.All(currents.QuestCurrents, q => Assert.False(string.IsNullOrEmpty(q.Quest.Name)));
        Assert.True(currents.QuestCurrents.Count(q => q.StartLocation != null) >= 4,
            "quest start locations collapsed");
    }

    // FATE cards are name + level + description; Location is an LGB id the
    // sheets cannot resolve, so no coordinates are asserted anywhere.
    [Fact]
    public void CleverGirlsFateResolvesFromSheets()
    {
        var store = fixture.Store();

        var fate = store.GetFate(120);
        Assert.NotNull(fate);
        Assert.Equal("Clever Girls", fate.Name);
        Assert.Equal(5, fate.Level);
        Assert.Contains("anoles", fate.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fate.RequiredQuest);
    }

    // The Bozja field fates are the quest-gated ones.
    [Fact]
    public void BozjanFateCarriesItsRequiredQuest()
    {
        var store = fixture.Store();

        var gated = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Fate)
            .Select(n => store.GetFate(n.RowId))
            .FirstOrDefault(f => f?.RequiredQuest != null);
        Assert.NotNull(gated);
        Assert.Equal("Where Eagles Nest", gated.RequiredQuest!.Name);
    }

    // Checks the Leve sheet reads: assignment type, job category, the direct
    // LevelLevemete flag, and reward columns. "In with the New" has been the
    // level 1 Gridania carpenter leve since 2.0.
    [Fact]
    public void InWithTheNewLeveResolvesItsLevemete()
    {
        var store = fixture.Store();

        var leve = store.GetLeve(21);
        Assert.NotNull(leve);
        Assert.Equal("In with the New", leve.Name);
        Assert.Equal(1, leve.Level);
        Assert.Equal("Carpenter", leve.Type);
        Assert.Equal("CRP", leve.JobCategory);
        Assert.Equal(1, leve.AllowanceCost);
        Assert.True(leve.ExpReward > 0);
        Assert.True(leve.GilReward > 0);

        Assert.NotNull(leve.Levemete);
        Assert.Contains("Gridania", leve.Levemete.ZoneName, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(leve.Levemete.MapX, 10, 13);
        Assert.InRange(leve.Levemete.MapY, 10, 13);
    }
}
