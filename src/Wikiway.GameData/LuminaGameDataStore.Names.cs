using Lumina.Excel.Sheets;
using Wikiway.Core.Abstractions;

namespace Wikiway.GameData;

// The search index source: every searchable name in the game, tagged with its
// kind and row id. LocalGameDataProvider feeds this to FuzzyNameIndex once at
// startup; full entities are only built for the rows a search matches.
public sealed partial class LuminaGameDataStore
{
    public IReadOnlyList<NameIndexEntry> GetAllNames(CancellationToken ct = default)
    {
        var names = new List<NameIndexEntry>(80_000);

        // Gatherables carry their own kind so a "gather:" search can scope to
        // them; "item:" searches both kinds.
        var gatherable = GatherableItemIds();
        foreach (var row in Sheet<Item>())
            AddName(names, gatherable.Contains(row.RowId) ? EntityKind.Gatherable : EntityKind.Item,
                row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<ENpcResident>())
            AddName(names, EntityKind.Npc, row.RowId, row.Singular.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Quest>())
            AddName(names, EntityKind.Quest, row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Mount>())
            AddName(names, EntityKind.Mount, row.RowId, row.Singular.ExtractText());

        foreach (var row in Sheet<Companion>())
            AddName(names, EntityKind.Minion, row.RowId, row.Singular.ExtractText());

        foreach (var row in Sheet<Achievement>())
            AddName(names, EntityKind.Achievement, row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Orchestrion>())
            AddName(names, EntityKind.Orchestrion, row.RowId, row.Name.ExtractText());

        foreach (var row in Sheet<TripleTriadCard>())
            AddName(names, EntityKind.TripleTriadCard, row.RowId, row.Name.ExtractText());

        // Command-less emote rows are event-stage variants (duplicate Snowball
        // entries and the like), not something a player can look up.
        foreach (var row in Sheet<Emote>())
        {
            if (row.TextCommand.ValueNullable is { } command && !command.Command.IsEmpty)
                AddName(names, EntityKind.Emote, row.RowId, row.Name.ExtractText());
        }

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Adventure>())
            AddName(names, EntityKind.Vista, row.RowId, row.Name.ExtractText());

        foreach (var row in Sheet<NotoriousMonster>())
        {
            if (row.BNpcName.ValueNullable is { } mark && !mark.Singular.IsEmpty)
                AddName(names, EntityKind.HuntMark, row.RowId, mark.Singular.ExtractText());
        }

        // Indexed under both word orders so "kholusia" and "aether currents"
        // both reach the zone card; the duplicates collapse at match time.
        foreach (var row in Sheet<AetherCurrentCompFlgSet>())
        {
            var zone = row.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (zone.Length > 0)
            {
                AddName(names, EntityKind.AetherCurrentZone, row.RowId, $"{zone} aether currents");
                AddName(names, EntityKind.AetherCurrentZone, row.RowId, $"aether currents {zone}");
            }
        }

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Fate>())
            AddName(names, EntityKind.Fate, row.RowId, row.Name.ExtractText());

        foreach (var row in Sheet<Leve>())
            AddName(names, EntityKind.Leve, row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<ContentFinderCondition>())
            AddName(names, IsUnlockable(row) ? EntityKind.Unlockable : EntityKind.Duty, row.RowId, row.Name.ExtractText());

        foreach (var zone in CuratedZones)
            AddName(names, EntityKind.Unlockable, zone.RowId, zone.Name);

        return names;
    }

    // Single-row name reads for game-thread callers (context menu, territory
    // change).
    public string? GetItemName(uint rowId)
    {
        var row = Sheet<Item>().GetRowOrDefault(rowId);
        return row == null || row.Value.Name.IsEmpty ? null : row.Value.Name.ExtractText();
    }

    public string? GetNpcName(uint rowId)
    {
        var row = Sheet<ENpcResident>().GetRowOrDefault(rowId);
        return row == null || row.Value.Singular.IsEmpty ? null : TitleCase(row.Value.Singular.ExtractText());
    }

    // Empty names are placeholder rows; everything is lowercased because
    // FuzzyNameIndex compares ordinally.
    private static void AddName(List<NameIndexEntry> names, EntityKind kind, uint rowId, string name)
    {
        if (name.Length > 0)
            names.Add(new NameIndexEntry(kind, rowId, name.ToLowerInvariant()));
    }
}
