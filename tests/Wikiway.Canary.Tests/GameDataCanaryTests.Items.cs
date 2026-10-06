using Xunit;

namespace Wikiway.Canary.Tests;

// Item cards: equipment stats and food/medicine effects (store .Items.cs).
public partial class GameDataCanaryTests
{
    // Checks the equipment chain: EquipSlotCategory flags -> slot name,
    // ClassJobCategory, LevelItem, damage columns. The MRD starting weapon has
    // been an untouched level-1 two-hander since 2013. It carries no BaseParam
    // stats and can't be HQ, so phantom stats here mean a column read drifted.
    [Fact]
    public void WeatheredWarAxeEquipmentResolvesFromSheets()
    {
        var store = fixture.Store();

        var item = store.GetItem(1749);
        Assert.NotNull(item);
        Assert.Equal("Weathered War Axe", item.Name);
        Assert.NotNull(item.Equipment);
        Assert.Equal("Main Hand", item.Equipment.Slot);
        Assert.Equal(1, item.Equipment.EquipLevel);
        Assert.InRange(item.Equipment.ItemLevel, 1, 10);
        Assert.Contains("MRD", item.Equipment.ClassJobs);
        Assert.NotNull(item.Equipment.Weapon);
        Assert.InRange(item.Equipment.Weapon.PhysDamage, 5, 15);
        Assert.InRange(item.Equipment.Weapon.DelaySeconds, 1.0, 5.0);
        Assert.Empty(item.Equipment.Stats);
        Assert.False(item.Equipment.CanBeHq);
        Assert.StartsWith("BSM", item.Equipment.Repair);
    }

    // Checks the HQ-delta semantics: BaseParamSpecial rows 21/24 carry the HQ
    // defense bonuses on crafted armor (probed 2026-08-23). Bronze Cuirass has
    // been a level-15 CanBeHq armorer craft with two materia slots since 2013.
    [Fact]
    public void BronzeCuirassHqBonusesResolveFromSpecialParams()
    {
        var store = fixture.Store();

        var item = store.GetItem(3026);
        Assert.NotNull(item);
        Assert.Equal("Bronze Cuirass", item.Name);
        Assert.NotNull(item.Equipment);
        Assert.Equal("Body", item.Equipment.Slot);
        Assert.True(item.Equipment.CanBeHq);
        Assert.NotNull(item.Equipment.Defense);
        Assert.InRange(item.Equipment.Defense.Physical, 30, 50);
        Assert.InRange(item.Equipment.Defense.HqPhysBonus, 3, 8);
        Assert.InRange(item.Equipment.Defense.HqMagBonus, 3, 8);
        Assert.InRange(item.Equipment.Stats.Count, 2, 5);
        Assert.All(item.Equipment.Stats, s => Assert.False(string.IsNullOrEmpty(s.Name)));
        Assert.InRange(item.Equipment.MateriaSlots, 1, 5);
        Assert.False(string.IsNullOrEmpty(item.Equipment.Repair));
    }

    // 28,993 as of 7.3. Non-gear must stay payload-free or every item card
    // grows a bogus EQUIPMENT block.
    [Fact]
    public void EquippableItemCountLandsInAPlausibleBand()
    {
        var store = fixture.Store();
        var items = fixture.GameData!.GetExcelSheet<Lumina.Excel.Sheets.Item>()!;

        var equippable = items.Count(r => r.EquipSlotCategory.RowId != 0);
        Assert.InRange(equippable, 20_000, 60_000);

        Assert.Null(store.GetItem(5057)!.Equipment);
    }

    // Checks the ItemFood read (status + duration from ItemAction.Data, stats
    // with relative caps and HQ values from the shared ItemFood row).
    [Fact]
    public void BoiledEggCarriesItsFoodStats()
    {
        var store = fixture.Store();

        var item = store.GetItem(4650);
        Assert.NotNull(item?.Food);
        Assert.Equal("Well Fed", item.Food.StatusName);
        Assert.Equal(1800, item.Food.DurationSeconds);
        Assert.Equal(3, item.Food.ExpBonusPercent);

        var crit = item.Food.Stats.Single(s => s.Name == "Critical Hit");
        Assert.True(crit.Relative);
        Assert.Equal(8, crit.Value);
        Assert.Equal(2, crit.Max);
        Assert.Equal(10, crit.HqValue);
        Assert.Equal(3, crit.HqMax);

        var vitality = item.Food.Stats.Single(s => s.Name == "Vitality");
        Assert.False(vitality.Relative);
        Assert.Equal(1, vitality.Value);
    }

    // Medicine variant: short Medicated buff, no EXP bonus.
    [Fact]
    public void TinctureOfStrengthIsAShortMedicatedBuff()
    {
        var store = fixture.Store();

        var item = store.GetItem(27786);
        Assert.NotNull(item?.Food);
        Assert.Equal("Medicated", item.Food.StatusName);
        Assert.Equal(30, item.Food.DurationSeconds);
        Assert.Equal(0, item.Food.ExpBonusPercent);

        var strength = Assert.Single(item.Food.Stats);
        Assert.Equal("Strength", strength.Name);
        Assert.True(strength.Relative);
        Assert.Equal(94, strength.Max);
        Assert.Equal(117, strength.HqMax);
    }
}
