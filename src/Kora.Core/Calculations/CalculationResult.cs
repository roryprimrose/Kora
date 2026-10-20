namespace Kora.Core.Calculations;

public sealed class CalculationResult
{
    public static CalculationResult Refused { get; } = new(CalculationOutcome.Denied);

    internal CalculationResult(CalculationOutcome outcome, string? value = null)
    {
        Outcome = outcome;
        Value = value;
    }

    public CalculationOutcome Outcome { get; }
    public string? Value { get; }
}
