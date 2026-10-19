namespace HeadlessScreenReader.Domain;

public sealed record RangeValueText(string Text, SemanticSource Source);

/// <summary>Observed range data. Null is unobserved; no percentage, default or completion is inferred.</summary>
public sealed record RangeObservation(
    double? Value, double? Minimum, double? Maximum, SemanticSource Source,
    RangeValueText? ValueText = null)
{
    public bool IsConsistent => Finite(Value) && Finite(Minimum) && Finite(Maximum)
        && !(Minimum is double min && Maximum is double max && min > max)
        && !(Value is double low && Minimum is double floor && low < floor)
        && !(Value is double high && Maximum is double ceiling && high > ceiling);

    private static bool Finite(double? value) => value is null || double.IsFinite(value.Value);
}
