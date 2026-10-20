namespace Kora.Core.Conversions;

public sealed record ConversionResult(ConversionOutcome Outcome, string? Value = null, bool Rounded = false);
