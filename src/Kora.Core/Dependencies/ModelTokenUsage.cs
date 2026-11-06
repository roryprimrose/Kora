namespace Kora.Core.Dependencies;

/// <summary>Provider-reported counts, independent of transport byte budgets. Missing counts remain unknown.</summary>
public sealed record ModelTokenUsage(
    long? InputTokens = null, long? OutputTokens = null, long? ContextTokens = null, long? ContextCapacityTokens = null)
{
    public void Validate()
    {
        if (InputTokens is < 0 || OutputTokens is < 0 || ContextTokens is < 0
            || ContextCapacityTokens is <= 0 || ContextTokens > ContextCapacityTokens)
        { throw new InvalidDataException("Provider token usage contains invalid counts or context capacity."); }
    }
}
