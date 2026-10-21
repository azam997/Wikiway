using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Items: the card itself, plus the EQUIPMENT and CONSUMABLE blocks read
// straight off the Item row. Acquisition and usage come from .Acquisition.cs,
// .Shops.cs and .Usage.cs, only when the card is opened (GetItemDetails).
public sealed partial class LuminaGameDataStore
{
    // ItemSearchCategory 0 means the market board doesn't list the item;
    // PriceMid is the gil-shop price shown on vendor lines.
    public ItemEntity? GetItem(uint rowId)
    {
        var row = Sheet<Item>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return new ItemEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "",
            row.Value.Description.ExtractText(),
            row.Value.ItemSearchCategory.RowId != 0,
            row.Value.Icon)
        {
            Equipment = BuildEquipment(row.Value),
            Food = BuildFood(row.Value),
            MateriaTag = row.Value.ItemUICategory.RowId == ItemUICategoryMateria ? MateriaTag(rowId) : "",
        };
    }

    // Every item the Materia sheet tags sits in this UI category (probed
    // 2026-10-21 against 7.3 sheets: 156 of 156), so other items skip the
    // Materia scan. A canary test fails if a patch breaks that.
    private const uint ItemUICategoryMateria = 58;

    // ACQUISITION and USAGE for one item, built when its card is opened. Each
    // source type scans its own sheet for rows naming the item.
    public ItemDetails GetItemDetails(uint itemRowId)
    {
        var row = Sheet<Item>().GetRowOrDefault(itemRowId);
        if (row == null || row.Value.Name.IsEmpty)
            return new ItemDetails(null, null);

        return new ItemDetails(BuildAcquisition(itemRowId, row.Value.PriceMid), BuildUsage(itemRowId));
    }

    // "Critical Hit +12" for a materia item, empty otherwise. The collapsed
    // row shows it. Zero-value grades exist (retired main-stat materia) and
    // get no tag.
    private string MateriaTag(uint itemRowId)
    {
        foreach (var materia in Sheet<Materia>())
        {
            for (var i = 0; i < materia.Item.Count && i < materia.Value.Count; i++)
            {
                if (materia.Item[i].RowId != itemRowId || materia.Value[i] == 0)
                    continue;

                var param = materia.BaseParam.ValueNullable?.Name.ExtractText() ?? "";
                if (param.Length > 0)
                    return $"{param} +{materia.Value[i]}";
            }
        }

        return "";
    }

    // HQ bonuses live in BaseParamSpecial/BaseParamValueSpecial as deltas keyed
    // by BaseParam row id; the combat numbers use these fixed rows (probed
    // 2026-08-23 against 7.3 sheets: Bronze Cuirass, Weathered War Axe, Square
    // Maple Shield). No delay delta exists.
    private const uint ParamPhysicalDamage = 12;
    private const uint ParamMagicDamage = 13;
    private const uint ParamBlockRate = 17;
    private const uint ParamBlockStrength = 18;
    private const uint ParamDefense = 21;
    private const uint ParamMagicDefense = 24;

    // Null for anything that can't be equipped (EquipSlotCategory 0).
    private ItemEquipment? BuildEquipment(Item row)
    {
        var slots = row.EquipSlotCategory.ValueNullable;
        if (row.EquipSlotCategory.RowId == 0 || slots == null)
            return null;

        var hqDeltas = new Dictionary<uint, short>();
        if (row.CanBeHq)
            for (var i = 0; i < row.BaseParamSpecial.Count && i < row.BaseParamValueSpecial.Count; i++)
                if (row.BaseParamSpecial[i].RowId != 0 && row.BaseParamValueSpecial[i] != 0)
                    hqDeltas[row.BaseParamSpecial[i].RowId] = row.BaseParamValueSpecial[i];

        var stats = new List<EquipStat>();
        for (var i = 0; i < row.BaseParam.Count && i < row.BaseParamValue.Count; i++)
        {
            var paramId = row.BaseParam[i].RowId;
            if (paramId == 0)
                continue;
            var hqBonus = hqDeltas.GetValueOrDefault(paramId);
            if (row.BaseParamValue[i] == 0 && hqBonus == 0)
                continue;
            var name = row.BaseParam[i].ValueNullable?.Name.ExtractText() ?? "";
            if (name.Length > 0)
                stats.Add(new EquipStat(name, row.BaseParamValue[i], hqBonus));
        }

        var repairJob = row.ClassJobRepair.ValueNullable?.Abbreviation.ExtractText() ?? "";
        var repairMat = row.ItemRepair.ValueNullable?.Item.ValueNullable?.Name.ExtractText() ?? "";
        var special = row.ItemSpecialBonus.ValueNullable?.Name.ExtractText() ?? "";
        if (special.Length > 0 && row.ItemSpecialBonusParam > 0)
            special = $"{special} ({row.ItemSpecialBonusParam})";

        return new ItemEquipment(
            SlotName(slots.Value),
            (ushort)row.LevelItem.RowId,
            row.LevelEquip,
            row.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "",
            stats)
        {
            Weapon = row.DamagePhys > 0 || row.DamageMag > 0
                ? new WeaponInfo(row.DamagePhys, row.DamageMag, row.Delayms / 1000.0,
                    (ushort)hqDeltas.GetValueOrDefault(ParamPhysicalDamage),
                    (ushort)hqDeltas.GetValueOrDefault(ParamMagicDamage))
                : null,
            Defense = row.DefensePhys > 0 || row.DefenseMag > 0
                ? new DefenseInfo(row.DefensePhys, row.DefenseMag,
                    (ushort)hqDeltas.GetValueOrDefault(ParamDefense),
                    (ushort)hqDeltas.GetValueOrDefault(ParamMagicDefense))
                : null,
            Block = row.Block > 0 || row.BlockRate > 0
                ? new BlockInfo(row.Block, row.BlockRate,
                    (ushort)hqDeltas.GetValueOrDefault(ParamBlockStrength),
                    (ushort)hqDeltas.GetValueOrDefault(ParamBlockRate))
                : null,
            MateriaSlots = row.MateriaSlotCount,
            AdvancedMelding = row.IsAdvancedMeldingPermitted,
            Unique = row.IsUnique,
            Untradable = row.IsUntradable,
            CanBeHq = row.CanBeHq,
            DyeCount = row.DyeCount,
            Repair = repairJob.Length > 0 && repairMat.Length > 0 ? $"{repairJob} · {repairMat}" : "",
            Series = row.ItemSeries.ValueNullable?.Name.ExtractText() ?? "",
            SpecialBonus = special,
            Desynthable = row.Desynth > 0,
            SellPrice = row.PriceLow,
        };
    }

    // 1 marks the slot the item occupies (-1 marks slots it blocks, e.g. a
    // two-hander's off hand); rings carry 1 in both finger columns.
    private static string SlotName(EquipSlotCategory s) => s switch
    {
        { MainHand: 1 } => "Main Hand",
        { OffHand: 1 } => "Off Hand",
        { Head: 1 } => "Head",
        { Body: 1 } => "Body",
        { Gloves: 1 } => "Hands",
        { Legs: 1 } => "Legs",
        { Feet: 1 } => "Feet",
        { Ears: 1 } => "Ears",
        { Neck: 1 } => "Neck",
        { Wrists: 1 } => "Wrists",
        { FingerL: 1 } or { FingerR: 1 } => "Ring",
        { Waist: 1 } => "Waist",
        { SoulCrystal: 1 } => "Soul Crystal",
        _ => "",
    };

    // Food and medicine actions carry (status, ItemFood row, duration in
    // seconds) in Data[0..2]: both food types buff Well Fed for 1800s, the
    // stat tinctures Medicated for 15-30s (probed 2026-09-01: Boiled Egg,
    // Rroneek Steak, Tincture of Strength). NQ and HQ share the ItemFood row;
    // HQ values live in its ValueHQ/MaxHQ columns. EXPBonusPercent is set on
    // every ItemFood row, but only Well Fed grants it in-game.
    private const uint ItemActionBattleFood = 844;
    private const uint ItemActionCraftFood = 845;
    private const uint ItemActionMedicine = 846;
    private const uint StatusWellFed = 48;

    // Null unless the item's action is food or medicine; potions and ethers
    // use other action types and don't reference ItemFood.
    private ItemFoodEffect? BuildFood(Item row)
    {
        if (row.ItemAction.ValueNullable is not { } action ||
            action.Action.RowId is not (ItemActionBattleFood or ItemActionCraftFood or ItemActionMedicine))
            return null;

        var food = Sheet<ItemFood>().GetRowOrDefault(action.Data[1]);
        if (food == null)
            return null;

        var stats = new List<FoodStat>();
        foreach (var param in food.Value.Params)
        {
            if (param.BaseParam.RowId == 0 ||
                param.BaseParam.ValueNullable is not { } baseParam || baseParam.Name.IsEmpty)
                continue;

            stats.Add(new FoodStat(
                baseParam.Name.ExtractText(),
                param.IsRelative,
                param.Value,
                param.Max,
                row.CanBeHq ? param.ValueHQ : param.Value,
                row.CanBeHq ? param.MaxHQ : param.Max));
        }

        return new ItemFoodEffect(
            Sheet<Status>().GetRowOrDefault(action.Data[0])?.Name.ExtractText() ?? "",
            action.Data[2],
            action.Data[0] == StatusWellFed ? food.Value.EXPBonusPercent : 0,
            stats);
    }
}
