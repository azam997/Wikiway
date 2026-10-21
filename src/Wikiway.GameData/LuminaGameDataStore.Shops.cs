using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Shop sources for an item card's ACQUISITION block: gil vendors, special-shop
// exchanges and Grand Company seal vendors. Finding the shops that stock the
// item is quick. Finding the NPCs who run them means walking every NPC's event
// handlers (ENpcBase), 25-45 ms per walk once the sheets are loaded. That's
// the slowest part of an item card, so it only runs for items some shop
// actually stocks.
public sealed partial class LuminaGameDataStore
{
    // Gil vendors with their positions; every vendor charges the item's
    // PriceMid. GilShopItem is a subrow sheet keyed by GilShop row.
    private List<VendorSource> BuildVendors(uint itemRowId, uint gilPrice)
    {
        var vendors = new List<VendorSource>();
        var shopIds = new List<uint>();
        foreach (var group in SubrowSheet<GilShopItem>())
        {
            if (group.Any(sub => sub.Item.RowId == itemRowId))
                shopIds.Add(group.RowId);
        }

        if (shopIds.Count == 0)
            return vendors;

        var npcsByShop = NpcsByHandler(shopIds.ToHashSet());
        var seen = new HashSet<(string Name, uint MapId, float X, float Y)>();
        foreach (var shopId in shopIds)
        {
            if (!npcsByShop.TryGetValue(shopId, out var npcIds))
                continue;

            foreach (var npcId in npcIds)
            {
                var resident = Sheet<ENpcResident>().GetRowOrDefault(npcId);
                if (resident == null || resident.Value.Singular.IsEmpty)
                    continue;

                var name = TitleCase(resident.Value.Singular.ExtractText());
                var location = FindLocation(npcId);
                var key = location is { } loc
                    ? (name.ToLowerInvariant(), loc.MapId, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1))
                    : (name.ToLowerInvariant(), 0u, 0f, 0f);
                if (seen.Add(key))
                    vendors.Add(new VendorSource(name, location, gilPrice));
            }
        }

        // Housing-district copies have no Level row; hide them when a
        // located copy of the same vendor exists.
        var located = vendors.Where(v => v.Location != null)
            .Select(v => v.NpcName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        vendors.RemoveAll(v => v.Location == null && located.Contains(v.NpcName));
        return vendors;
    }

