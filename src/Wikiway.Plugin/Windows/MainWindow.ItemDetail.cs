using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// Expanded item detail: EQUIPMENT, CONSUMABLE, ACQUISITION and USAGE blocks.
// The data comes pre-resolved on ItemEntity (LuminaGameDataStore.Items,
// .Acquisition, .Usage); this file only lays it out.
public partial class MainWindow
{
    private const int MaxVendorLines = 6;

    // One line per source: recipes, gil vendors, special-shop exchanges,
    // gathering nodes, fishing holes, GC seal vendors and retainer ventures.
    // Shared by the item card and every teaching-item reuse (mount whistles,
    // minion items...); ownerRowId keeps their button ids distinct.
    private void DrawAcquisitionSources(uint ownerRowId, ItemAcquisition acquisition)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        foreach (var recipe in acquisition.Recipes)
        {
            fonts.Body13.Push();
            IconLine(FontAwesomeIcon.Hammer, recipe.Level > 0 ? $"{recipe.CraftType} · Level {recipe.Level}" : recipe.CraftType);
            if (recipe.MasterBook.Length > 0)
            {
                InlineText("· requires", Theme.Neutral400);
                ImGui.SameLine(0, Theme.Space2 * scale);
                NavLink(recipe.MasterBook, SearchCategory.Items);
            }

            if (recipe.Ingredients.Count > 0)
            {
                InlineText("—", Theme.Neutral400);
                ImGui.SameLine(0, Theme.Space2 * scale);
                DrawItemAmounts(recipe.Ingredients);
            }

            fonts.Body13.Pop();
        }

        for (var i = 0; i < acquisition.Vendors.Count; i++)
        {
            if (CapReached(i, acquisition.Vendors.Count, "vendors"))
                break;

            var vendor = acquisition.Vendors[i];
            var actionsWidth = LocationActionsWidth(vendor.Location);
            var price = vendor.GilPrice > 0 ? $"{vendor.GilPrice} gil" : "";
            var priceWidth = PriceWidth(price);

            fonts.Body13.Push();
            GlyphPrefix(FontAwesomeIcon.MapPin);
            ImGui.TextUnformatted(vendor.NpcName);
            if (vendor.Location is { } loc)
                LocationTail(loc, actionsWidth + priceWidth);
            fonts.Body13.Pop();

            if (price.Length > 0)
                DrawPrice(price, actionsWidth, priceWidth);
            if (vendor.Location is { } flagLoc)
                LocationButtons(flagLoc, $"vendor{ownerRowId}-{i + 1}");
        }

        for (var i = 0; i < acquisition.Exchanges.Count; i++)
        {
            if (CapReached(i, acquisition.Exchanges.Count, "exchanges"))
                break;

            var exchange = acquisition.Exchanges[i];
            fonts.Body13.Push();
            GlyphPrefix(FontAwesomeIcon.ExchangeAlt);
            ImGui.TextUnformatted(exchange.NpcName);
            InlineText($"{exchange.ShopName} —", Theme.Neutral400);
            // Each entry is one offer; its parts are all paid together.
            for (var o = 0; o < exchange.Costs.Count; o++)
            {
                if (o > 0)
                {
                    ImGui.SameLine(0, 0);
                    ColorText(",", Theme.Neutral400);
                }

                ImGui.SameLine(0, Theme.Space2 * scale);
                DrawItemAmounts(exchange.Costs[o]);
            }

            if (exchange.Location is { } loc)
                LocationTail(loc, LocationActionsWidth(loc));
            fonts.Body13.Pop();

            if (exchange.Location is { } flagLoc)
                LocationButtons(flagLoc, $"exch{ownerRowId}-{i + 1}");
        }

