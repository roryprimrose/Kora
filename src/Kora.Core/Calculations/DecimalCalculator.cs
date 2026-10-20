using System.Globalization;

namespace Kora.Core.Calculations;

/// <summary>A bounded decimal grammar, not an interpreter or an execution surface.</summary>
public static class DecimalCalculator
{
    public const int MaximumExpressionCharacters = 512;
    public const int MaximumTokens = 128;
    public const int MaximumNesting = 16;
    public const int MaximumOutputCharacters = 31;

    public static CalculationResult Evaluate(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Length > MaximumExpressionCharacters) { return new(CalculationOutcome.ExpressionLimit); }
        try
        {
            var value = new Parser(expression).Evaluate();
            return new(CalculationOutcome.Succeeded,
                value.ToString("0.############################", CultureInfo.InvariantCulture));
        }
        catch (SyntaxException exception) { return new(exception.Outcome); }
        catch (OverflowException) { return new(CalculationOutcome.Overflow); }
        catch (DivideByZeroException) { return new(CalculationOutcome.DivisionByZero); }
    }

    private sealed class SyntaxException(CalculationOutcome outcome) : Exception
    {
        public CalculationOutcome Outcome { get; } = outcome;
    }

    private sealed class Parser(string text)
    {
        private int position;
        private int tokens;

        public decimal Evaluate()
        {
            var value = Sum(0);
            SkipSpaces();
            if (position != text.Length) { throw new SyntaxException(CalculationOutcome.InvalidExpression); }
            return value;
        }

        private decimal Sum(int depth)
        {
            var value = Product(depth);
            while (true)
            {
                if (Take('+')) { value = checked(value + Product(depth)); }
                else if (Take('-')) { value = checked(value - Product(depth)); }
                else { return value; }
            }
        }

        private decimal Product(int depth)
        {
            var value = Unary(depth);
            while (true)
            {
                if (Take('*')) { value = checked(value * Unary(depth)); }
                else if (Take('/')) { value = checked(value / Unary(depth)); }
                else { return value; }
            }
        }

        private decimal Unary(int depth)
        {
            if (depth > MaximumNesting) { throw new SyntaxException(CalculationOutcome.NestingLimit); }
            if (Take('+')) { return Unary(depth + 1); }
            if (Take('-')) { return checked(-Unary(depth + 1)); }
            if (Take('('))
            {
                var value = Sum(depth + 1);
                if (!Take(')')) { throw new SyntaxException(CalculationOutcome.InvalidExpression); }
                return value;
            }
            return Literal();
        }

        private decimal Literal()
        {
            SkipSpaces();
            CountToken();
            var start = position;
            while (position < text.Length && char.IsAsciiDigit(text[position])) { position++; }
            if (position == start) { throw new SyntaxException(CalculationOutcome.InvalidExpression); }
            var integerEnd = position;
            if (position < text.Length && text[position] == '.')
            {
                position++;
                var fraction = position;
                while (position < text.Length && char.IsAsciiDigit(text[position])) { position++; }
                if (position == fraction) { throw new SyntaxException(CalculationOutcome.InvalidExpression); }
            }
            var end = position;
            var scale = 0;
            if (end > integerEnd)
            {
                while (text[end - 1] == '0') { end--; }
                scale = end - integerEnd - 1;
                if (scale > 28) { throw new OverflowException(); }
            }
            // Accumulate an exact coefficient: decimal.TryParse may round an unrepresentable literal.
            decimal coefficient = 0;
            for (var index = start; index < end; index++)
            {
                if (text[index] != '.') { coefficient = checked(coefficient * 10 + (text[index] - '0')); }
            }
            decimal divisor = 1;
            for (var index = 0; index < scale; index++) { divisor *= 10; }
            return coefficient / divisor;
        }

        private bool Take(char expected)
        {
            SkipSpaces();
            if (position == text.Length || text[position] != expected) { return false; }
            CountToken();
            position++;
            return true;
        }

        private void CountToken()
        {
            if (++tokens > MaximumTokens) { throw new SyntaxException(CalculationOutcome.TokenLimit); }
        }

        private void SkipSpaces()
        {
            while (position < text.Length && text[position] == ' ') { position++; }
        }
    }
}