    // Handler id -> the NPCs (ENpcBase rows, in sheet order) whose ENpcData
    // lists it. Gil shops and GC shops sit directly in ENpcData.
    private Dictionary<uint, List<uint>> NpcsByHandler(HashSet<uint> handlerIds)
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handlerIds.Contains(handler.RowId))
                    Add(lookup, handler.RowId, npcBase.RowId);
            }
        }

        return lookup;
    }

    // Special-shop exchanges (tomestones, scrips, item trades): one line per
    // shop and NPC, deduplicated by shop name and NPC position.
    private List<ExchangeSource> BuildExchanges(uint itemRowId)
    {
        var exchanges = new List<ExchangeSource>();
        var offered = new List<(uint ShopId, string ShopName, List<IReadOnlyList<ItemAmount>> Costs)>();
        foreach (var shop in Sheet<SpecialShop>())
        {
            var costs = BuildExchangeCosts(shop, itemRowId);
            if (costs.Count > 0)
                offered.Add((shop.RowId, shop.Name.ExtractText(), costs));
        }

        if (offered.Count == 0)
            return exchanges;

        var npcsByShop = SpecialShopNpcs(offered.Select(o => o.ShopId).ToHashSet());
        var seen = new HashSet<(string Shop, string Npc, uint MapId, float X, float Y)>();
        foreach (var (shopId, shopName, costs) in offered)
        {
            if (!npcsByShop.TryGetValue(shopId, out var npcIds))
                continue;

            foreach (var npcId in npcIds)
            {
                var resident = Sheet<ENpcResident>().GetRowOrDefault(npcId);
                if (resident == null || resident.Value.Singular.IsEmpty)
                    continue;

                var npcName = TitleCase(resident.Value.Singular.ExtractText());
                var location = FindLocation(npcId);
                var key = location is { } loc
                    ? (shopName.ToLowerInvariant(), npcName.ToLowerInvariant(), loc.MapId, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1))
                    : (shopName.ToLowerInvariant(), npcName.ToLowerInvariant(), 0u, 0f, 0f);
                if (seen.Add(key))
                    exchanges.Add(new ExchangeSource(shopName.Length > 0 ? shopName : npcName, npcName, location, costs));
            }
        }

        return exchanges;
    }

    private static List<IReadOnlyList<ItemAmount>> BuildExchangeCosts(SpecialShop shop, uint itemRowId)
    {
        // Each shop entry is one offer; its cost slots are all required together.
        // Tomestone stand-in slots resolve to no named item and are skipped.
        var costs = new List<IReadOnlyList<ItemAmount>>();
        foreach (var entry in shop.Item)
        {
            if (!entry.ReceiveItems.Any(r => r.Item.RowId == itemRowId))
                continue;

            var parts = new List<ItemAmount>();
            foreach (var cost in entry.ItemCosts)
            {
                if (cost.ItemCost.ValueNullable is { } costItem && !costItem.Name.IsEmpty)
                    parts.Add(new ItemAmount(costItem.Name.ExtractText(), cost.CurrencyCost));
            }

            if (parts.Count > 0)
                costs.Add(parts);
        }

        return costs;
    }

    // SpecialShop row -> the NPCs who open it, for the shops asked about.
    // A direct 0x1B0000 handler reaches only ~31% of offer-carrying shops
    // (probed 2026-09-01: 375 of 1215). The rest hang off menu and aggregator
    // handlers - TopicSelect pages, InclusionShop category trees, PreHandler
    // gates, CustomTalk scripts - and bicolor-gemstone FateShops are keyed by
    // the vendor NPC row itself. Shops no mechanism reaches are dev leftovers
    // and superseded content; leaving them unmapped keeps them out of results.
    private Dictionary<uint, List<uint>> SpecialShopNpcs(HashSet<uint> shops)
    {
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
                    if (shops.Contains(sub.SpecialShop.RowId))
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
                if (shops.Contains(shopRef.RowId))
                    Add(lookup, shopRef.RowId, fateShop.RowId);
            }
        }

        return lookup;
    }

    // One GC seal shop entry: which company sells it, for how many seals, from
    // which rank.
    private sealed record GcSealOffer(uint GrandCompany, uint Seals, uint Rank);

    // Grand Company quartermasters selling the item for seals, with the minimum
    // rank when there is one. GCScripShopItem is a subrow sheet whose parent
    // GCScripShopCategory row names the company.
    private List<SealVendorSource> BuildSealVendors(uint itemRowId)
    {
        var vendors = new List<SealVendorSource>();
        var offers = new List<GcSealOffer>();
        var categories = Sheet<GCScripShopCategory>();
        foreach (var group in SubrowSheet<GCScripShopItem>())
        {
            foreach (var sub in group)
            {
                if (sub.Item.RowId != itemRowId)
                    continue;

                var company = categories.GetRowOrDefault(group.RowId)?.GrandCompany.RowId ?? 0;
                if (company != 0)
                    offers.Add(new GcSealOffer(company, sub.CostGCSeals, sub.RequiredGrandCompanyRank.RowId));
            }
        }

        if (offers.Count == 0)
            return vendors;

        var quartermasters = Quartermasters(offers.Select(o => o.GrandCompany).ToHashSet());
        var seen = new HashSet<GcSealOffer>();
        foreach (var offer in offers)
        {
            if (!seen.Add(offer) || !quartermasters.TryGetValue(offer.GrandCompany, out var npcId))
                continue;

            var resident = Sheet<ENpcResident>().GetRowOrDefault(npcId);
            if (resident == null || resident.Value.Singular.IsEmpty)
                continue;

            vendors.Add(new SealVendorSource(
                TitleCase(resident.Value.Singular.ExtractText()),
                FindLocation(npcId),
                offer.Seals,
                RankName(offer.GrandCompany, offer.Rank)));
        }

        return vendors;
    }

    // Company -> its quartermaster: the first NPC (lowest ENpcBase row) that
    // carries one of the company's GCShop handlers. Each company has exactly
    // one.
    private Dictionary<uint, uint> Quartermasters(HashSet<uint> companies)
    {
        var companyByShop = new Dictionary<uint, uint>();
        foreach (var shop in Sheet<GCShop>())
        {
            if (companies.Contains(shop.GrandCompany.RowId))
                companyByShop.TryAdd(shop.RowId, shop.GrandCompany.RowId);
        }

        var lookup = new Dictionary<uint, uint>();
        foreach (var (shopId, npcIds) in NpcsByHandler(companyByShop.Keys.ToHashSet()))
        {
            var company = companyByShop[shopId];
            if (!lookup.TryGetValue(company, out var existing) || npcIds[0] < existing)
                lookup[company] = npcIds[0];
        }

        return lookup;
    }

    // Rank display names live in per-company text sheets; the male and female
    // variants carry the same strings. Rank 1 means any member, not worth a label.
    private string RankName(uint grandCompany, uint rankRowId)
    {
        if (rankRowId <= 1)
            return "";

        return grandCompany switch
        {
            1 => Sheet<GCRankLimsaMaleText>().GetRowOrDefault(rankRowId)?.Singular.ExtractText() ?? "",
            2 => Sheet<GCRankGridaniaMaleText>().GetRowOrDefault(rankRowId)?.Singular.ExtractText() ?? "",
            3 => Sheet<GCRankUldahMaleText>().GetRowOrDefault(rankRowId)?.Singular.ExtractText() ?? "",
            _ => "",
        };
    }
}
