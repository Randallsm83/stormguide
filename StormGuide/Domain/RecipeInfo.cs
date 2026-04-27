namespace StormGuide.Domain;

public sealed record RecipeInfo(
    string Name,
    string DisplayName,
    string Grade,
    string ProducedGood,
    int    ProducedAmount,
    double ProductionTime,
    IReadOnlyList<string>           Tags,
    IReadOnlyList<RecipeInputSlot>  RequiredGoods,
    IReadOnlyList<string>           Buildings)
{
    /// <summary>
    /// Base goods/sec assuming a single worker and no modifiers.
    /// (Effective rates accounting for workers/effects are computed at the
    /// Provider layer using live game state.)
    /// </summary>
    public double BaseGoodsPerSec => ProductionTime > 0 ? ProducedAmount / ProductionTime : 0;
}
