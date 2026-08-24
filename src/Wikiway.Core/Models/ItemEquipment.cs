namespace Wikiway.Core.Models;

public sealed record ItemEquipment(
    string Slot,
    ushort ItemLevel,
    byte EquipLevel,
    string ClassJobs,
    IReadOnlyList<EquipStat> Stats)
{
    public WeaponInfo? Weapon { get; init; }
    public DefenseInfo? Defense { get; init; }
    public BlockInfo? Block { get; init; }
    public byte MateriaSlots { get; init; }
    public bool AdvancedMelding { get; init; }
    public bool Unique { get; init; }
    public bool Untradable { get; init; }
    public bool CanBeHq { get; init; }
    public byte DyeCount { get; init; }
    public string Repair { get; init; } = "";
    public string Series { get; init; } = "";
    public string SpecialBonus { get; init; } = "";
    public bool Desynthable { get; init; }
    public uint SellPrice { get; init; }
}

public sealed record EquipStat(string Name, short Value, short HqBonus = 0);

public sealed record WeaponInfo(
    ushort PhysDamage,
    ushort MagDamage,
    double DelaySeconds,
    ushort HqPhysBonus = 0,
    ushort HqMagBonus = 0);

public sealed record DefenseInfo(
    ushort Physical,
    ushort Magical,
    ushort HqPhysBonus = 0,
    ushort HqMagBonus = 0);

public sealed record BlockInfo(ushort Strength, ushort Rate, ushort HqStrengthBonus = 0, ushort HqRateBonus = 0);
