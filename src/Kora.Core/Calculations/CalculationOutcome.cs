namespace Kora.Core.Calculations;

public enum CalculationOutcome
{
    Succeeded,
    InvalidExpression,
    ExpressionLimit,
    TokenLimit,
    NestingLimit,
    Overflow,
    DivisionByZero,
    Denied,
}
