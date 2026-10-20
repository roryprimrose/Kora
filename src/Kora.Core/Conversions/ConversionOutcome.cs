namespace Kora.Core.Conversions;

public enum ConversionOutcome
{
    Succeeded, InvalidInput, UnknownUnit, IncompatibleUnits, NegativeQuantity,
    BelowAbsoluteZero, Overflow, Denied, Cancelled,
}
