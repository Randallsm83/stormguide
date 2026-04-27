namespace StormGuide.Domain;

public sealed record GoodInfo(
    string Name,
    string DisplayName,
    string Category,
    bool   IsEatable,
    double EatingFullness,
    bool   CanBeBurned,
    double BurningTime,
    double TradingBuyValue,   // value player gets when selling to a trader
    double TradingSellValue,  // cost player pays when buying from a trader
    IReadOnlyList<string> TradersBuying,
    IReadOnlyList<string> TradersSelling,
    IReadOnlyList<string> Tags);