        for (var i = 0; i < acquisition.Gathering.Count; i++)
        {
            if (CapReached(i, acquisition.Gathering.Count, "nodes"))
                break;

            var node = acquisition.Gathering[i];
            fonts.Body13.Push();
            IconLine(FontAwesomeIcon.Leaf, node.Level > 0 ? $"{node.NodeType} · Level {node.Level}" : node.NodeType);
            if (node.TimeWindow.Length > 0)
            {
                InlineText(FontAwesomeIcon.Clock.ToIconString(), Theme.Highlight);
                InlineText(node.TimeWindow, Theme.Neutral300, Theme.Space2);
            }

            if (node.Location is { } loc)
                LocationTail(loc, LocationActionsWidth(loc));
            fonts.Body13.Pop();

            if (node.Location is { } flagLoc)
                LocationButtons(flagLoc, $"gather{ownerRowId}-{i + 1}");
        }

        for (var i = 0; i < acquisition.Fishing.Count; i++)
        {
            if (CapReached(i, acquisition.Fishing.Count, "fishing spots"))
                break;

            var spot = acquisition.Fishing[i];
            var method = spot.Spearfishing ? "Spearfishing" : "Fishing";
            fonts.Body13.Push();
            IconLine(FontAwesomeIcon.Fish, spot.Level > 0 ? $"{method} · Level {spot.Level}" : method);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(spot.SpotName);
            if (spot.Location is { } loc)
                LocationTail(loc, LocationActionsWidth(loc));
            fonts.Body13.Pop();

            if (spot.Location is { } flagLoc)
                LocationButtons(flagLoc, $"fish{ownerRowId}-{i + 1}");
        }

        if (acquisition.FishingNote.Length > 0)
            TextLine(acquisition.FishingNote, fonts.Citation12, Theme.Neutral500, wrap: true);

        for (var i = 0; i < acquisition.SealVendors.Count; i++)
        {
            var sealVendor = acquisition.SealVendors[i];
            var actionsWidth = LocationActionsWidth(sealVendor.Location);
            var seals = $"{sealVendor.SealCost} seals";
            var sealsWidth = PriceWidth(seals);

            fonts.Body13.Push();
            GlyphPrefix(FontAwesomeIcon.Medal);
            ImGui.TextUnformatted(sealVendor.NpcName);
            if (sealVendor.RequiredRank.Length > 0)
                InlineText($"requires {sealVendor.RequiredRank}", Theme.Neutral400);
            if (sealVendor.Location is { } loc)
                LocationTail(loc, actionsWidth + sealsWidth);
            fonts.Body13.Pop();

            DrawPrice(seals, actionsWidth, sealsWidth);
            if (sealVendor.Location is { } flagLoc)
                LocationButtons(flagLoc, $"seal{ownerRowId}-{i + 1}");
        }

