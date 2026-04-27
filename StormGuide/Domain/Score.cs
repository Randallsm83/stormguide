namespace StormGuide.Domain;

/// <summary>
/// One contribution to a Score. Surfaced row-by-row in the UI breakdown so the
/// player can always see HOW the number was derived.
/// </summary>
public sealed record ScoreComponent(string Label, double Value, string? Note = null);

/// <summary>
/// A composable, transparent score. <see cref="Components"/> is the source of
/// truth: <see cref="Value"/> is just their sum (or whatever combination the
/// producer chose), preserved here so it doesn't have to be recomputed each
/// time the UI redraws.
/// </summary>
public sealed record Score(double Value, IReadOnlyList<ScoreComponent> Components, string? Unit = null)
{
    public static Score Empty { get; } = new(0, Array.Empty<ScoreComponent>());

    public string Format(string fmt = "0.##")
        => Unit is null ? Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture)
                        : $"{Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture)} {Unit}";
}
