using Wikiway.Core.Abstractions;
using Xunit;

namespace Wikiway.Canary.Tests;

// Mounts, minions, orchestrion rolls, Triple Triad cards and emotes (store
// .Collections.cs).
public partial class GameDataCanaryTests
{
    [Fact]
    public void MountAndMinionSamplesResolve()
    {
        var store = fixture.Store();
        var names = store.GetAllNames();

        var mount = store.GetMount(names.First(n => n.Kind == EntityKind.Mount).RowId);
        var minion = store.GetMinion(names.First(n => n.Kind == EntityKind.Minion).RowId);

        Assert.False(string.IsNullOrEmpty(mount?.Name));
        Assert.False(string.IsNullOrEmpty(minion?.Name));
        Assert.True(mount!.Icon > 0, "mount has no icon - Icon column read broke");
        Assert.True(minion!.Icon > 0, "minion has no icon - Icon column read broke");
    }

    // Checks the reverse ItemAction join for mounts (action type 1322, Data[0]
    // = Mount row) and the MountTransient lore text. Aithon has been taught by
    // its whistle since 2.x.
    [Fact]
    public void AithonMountResolvesItsWhistleAndLore()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Mount && n.Name == "aithon");
        Assert.NotNull(entry);

        var mount = store.GetMount(entry.RowId);
        Assert.NotNull(mount);
        Assert.NotNull(mount.TeachingItem);
        Assert.Equal("Aithon Whistle", mount.TeachingItem.Name);
        Assert.False(string.IsNullOrEmpty(mount.Description));
    }

    // Checks the minion join (action type 853) plus the CompanionTransient
    // battle-stat columns, checked 2026-09-01: HP 400, 55/40/3.
    [Fact]
    public void WindupCursorMinionCarriesBattleStatsAndItsItem()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Minion && n.Name == "wind-up cursor");
        Assert.NotNull(entry);

        var minion = store.GetMinion(entry.RowId);
        Assert.NotNull(minion);
        Assert.NotNull(minion.TeachingItem);
        Assert.Equal("Wind-up Cursor", minion.TeachingItem.Name);
        Assert.NotNull(minion.BattleStats);
        Assert.Equal(400, minion.BattleStats.Hp);
        Assert.Equal(55, minion.BattleStats.Attack);
        Assert.Equal(40, minion.BattleStats.Defense);
        Assert.Equal(3, minion.BattleStats.Speed);
        Assert.Equal("The Finger", minion.BattleStats.SpecialAction);
        Assert.False(string.IsNullOrEmpty(minion.Description));
    }

    // Orchestrion rolls join through Item.AdditionalData (action type 25183),
    // not Data[0]. Row 1 has been "A Cold Wind" since the orchestrion shipped.
    [Fact]
    public void AColdWindOrchestrionRollResolvesFromItsItem()
    {
        var store = fixture.Store();

        var roll = store.GetOrchestrion(1);
        Assert.NotNull(roll);
        Assert.Equal("A Cold Wind", roll.Name);
        Assert.NotNull(roll.TeachingItem);
        Assert.Equal("A Cold Wind Orchestrion Roll", roll.TeachingItem.Name);
        Assert.Contains("MGP", roll.Description);
        Assert.False(string.IsNullOrEmpty(roll.Category));
    }

    // Checks TripleTriadCardResident stats and the pack-purchase obtain label
    // (checked 2026-09-01: 7/5/3/5, sale 300, obtain type 8).
    [Fact]
    public void MomodiModiCardCarriesStatsAndPackLabel()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.TripleTriadCard && n.Name == "momodi modi");
        Assert.NotNull(entry);

        var card = store.GetTripleTriadCard(entry.RowId);
        Assert.NotNull(card);
        Assert.Equal(7, card.Top);
        Assert.Equal(5, card.Bottom);
        Assert.Equal(3, card.Left);
        Assert.Equal(5, card.Right);
        Assert.InRange(card.Stars, 1, 5);
        Assert.Equal(300u, card.SaleValue);
        Assert.Contains("purchase", card.ObtainText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(card.TeachingItem);
        Assert.Equal("Momodi Modi Card", card.TeachingItem.Name);
    }

    // NPC-win obtain types carry an ENpcResident in Acquisition and a Level in
    // Location; the Vanu Vanu card has been won from Mogmill since 3.2.
    [Fact]
    public void VanuVanuCardIsWonFromMogmill()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.TripleTriadCard && n.Name == "vanu vanu");
        Assert.NotNull(entry);

        var card = store.GetTripleTriadCard(entry.RowId);
        Assert.NotNull(card);
        Assert.Equal("Mogmill", card.NpcName);
        Assert.NotNull(card.NpcLocation);
        Assert.Contains("Churning Mists", card.NpcLocation.ZoneName, StringComparison.OrdinalIgnoreCase);
    }

    // Duty-drop obtain types carry a ContentFinderCondition in Acquisition.
    [Fact]
    public void SahaginCardDropsInSastasha()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.TripleTriadCard && n.Name == "sahagin");
        Assert.NotNull(entry);

        var card = store.GetTripleTriadCard(entry.RowId);
        Assert.NotNull(card);
        Assert.Contains("Sastasha", card.DutyName, StringComparison.OrdinalIgnoreCase);
    }

    // Emote unlock bits (Emote.UnlockLink outside the quest id block) join to
    // the manual item whose type-2633 ItemAction sets the same bit.
    [Fact]
    public void BombDanceEmoteResolvesItsEtiquetteManual()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Emote && n.Name == "bomb dance");
        Assert.NotNull(entry);

        var emote = store.GetEmote(entry.RowId);
        Assert.NotNull(emote);
        Assert.Equal("/bombdance", emote.Command);
        Assert.Null(emote.UnlockQuest);
        Assert.NotNull(emote.TeachingItem);
        Assert.Equal("Ballroom Etiquette - The Bomb Dance", emote.TeachingItem.Name);
    }

    // UnlockLink values in the 0x10000 quest block are Quest row ids.
    [Fact]
    public void MoogleDanceEmoteUnlocksThroughItsQuest()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Emote && n.Name == "moogle dance");
        Assert.NotNull(entry);

        var emote = store.GetEmote(entry.RowId);
        Assert.NotNull(emote);
        Assert.NotNull(emote.UnlockQuest);
        Assert.Contains("Piecing Together the Past", emote.UnlockQuest.Name);
    }
}