        foreach (var venture in acquisition.Ventures)
        {
            fonts.Body13.Push();
            IconLine(FontAwesomeIcon.Suitcase, $"Retainer venture · Level {venture.Level} {venture.Category}");
            if (venture.Quantities.Length > 0)
                InlineText($"— {venture.Quantities} per venture", Theme.Neutral400);
            fonts.Body13.Pop();
        }
    }

    // Capped source lists stop at MaxVendorLines with a "+N more" line.
    // Returns true once that line has been drawn.
    private bool CapReached(int index, int count, string noun)
    {
        if (index < MaxVendorLines)
            return false;

        TextLine($"+{count - index} more {noun}", plugin.Fonts.Citation12, Theme.Neutral600);
        return true;
    }

    // Width a price column takes, including its gap before the buttons.
    private float PriceWidth(string price) =>
        price.Length > 0 ? CitationTextWidth(price) + (Theme.Space3 * ImGuiHelpers.GlobalScale) : 0f;

    // A muted price ("120 gil", "300 seals") right-aligned just left of the
    // location buttons, so prices line up in a column across rows.
    private void DrawPrice(string price, float actionsWidth, float priceWidth)
    {
        plugin.Fonts.Citation12.Push();
        ImGui.SameLine();
        ImGui.SetCursorPosX(RowRightEdge() - actionsWidth - priceWidth);
        ColorText(price, Theme.Neutral500);
        plugin.Fonts.Citation12.Pop();
    }

    // "2x Iron Ore + 1x Fire Shard": quantities stay plain text; the item
    // name is the link, so "where do I get the Steel Amalj'ok" is one click
    // instead of a retyped search.
    private void DrawItemAmounts(IReadOnlyList<ItemAmount> parts)
    {
        var scale = ImGuiHelpers.GlobalScale;
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                InlineText("+", Theme.Neutral400, Theme.Space2);
                ImGui.SameLine(0, Theme.Space2 * scale);
            }

            ColorText($"{parts[i].Amount}x", Theme.Neutral400);
            ImGui.SameLine(0, Theme.Space1 * scale);
            NavLink(parts[i].Name, SearchCategory.Items);
        }
    }

    // Slot, item level and job line, combat numbers, one line per stat, then
    // a muted line of flags (materia slots, dyeable, unique, repair...).
    // HQ bonuses render inline as "(+n HQ)".
    private void DrawEquipment(ItemEquipment eq)
    {
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("EQUIPMENT");

        fonts.Body13.Push();
        IconLine(FontAwesomeIcon.ShieldAlt, eq.Slot.Length > 0 ? $"{eq.Slot} · ilvl {eq.ItemLevel}" : $"ilvl {eq.ItemLevel}");
        InlineText(eq.ClassJobs.Length > 0 ? $"Lv. {eq.EquipLevel} · {eq.ClassJobs}" : $"Lv. {eq.EquipLevel}", Theme.Neutral400);

        var combat = new List<string>();
        if (eq.Weapon is { } weapon)
        {
            if (weapon.PhysDamage > 0)
                combat.Add($"Physical Damage {weapon.PhysDamage}{HqSuffix(weapon.HqPhysBonus)}");
            if (weapon.MagDamage > 0)
                combat.Add($"Magic Damage {weapon.MagDamage}{HqSuffix(weapon.HqMagBonus)}");
            combat.Add(string.Create(CultureInfo.InvariantCulture, $"Delay {weapon.DelaySeconds:0.00}s"));
        }

        if (eq.Defense is { } defense)
        {
            combat.Add($"Defense {defense.Physical}{HqSuffix(defense.HqPhysBonus)}");
            combat.Add($"Magic Defense {defense.Magical}{HqSuffix(defense.HqMagBonus)}");
        }

        if (eq.Block is { } block)
        {
            combat.Add($"Block {block.Strength}{HqSuffix(block.HqStrengthBonus)}");
            combat.Add($"Block Rate {block.Rate}{HqSuffix(block.HqRateBonus)}");
        }

        if (combat.Count > 0)
            ColorText(string.Join(" · ", combat), Theme.Neutral300);

        foreach (var stat in eq.Stats)
            ColorText($"{stat.Name} {(stat.Value >= 0 ? "+" : "")}{stat.Value}{HqSuffix(stat.HqBonus)}", Theme.Neutral300);
        fonts.Body13.Pop();

        var meta = new List<string>();
        if (eq.MateriaSlots > 0)
            meta.Add($"{eq.MateriaSlots} materia slot{(eq.MateriaSlots > 1 ? "s" : "")}{(eq.AdvancedMelding ? " (advanced melding)" : "")}");
        if (eq.DyeCount > 0)
            meta.Add(eq.DyeCount > 1 ? $"Dyeable ×{eq.DyeCount}" : "Dyeable");
        if (eq.Unique)
            meta.Add("Unique");
        if (eq.Untradable)
            meta.Add("Untradable");
        if (eq.CanBeHq)
            meta.Add("HQ available");
        if (eq.Repair.Length > 0)
            meta.Add($"Repair: {eq.Repair}");
        if (eq.Desynthable)
            meta.Add("Desynthable");
        if (eq.SellPrice > 0 && !eq.Untradable)
            meta.Add($"Sells for {eq.SellPrice} gil");
        if (eq.Series.Length > 0)
            meta.Add($"Set: {eq.Series}");
        if (eq.SpecialBonus.Length > 0)
            meta.Add(eq.SpecialBonus);

        if (meta.Count > 0)
            TextLine(string.Join(" · ", meta), fonts.Citation12, Theme.Neutral500, wrap: true);
    }

    private static string HqSuffix(int bonus) => bonus > 0 ? $" (+{bonus} HQ)" : "";

    // Food and medicine: "Well Fed · 30 min · EXP +3%", then one line per stat.
    private void DrawConsumable(ItemFoodEffect food)
    {
        ImGui.Spacing();
        DetailLabel("CONSUMABLE");

        var header = food.StatusName.Length > 0
            ? $"{food.StatusName} · {DurationLabel(food.DurationSeconds)}"
            : DurationLabel(food.DurationSeconds);
        if (food.ExpBonusPercent > 0)
            header += $" · EXP +{food.ExpBonusPercent}%";

        plugin.Fonts.Body13.Push();
        IconLine(FontAwesomeIcon.Utensils, header);
        foreach (var stat in food.Stats)
            ColorText(FoodStatLine(stat), Theme.Neutral300);
        plugin.Fonts.Body13.Pop();
    }

    private static string DurationLabel(int seconds) =>
        seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds}s";

    // "Vitality +8% (max 2) (HQ +10% (max 3))"; the HQ part only when it differs.
    private static string FoodStatLine(FoodStat stat)
    {
        var line = $"{stat.Name} {FoodValue(stat.Relative, stat.Value, stat.Max)}";
        if (stat.HqValue != stat.Value || stat.HqMax != stat.Max)
            line += $" (HQ {FoodValue(stat.Relative, stat.HqValue, stat.HqMax)})";
        return line;
    }

    private static string FoodValue(bool relative, int value, int max)
    {
        var sign = value >= 0 ? "+" : "";
        return relative ? $"{sign}{value}% (max {max})" : $"{sign}{value}";
    }

    // What the item is used for: materia stat, recipe count, custom
    // deliveries, collectable turn-ins, and treasure-map dig zones.
    private void DrawUsage(ItemUsage usage)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("USAGE");

        fonts.Body13.Push();
        if (usage.MateriaTag.Length > 0)
            IconLine(FontAwesomeIcon.Gem, $"Materia · {usage.MateriaTag}");

        if (usage.UsedInRecipes > 0)
            IconLine(FontAwesomeIcon.Hammer,
                usage.UsedInRecipes == 1 ? "Used in 1 recipe" : $"Used in {usage.UsedInRecipes} recipes");

        foreach (var delivery in usage.Deliveries)
        {
            IconLine(FontAwesomeIcon.BoxOpen, $"Custom delivery · {delivery.NpcName}");
            if (delivery.UnlockQuest is { } quest)
            {
                InlineText("— requires", Theme.Neutral400);
                ImGui.SameLine(0, Theme.Space2 * scale);
                NavLink(quest.Name, SearchCategory.Unlocks);
            }
        }

        foreach (var turnIn in usage.CollectableTurnIns)
        {
            var band = turnIn.LevelMax > turnIn.LevelMin
                ? $"Lv {turnIn.LevelMin}-{turnIn.LevelMax}"
                : $"Lv {turnIn.LevelMin}";
            IconLine(FontAwesomeIcon.Star, $"Collectable turn-in · {band}");
            if (turnIn.MaxScrips > 0)
                InlineText($"— up to {turnIn.MaxScrips} scrips", Theme.Neutral400);
        }

        if (usage.TreasureMap is { } map)
        {
            var sites = map.Zones.Sum(z => z.SpotCount);
            var party = map.PartySize > 1 ? $" · up to {map.PartySize} players" : "";
            IconLine(FontAwesomeIcon.Map, $"Treasure map{party} · {sites} dig sites");
            TextLine(string.Join(", ", map.Zones.Select(z => $"{z.ZoneName} ({z.SpotCount})")),
                fonts.Citation12, Theme.Neutral500, wrap: true);
        }

        fonts.Body13.Pop();
    }
}
