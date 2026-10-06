using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Reverse indexes behind item acquisition: for each item, which recipes, shops,
// nodes, fishing spots, GC vendors and ventures produce it, and which NPCs run
// those shops. Built once (see LazyLookup) and read by .Acquisition.cs.
public sealed partial class LuminaGameDataStore
{
    // Item-to-source reverse indexes, built in one pass over the shop, recipe,
    // gathering, fishing, GC and venture sheets. Keys are Item row ids unless
    // the name says otherwise.
    private sealed record AcquisitionIndex(
        Dictionary<uint, List<uint>> RecipesByItem,
        Dictionary<uint, List<uint>> ShopsByItem,
        Dictionary<uint, List<uint>> NpcsByShop,
        Dictionary<uint, List<uint>> SpecialShopsByItem,
        Dictionary<uint, List<uint>> NpcsBySpecialShop,
        Dictionary<uint, List<uint>> GatheringBasesByItem,
        Dictionary<uint, uint> GatheringPointByBase,
        Dictionary<uint, List<uint>> FishingSpotsByItem,
        Dictionary<uint, List<uint>> SpearfishingSpotsByItem,
        Dictionary<uint, uint> FishNoteByItem,
        Dictionary<uint, List<GcSealOffer>> SealOffersByItem,
        Dictionary<uint, uint> QuartermasterByCompany,
        Dictionary<uint, List<uint>> VenturesByItem,
        Dictionary<uint, uint> VentureTaskByNormal);

    private AcquisitionIndex BuildAcquisitionIndex() => new(
        BuildRecipeLookup(),
        BuildShopItemLookup(),
        BuildShopNpcLookup(),
        BuildSpecialShopItemLookup(),
        BuildSpecialShopNpcLookup(),
        gatheringBases.Value,
        BuildGatheringPointLookup(),
        BuildFishingSpotLookup(),
        BuildSpearfishingLookup(),
        BuildFishNoteLookup(),
        BuildSealOfferLookup(),
        BuildQuartermasterLookup(),
        BuildVentureItemLookup(),
        BuildVentureTaskLookup());

