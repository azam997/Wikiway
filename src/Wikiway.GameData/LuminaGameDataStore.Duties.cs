using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Duties and unlockable areas (ContentFinderCondition, or CFC, rows plus a few
// curated zones): classification, unlock quest resolution, and the solo-duty
// lookup used by the toast.
public sealed partial class LuminaGameDataStore
{
    // Solo := quest battles or Masked Carnivale. ContentMemberType can't tell:
    // quest battles carry all-zero role counts and named CFC party sizes are
    // only ever 0/4/8 (probed against 7.3 sheets; 116 solo rows). A canary test
    // on the solo-duty count fails if a patch adds a new solo content type.
    private const uint ContentTypeQuestBattles = 7;
    private const uint ContentTypeMaskedCarnivale = 27;
    private const byte ContentLinkTypeInstanceContent = 1;
    private const byte ContentLinkTypePublicContent = 3;

    // Field-operation zones: Diadem, Eureka, Save the Queen, Occult Crescent.
    // Public-content link alone is too broad (GATEs are public content too).
    private static readonly uint[] AreaContentTypes = [16, 26, 29, 38];

    // These field-area CFC rows carry no quest-typed UnlockCriteria; the real
    // gate quests were checked against the sheets, and a canary test keeps
    // checking them.
    private static readonly Dictionary<uint, uint> FieldAreaUnlockQuests = new()
    {
        [735] = 69477, // the Bozjan Southern Front <- Where Eagles Nest
        [742] = 69208, // the Diadem <- Towards the Firmament
        [753] = 69208, // the Diadem <- Towards the Firmament
        [760] = 69562, // Delubrum Reginae <- Fit for a Queen
        [761] = 69562, // Delubrum Reginae (Savage) <- Fit for a Queen
        [778] = 69620, // Zadnor <- A New Playing Field
    };

    // Unlockable zones with no ContentFinderCondition row; synthetic ids sit
    // far above the CFC sheet so they can never collide with real rows.
    private const uint CuratedZoneBase = 1_000_000;

    private static readonly (uint RowId, string Name, string Kind, uint QuestId)[] CuratedZones =
    [
        (CuratedZoneBase + 0, "The Firmament", "Ishgardian Restoration", 69208),  // Towards the Firmament
        (CuratedZoneBase + 1, "The Gold Saucer", "Gold Saucer", 65970),           // It Could Happen to You
        (CuratedZoneBase + 2, "Island Sanctuary", "Island Sanctuary", 70179),     // Seeking Sanctuary
    ];

    // The name for the solo-duty toast, or null when the territory isn't a solo
    // duty.
    public string? FindSoloDutyName(uint territoryTypeId)
    {
        // Game-thread callback (TerritoryChanged): never build or wait on the
        // lazy lookup here. Before WarmAll has built it, skip the toast.
        if (dutyByTerritory.ValueIfBuilt is not { } lookup || !lookup.TryGetValue(territoryTypeId, out var rowId))
            return null;

        var row = Sheet<ContentFinderCondition>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return row.Value.ContentType.RowId is ContentTypeQuestBattles or ContentTypeMaskedCarnivale
            ? row.Value.Name.ExtractText()
            : null;
    }

    // Content gated behind a side quest is listed as Unlockable rather than
    // Duty; content behind the MSQ stays a Duty.
    private bool IsUnlockable(ContentFinderCondition row)
    {
        var questId = ResolveUnlockQuestId(row);
        return questId != 0 && !IsMainScenario(questId);
    }

    // The quest that unlocks a duty, from three places in order: the row's own
    // UnlockCriteria, the curated field-area table, then the quest-side script
    // args. 0 when none applies.
    private uint ResolveUnlockQuestId(ContentFinderCondition row)
    {
        if (row.UnlockCriteria.Is<Quest>() &&
            row.UnlockCriteria.GetValueOrDefault<Quest>() is { } unlock && !unlock.Name.IsEmpty)
            return row.UnlockCriteria.RowId;

        if (FieldAreaUnlockQuests.TryGetValue(row.RowId, out var curated))
            return curated;

        // Most instanced content carries no UnlockCriteria; its gate quest is
        // named on the quest side instead, via INSTANCEDUNGEON script args.
        return row.ContentLinkType == ContentLinkTypeInstanceContent &&
               unlockQuestByInstance.Value.TryGetValue(row.Content.RowId, out var scripted)
            ? scripted
            : 0;
    }

    // InstanceContent id -> unlocking Quest row, read from quests'
    // INSTANCEDUNGEON script parameters.
    private Dictionary<uint, uint> BuildInstanceUnlockLookup()
    {
        // Several quests can reference one instance (relic re-checks, later
        // chains); the lowest quest id is the original unlock.
        var lookup = new Dictionary<uint, uint>();
        foreach (var quest in Sheet<Quest>())
        {
            if (quest.Name.IsEmpty)
                continue;

            foreach (var param in quest.QuestParams)
            {
                if (param.ScriptArg == 0)
                    continue;

                if (!IsInstanceUnlockInstruction(param.ScriptInstruction.ExtractText()))
                    continue;

                if (!lookup.TryGetValue(param.ScriptArg, out var existing) || quest.RowId < existing)
                    lookup[param.ScriptArg] = quest.RowId;
            }
        }

        return lookup;
    }

    // "INSTANCEDUNGEON", optionally slot-numbered; named variants like
    // "INSTANCEDUNGEON_W" are labels, not unlock args.
    private static bool IsInstanceUnlockInstruction(string text)
    {
        const string stem = "INSTANCEDUNGEON";
        if (!text.StartsWith(stem, StringComparison.Ordinal))
            return false;

        for (var i = stem.Length; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
                return false;
        }

        return true;
    }

    // Duty card: content type, level and item level, solo and high-end flags,
    // and the unlock quest with its chain start and MSQ gate.
    public DutyEntity? GetDuty(uint rowId)
    {
        if (rowId >= CuratedZoneBase)
            return GetCuratedZone(rowId);

        var row = Sheet<ContentFinderCondition>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var duty = row.Value;
        var solo = duty.ContentType.RowId is ContentTypeQuestBattles or ContentTypeMaskedCarnivale;

        var unlockId = ResolveUnlockQuestId(duty);
        var unlockEntity = unlockId != 0 ? GetQuest(unlockId) : null;

        return new DutyEntity(
            rowId,
            duty.Name.ExtractText(),
            duty.ContentType.ValueNullable?.Name.ExtractText() ?? "",
            duty.ClassJobLevelRequired,
            duty.ItemLevelRequired,
            solo,
            duty.HighEndDuty,
            duty.TerritoryType.RowId)
        {
            UnlockQuest = unlockEntity != null ? new QuestLink(unlockId, unlockEntity.Name) : null,
            ChainStart = unlockEntity?.UnlockChains.FirstOrDefault(c => c.Steps.Count > 0)?.Steps[0].Quest,
            MsqGate = ResolveMsqGate(unlockId, unlockEntity),
            FieldArea = IsFieldArea(duty),
            Optional = unlockEntity is { MainScenario: false },
        };
    }

    // The gate nearest the unlock quest (last in play order) is the most
    // advanced main-scenario requirement; MSQ linearity makes testing only
    // that one sufficient.
    private static MsqGate? ResolveMsqGate(uint unlockId, QuestEntity? unlock) =>
        unlock == null ? null
        : unlock.MainScenario ? new MsqGate(new QuestLink(unlockId, unlock.Name), unlock.Expansion)
        : unlock.UnlockChains.Select(c => c.Gate).LastOrDefault(g => g != null);

    // Duty-shaped cards for the curated zones that have no
    // ContentFinderCondition row.
    private DutyEntity? GetCuratedZone(uint rowId)
    {
        foreach (var zone in CuratedZones)
        {
            if (zone.RowId != rowId)
                continue;

            if (GetQuest(zone.QuestId) is not { } unlock)
                return null;

            return new DutyEntity(rowId, zone.Name, zone.Kind, 0, 0, false, false, 0)
            {
                UnlockQuest = new QuestLink(zone.QuestId, unlock.Name),
                ChainStart = unlock.UnlockChains.FirstOrDefault(c => c.Steps.Count > 0)?.Steps[0].Quest,
                MsqGate = ResolveMsqGate(zone.QuestId, unlock),
                FieldArea = true,
                Optional = !unlock.MainScenario,
            };
        }

        return null;
    }

    private static bool IsFieldArea(ContentFinderCondition row) =>
        row.ContentLinkType == ContentLinkTypePublicContent && AreaContentTypes.Contains(row.ContentType.RowId);

    // Duty card for a territory id (the first named CFC row that uses it).
    public DutyEntity? FindDutyByTerritory(uint territoryTypeId) =>
        dutyByTerritory.Value.TryGetValue(territoryTypeId, out var rowId) ? GetDuty(rowId) : null;

    private Dictionary<uint, uint> BuildDutyTerritoryLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var duty in Sheet<ContentFinderCondition>())
        {
            if (duty.TerritoryType.RowId != 0 && !duty.Name.IsEmpty)
                lookup.TryAdd(duty.TerritoryType.RowId, duty.RowId);
        }

        return lookup;
    }
}
