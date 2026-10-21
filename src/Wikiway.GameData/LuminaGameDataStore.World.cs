using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Standalone world kinds with simple one-row cards: sightseeing vistas, hunt
// marks, aether-current zones, FATEs, levequests and achievements.
public sealed partial class LuminaGameDataStore
{
    // Sightseeing log entry: position, required emote, ET window, hint and
    // lore.
    public VistaEntity? GetVista(uint rowId)
    {
        var row = Sheet<Adventure>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        // Impression is the log's riddle hint; Description the location lore
        // (probed 2026-09-01: Barracuda Piers, Seasong Grotto).
        var emote = row.Value.Emote.ValueNullable;
        var command = emote?.TextCommand.ValueNullable?.Command.ExtractText() ?? "";
        return new VistaEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Impression.ExtractText(),
            row.Value.Description.ExtractText())
        {
            Location = row.Value.Level.ValueNullable is { } level ? ToMapLocation(level) : null,
            Region = row.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
            Emote = command.Length > 0 ? command : emote?.Name.ExtractText() ?? "",
            TimeWindow = VistaTimeWindow(row.Value.MinTime, row.Value.MaxTime),
        };
    }

    // Vista windows are ET clock values with an inclusive end (800-1159 is the
    // 8:00-12:00 window); 0-0 marks the always-available vistas.
    private static string VistaTimeWindow(ushort minTime, ushort maxTime)
    {
        if (minTime == 0 && maxTime == 0)
            return "";

        var end = (EorzeaClock.ToMinutes(maxTime) + 1) % EorzeaClock.Day;
        return $"{EorzeaClock.Label(EorzeaClock.ToMinutes(minTime))}-{EorzeaClock.Label(end == 0 ? EorzeaClock.Day : end)} ET";
    }

    // Hunt mark with its rank; only B ranks have a zone in the sheets (from the
    // hunt bills).
    public HuntMarkEntity? GetHuntMark(uint rowId)
    {
        var row = Sheet<NotoriousMonster>().GetRowOrDefault(rowId);
        if (row == null || row.Value.BNpcName.ValueNullable is not { } mark || mark.Singular.IsEmpty)
            return null;

        return new HuntMarkEntity(rowId, TitleCase(mark.Singular.ExtractText()), RankLetter(row.Value.Rank))
        {
            ZoneName = MarkZone(row.Value.BNpcName.RowId),
        };
    }

    private static string RankLetter(byte rank) => rank switch
    {
        1 => "B",
        2 => "A",
        3 => "S",
        _ => "",
    };

    private string MarkZone(uint bnpcNameRowId)
    {
        // MobHuntTarget is the hunt-bill sheet, so only billed marks (the B
        // ranks) appear; their PlaceName column holds the log sub-zone and is
        // empty for marks, so the zone comes from the Map row instead.
        foreach (var target in Sheet<MobHuntTarget>())
        {
            if (target.Name.RowId == 0 || target.Name.RowId != bnpcNameRowId)
                continue;

            var zone = target.Map.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (zone.Length > 0)
                return zone;
        }

        return "";
    }

    // A zone's quest-gated aether currents as quest steps with start locations.
    public AetherCurrentZoneEntity? GetAetherCurrentZone(uint rowId)
    {
        var row = Sheet<AetherCurrentCompFlgSet>().GetRowOrDefault(rowId);
        var zone = row?.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        if (row == null || zone.Length == 0)
            return null;

        var quests = new List<QuestChainStep>();
        foreach (var current in row.Value.AetherCurrents)
        {
            if (current.ValueNullable?.Quest is not { RowId: not 0 } questRef ||
                questRef.ValueNullable is not { } quest || quest.Name.IsEmpty)
                continue;

            quests.Add(new QuestChainStep(
                new QuestLink(questRef.RowId, quest.Name.ExtractText()),
                quest.ClassJobLevel.FirstOrDefault(),
                quest.IssuerLocation.ValueNullable is { } issuer ? ToMapLocation(issuer) : null));
        }

        return new AetherCurrentZoneEntity(rowId, zone, quests);
    }

    // FATE name, level, description and, for FATEs gated behind a quest
    // (Bozja), that quest.
    public FateEntity? GetFate(uint rowId)
    {
        var row = Sheet<Fate>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        // Fate.Location is an id into the zone layout files (LGB), not a Level
        // row (probed 2026-09-01: 0 of 1697 resolve), so FATE cards carry no
        // map pin.
        var required = row.Value.RequiredQuest.ValueNullable;
        return new FateEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.ClassJobLevel,
            row.Value.Description.ExtractText())
        {
            RequiredQuest = required is { } quest && !quest.Name.IsEmpty
                ? new QuestLink(row.Value.RequiredQuest.RowId, quest.Name.ExtractText())
                : null,
        };
    }

    // Levequest type, level, levemete location and rewards.
    public LeveEntity? GetLeve(uint rowId)
    {
        var row = Sheet<Leve>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var leve = row.Value;
        return new LeveEntity(
            rowId,
            leve.Name.ExtractText(),
            leve.ClassJobLevel,
            leve.LeveAssignmentType.ValueNullable?.Name.ExtractText() ?? "",
            leve.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "",
            leve.Description.ExtractText())
        {
            Levemete = leve.LevelLevemete.ValueNullable is { } level ? ToMapLocation(level) : null,
            IssuedAt = leve.PlaceNameIssued.ValueNullable?.Name.ExtractText() ?? "",
            AllowanceCost = leve.AllowanceCost,
            ExpReward = leve.ExpReward,
            GilReward = leve.GilReward,
        };
    }

    public AchievementEntity? GetAchievement(uint rowId)
    {
        var row = Sheet<Achievement>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return new AchievementEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            row.Value.AchievementCategory.ValueNullable?.Name.ExtractText() ?? "");
    }
}
