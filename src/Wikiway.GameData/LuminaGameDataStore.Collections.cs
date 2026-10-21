using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Collectibles: mounts, minions, orchestrion rolls, Triple Triad cards and
// emotes. Each card links back to the item that teaches it, found by scanning
// the Item sheet's item actions (up to ~2 ms per card once loaded).
public sealed partial class LuminaGameDataStore
{
    // ItemAction type discriminators (ItemAction.Action.RowId); Data[0] names
    // the taught row, except orchestrion rolls where Item.AdditionalData does
    // and emote manuals where Data[0] is the Emote.UnlockLink unlock bit.
    // Probed 2026-09-01 against 7.3 sheets (Aithon Whistle, Wind-up Cursor,
    // A Cold Wind Orchestrion Roll, Momodi Modi Card, The Bomb Dance).
    private const uint ItemActionMount = 1322;
    private const uint ItemActionMinion = 853;
    private const uint ItemActionOrchestrion = 25183;
    private const uint ItemActionTripleTriadCard = 3357;
    private const uint ItemActionUnlockBit = 2633;

    // The first item whose ItemAction teaches the given row. The card shows
    // its name and icon; its acquisition sources load when the card is opened,
    // like any item's.
    private ItemEntity? TeachingItem(uint actionType, uint taughtRowId)
    {
        foreach (var item in Sheet<Item>())
        {
            if (item.ItemAction.RowId == 0 || item.Name.IsEmpty ||
                item.ItemAction.ValueNullable is not { } action || action.Action.RowId != actionType)
                continue;

            var taught = actionType == ItemActionOrchestrion ? item.AdditionalData.RowId : action.Data[0];
            if (taught == taughtRowId)
                return GetItem(item.RowId);
        }

        return null;
    }

    public MountEntity? GetMount(uint rowId)
    {
        var row = Sheet<Mount>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        // DescriptionEnhanced is the mount-guide lore text; plain Description
        // is the summon-action blurb and Tooltip a one-line joke.
        var transient = Sheet<MountTransient>().GetRowOrDefault(rowId);
        return new MountEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon)
        {
            Description = transient?.DescriptionEnhanced.ExtractText() ?? "",
            TeachingItem = TeachingItem(ItemActionMount, rowId),
        };
    }

    public MinionEntity? GetMinion(uint rowId)
    {
        var row = Sheet<Companion>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        var transient = Sheet<CompanionTransient>().GetRowOrDefault(rowId);
        return new MinionEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon)
        {
            Description = transient?.DescriptionEnhanced.ExtractText() ?? "",
            BattleStats = row.Value.HP > 0 && transient != null
                ? new MinionBattleStats(
                    row.Value.HP,
                    transient.Value.Attack,
                    transient.Value.Defense,
                    transient.Value.Speed,
                    row.Value.Cost,
                    transient.Value.SpecialActionName.ExtractText())
                : null,
            TeachingItem = TeachingItem(ItemActionMinion, rowId),
        };
    }

    public OrchestrionEntity? GetOrchestrion(uint rowId)
    {
        var row = Sheet<Orchestrion>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var ui = Sheet<OrchestrionUiparam>().GetRowOrDefault(rowId);
        return new OrchestrionEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            ui?.OrchestrionCategory.ValueNullable?.Name.ExtractText() ?? "")
        {
            TeachingItem = TeachingItem(ItemActionOrchestrion, rowId),
        };
    }

    // Acquisition column meaning depends on the obtain type (probed 2026-09-01
    // across all 476 cards): NPC-win types carry an ENpcResident id plus a
    // Level row, duty-drop types a ContentFinderCondition id. Other types hold
    // ids from unrelated sheets, so they must not be resolved.
    private static readonly uint[] CardObtainNpcTypes = [6, 10];
    private static readonly uint[] CardObtainDutyTypes = [2, 3];

    // Stats, rarity and where the card comes from: an NPC to beat, a duty drop,
    // or the game's own obtain text.
    public TripleTriadCardEntity? GetTripleTriadCard(uint rowId)
    {
        var row = Sheet<TripleTriadCard>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var resident = Sheet<TripleTriadCardResident>().GetRowOrDefault(rowId);
        if (resident == null)
            return null;

        var r = resident.Value;
        var obtainType = r.AcquisitionType.RowId;

        var npcName = "";
        MapLocation? npcLocation = null;
        if (CardObtainNpcTypes.Contains(obtainType) &&
            Sheet<ENpcResident>().GetRowOrDefault(r.Acquisition.RowId) is { } npc && !npc.Singular.IsEmpty)
        {
            npcName = TitleCase(npc.Singular.ExtractText());
            npcLocation = Sheet<Level>().GetRowOrDefault(r.Location.RowId) is { } level
                ? ToMapLocation(level)
                : FindLocation(r.Acquisition.RowId);
        }

        var dutyName = "";
        if (CardObtainDutyTypes.Contains(obtainType) &&
            Sheet<ContentFinderCondition>().GetRowOrDefault(r.Acquisition.RowId) is { } duty && !duty.Name.IsEmpty)
            dutyName = duty.Name.ExtractText();

        return new TripleTriadCardEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            r.Top,
            r.Bottom,
            r.Left,
            r.Right,
            r.TripleTriadCardRarity.ValueNullable?.Stars ?? 0,
            r.TripleTriadCardType.ValueNullable?.Name.ExtractText() ?? "",
            r.SaleValue)
        {
            ObtainText = CleanObtainText(r.AcquisitionType.ValueNullable?.Text.ValueNullable?.Text.ExtractText() ?? ""),
            NpcName = npcName,
            NpcLocation = npcLocation,
            DutyName = dutyName,
            TeachingItem = TeachingItem(ItemActionTripleTriadCard, rowId),
        };
    }

    // Obtain labels are Addon strings with their payloads stripped, so several
    // come out as bare whitespace or end in a dangling colon.
    private static string CleanObtainText(string text)
    {
        var cleaned = text.Trim().TrimEnd(':', '.');
        return cleaned.Length > 1 ? cleaned : "";
    }

    private const uint QuestRowBlockStart = 0x10000;
    private const uint QuestRowBlockEnd = 0x20000;

    // An emote is unlocked by either a quest or an item (a manual).
    public EmoteEntity? GetEmote(uint rowId)
    {
        var row = Sheet<Emote>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        // UnlockLink is a quest row only in the quest id block; other values
        // are unlock bits that a manual item's ItemAction may set. Bits taught
        // neither way (old achievement-era emotes) stay blank for the wiki.
        var link = row.Value.UnlockLink;
        QuestLink? unlockQuest = null;
        if (link is >= QuestRowBlockStart and < QuestRowBlockEnd &&
            Sheet<Quest>().GetRowOrDefault(link) is { } quest && !quest.Name.IsEmpty)
            unlockQuest = new QuestLink(link, quest.Name.ExtractText().Trim());

        return new EmoteEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.TextCommand.ValueNullable?.Command.ExtractText() ?? "",
            row.Value.EmoteCategory.ValueNullable?.Name.ExtractText() ?? "")
        {
            UnlockQuest = unlockQuest,
            TeachingItem = link == 0 ? null : TeachingItem(ItemActionUnlockBit, link),
        };
    }
}