    // Result item -> Recipe rows.
    private Dictionary<uint, List<uint>> BuildRecipeLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var recipe in Sheet<Recipe>())
        {
            if (recipe.ItemResult.RowId != 0)
                Add(lookup, recipe.ItemResult.RowId, recipe.RowId);
        }

        return lookup;
    }

    // Item -> GilShop rows selling it (GilShopItem is a subrow sheet keyed by
    // shop).
    private Dictionary<uint, List<uint>> BuildShopItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var group in SubrowSheet<GilShopItem>())
        {
            foreach (var sub in group)
            {
                if (sub.Item.RowId != 0)
                    Add(lookup, sub.Item.RowId, group.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildShopNpcLookup()
    {
        // Gil shop event ids live in the 0x40000 block of ENpcData.
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId is >= 0x40000 and < 0x50000)
                    Add(lookup, handler.RowId, npcBase.RowId);
            }
        }

        return lookup;
    }

    // Item -> SpecialShop rows that hand it out in any offer.
    private Dictionary<uint, List<uint>> BuildSpecialShopItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var shop in Sheet<SpecialShop>())
        {
            foreach (var entry in shop.Item)
            {
                foreach (var receive in entry.ReceiveItems)
                {
                    if (receive.Item.RowId != 0)
                        Add(lookup, receive.Item.RowId, shop.RowId);
                }
            }
        }

        return lookup;
    }

    // A direct 0x1B0000 handler reaches only ~31% of offer-carrying shops
    // (probed 2026-09-01: 375 of 1215). The rest hang off menu and aggregator
    // handlers - TopicSelect pages, InclusionShop category trees, PreHandler
    // gates, CustomTalk scripts - and bicolor-gemstone FateShops are keyed by
    // the vendor NPC row itself. Shops no mechanism reaches are dev leftovers
    // and superseded content; leaving them unmapped keeps them out of results.
    private Dictionary<uint, List<uint>> BuildSpecialShopNpcLookup()
    {
        var shops = new HashSet<uint>();
        foreach (var shop in Sheet<SpecialShop>())
            shops.Add(shop.RowId);

        var preTargets = new Dictionary<uint, uint>();
        foreach (var pre in Sheet<PreHandler>())
        {
            if (pre.Target.RowId != 0)
                preTargets.TryAdd(pre.RowId, pre.Target.RowId);
        }

        var topicEntries = new Dictionary<uint, List<uint>>();
        foreach (var topic in Sheet<TopicSelect>())
        {
            foreach (var entry in topic.Shop)
            {
                if (entry.RowId != 0)
                    Add(topicEntries, topic.RowId, entry.RowId);
            }
        }

        var inclusionShops = new Dictionary<uint, List<uint>>();
        foreach (var inclusion in Sheet<InclusionShop>())
        {
            foreach (var category in inclusion.Category)
            {
                if (category.ValueNullable?.InclusionShopSeries.ValueNullable is not { } series)
                    continue;

                foreach (var sub in series)
                {
                    if (sub.SpecialShop.RowId != 0)
                        Add(inclusionShops, inclusion.RowId, sub.SpecialShop.RowId);
                }
            }
        }

        var customTalkShops = new Dictionary<uint, List<uint>>();
        foreach (var talk in Sheet<CustomTalk>())
        {
            foreach (var script in talk.Script)
            {
                if (shops.Contains(script.ScriptArg))
                    Add(customTalkShops, talk.RowId, script.ScriptArg);
            }
        }

        foreach (var group in SubrowSheet<CustomTalkNestHandlers>())
        {
            foreach (var sub in group)
            {
                if (shops.Contains(sub.NestHandler.RowId))
                    Add(customTalkShops, group.RowId, sub.NestHandler.RowId);
            }
        }

        var lookup = new Dictionary<uint, List<uint>>();

        // Deepest observed chain is TopicSelect -> PreHandler -> shop; the
        // budget only exists to stop a malformed self-referencing handler.
        void Resolve(uint handlerId, uint npcId, int depth)
        {
            if (shops.Contains(handlerId))
            {
                Add(lookup, handlerId, npcId);
                return;
            }

            if (depth == 0)
                return;

            if (preTargets.TryGetValue(handlerId, out var target))
                Resolve(target, npcId, depth - 1);

            if (topicEntries.TryGetValue(handlerId, out var entries))
            {
                foreach (var entry in entries)
                    Resolve(entry, npcId, depth - 1);
            }

            if (inclusionShops.TryGetValue(handlerId, out var included))
            {
                foreach (var shopId in included)
                    Add(lookup, shopId, npcId);
            }

            if (customTalkShops.TryGetValue(handlerId, out var scripted))
            {
                foreach (var shopId in scripted)
                    Add(lookup, shopId, npcId);
            }
        }

        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId != 0)
                    Resolve(handler.RowId, npcBase.RowId, 3);
            }
        }

        foreach (var fateShop in Sheet<FateShop>())
        {
            if (fateShop.RowId == 0)
                continue;

            foreach (var shopRef in fateShop.SpecialShop)
            {
                if (shopRef.RowId != 0)
                    Add(lookup, shopRef.RowId, fateShop.RowId);
            }
        }

        return lookup;
    }

    // Item -> GatheringPointBase rows (node templates). GatheringItem sits in
    // between.
    private Dictionary<uint, List<uint>> BuildGatheringItemLookup()
    {
        var itemsByGatheringItem = new Dictionary<uint, uint>();
        foreach (var row in Sheet<GatheringItem>())
        {
            if (row.Item.RowId != 0)
                itemsByGatheringItem.TryAdd(row.RowId, row.Item.RowId);
        }

        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var pointBase in Sheet<GatheringPointBase>())
        {
            foreach (var entry in pointBase.Item)
            {
                if (itemsByGatheringItem.TryGetValue(entry.RowId, out var itemId))
                    Add(lookup, itemId, pointBase.RowId);
            }
        }

        return lookup;
    }

    // GatheringPointBase -> the first placed GatheringPoint, which carries the
    // zone.
    private Dictionary<uint, uint> BuildGatheringPointLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var point in Sheet<GatheringPoint>())
        {
            if (point.GatheringPointBase.RowId != 0 && point.TerritoryType.RowId != 0 && point.PlaceName.RowId != 0)
                lookup.TryAdd(point.GatheringPointBase.RowId, point.RowId);
        }

        return lookup;
    }

    // Fish -> FishingSpot rows; spots without a zone or place name are skipped.
    private Dictionary<uint, List<uint>> BuildFishingSpotLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var spot in Sheet<FishingSpot>())
        {
            if (spot.TerritoryType.RowId == 0 || spot.PlaceName.RowId == 0)
                continue;

            foreach (var item in spot.Item)
            {
                if (item.RowId != 0)
                    Add(lookup, item.RowId, spot.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildSpearfishingLookup()
    {
        // Spearfishing bases fill their GatheringPointBase.Item slots with
        // SpearfishingItem row ids (20000+), disjoint from GatheringItem ids,
        // so the regular gathering index never sees them.
        var itemBySpearRow = new Dictionary<uint, uint>();
        foreach (var row in Sheet<SpearfishingItem>())
        {
            if (row.Item.RowId != 0)
                itemBySpearRow.TryAdd(row.RowId, row.Item.RowId);
        }

        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var spot in Sheet<SpearfishingNotebook>())
        {
            var pointBase = Sheet<GatheringPointBase>().GetRowOrDefault(spot.GatheringPointBase.RowId);
            if (pointBase == null || spot.TerritoryType.RowId == 0 || spot.PlaceName.RowId == 0)
                continue;

            foreach (var entry in pointBase.Value.Item)
            {
                if (itemBySpearRow.TryGetValue(entry.RowId, out var itemId))
                    Add(lookup, itemId, spot.RowId);
            }
        }

        return lookup;
    }

    // Fish -> FishParameter row holding its log text.
    private Dictionary<uint, uint> BuildFishNoteLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var row in Sheet<FishParameter>())
        {
            if (row.Item.RowId != 0 && !row.Text.IsEmpty)
                lookup.TryAdd(row.Item.RowId, row.RowId);
        }

        return lookup;
    }

    // One GC seal shop entry: which company sells it, for how many seals, from
    // which rank.
    private sealed record GcSealOffer(uint GrandCompany, uint Seals, uint Rank);

    // Item -> GC seal offers. The subrow parent is a GCScripShopCategory row,
    // which names the company.
    private Dictionary<uint, List<GcSealOffer>> BuildSealOfferLookup()
    {
        var lookup = new Dictionary<uint, List<GcSealOffer>>();
        var categories = Sheet<GCScripShopCategory>();
        foreach (var group in SubrowSheet<GCScripShopItem>())
        {
            var company = categories.GetRowOrDefault(group.RowId)?.GrandCompany.RowId ?? 0;
            if (company == 0)
                continue;

            foreach (var sub in group)
            {
                if (sub.Item.RowId == 0)
                    continue;

                if (!lookup.TryGetValue(sub.Item.RowId, out var list))
                    lookup[sub.Item.RowId] = list = [];
                list.Add(new GcSealOffer(company, sub.CostGCSeals, sub.RequiredGrandCompanyRank.RowId));
            }
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildQuartermasterLookup()
    {
        // GC shop event ids live in the 0x160000 block of ENpcData; each
        // company has exactly one quartermaster NPC.
        var companyByShop = new Dictionary<uint, uint>();
        foreach (var shop in Sheet<GCShop>())
        {
            if (shop.GrandCompany.RowId != 0)
                companyByShop.TryAdd(shop.RowId, shop.GrandCompany.RowId);
        }

        var lookup = new Dictionary<uint, uint>();
        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId is >= 0x160000 and < 0x170000 &&
                    companyByShop.TryGetValue(handler.RowId, out var company))
                    lookup.TryAdd(company, npcBase.RowId);
            }
        }

        return lookup;
    }

    // Item -> RetainerTaskNormal rows (the item-specific ventures).
    private Dictionary<uint, List<uint>> BuildVentureItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var normal in Sheet<RetainerTaskNormal>())
        {
            if (normal.Item.RowId != 0)
                Add(lookup, normal.Item.RowId, normal.RowId);
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildVentureTaskLookup()
    {
        // RetainerTask.Task points at RetainerTaskNormal (or RetainerTaskRandom
        // for the exploration ventures, which never award a specific item).
        var lookup = new Dictionary<uint, uint>();
        foreach (var task in Sheet<RetainerTask>())
        {
            if (task.IsRandom || task.Task.RowId == 0)
                continue;

            if (!lookup.TryGetValue(task.Task.RowId, out var existing) || task.RowId < existing)
                lookup[task.Task.RowId] = task.RowId;
        }

        return lookup;
    }
}
