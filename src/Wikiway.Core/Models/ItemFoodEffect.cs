namespace Wikiway.Core.Models;

// ExpBonusPercent applies only to food (Well Fed); medicine grants none.
public sealed record ItemFoodEffect(
    string StatusName,
    int DurationSeconds,
    int ExpBonusPercent,
    IReadOnlyList<FoodStat> Stats);

// HQ values equal the NQ ones when the item has no HQ form.
public sealed record FoodStat(
    string Name, bool Relative, int Value, int Max, int HqValue, int HqMax);
