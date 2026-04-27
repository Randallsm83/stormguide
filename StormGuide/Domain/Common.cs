namespace StormGuide.Domain;

/// <summary>
/// A reference to a quantity of a good. Used in recipe inputs.
/// </summary>
public sealed record GoodAmount(string Good, int Amount);

/// <summary>
/// One slot in a recipe's required-goods list. The recipe can be satisfied
/// by ANY of the goods in this slot (oneof). Concretely, a Bakery's Biscuit
/// recipe may accept "Flour OR Vegetables OR Roots" as the input slot.
/// </summary>
public sealed record RecipeInputSlot(IReadOnlyList<GoodAmount> Options);
