namespace Kora.Core.Dependencies;

/// <summary>Remaining complete serialized bytes for this one attempt, including provider framing.</summary>
public sealed record ModelToolBudget
{
    public ModelToolBudget(int inputUtf8Bytes, int outputUtf8Bytes)
    {
        if (inputUtf8Bytes <= 0 || inputUtf8Bytes > ModelToolProtocol.MaximumInputUtf8Bytes
            || outputUtf8Bytes <= 0 || outputUtf8Bytes > ModelToolProtocol.MaximumOutputUtf8Bytes)
        { throw new InvalidDataException("A transport budget must be positive and within the shared host limits."); }
        InputUtf8Bytes = inputUtf8Bytes;
        OutputUtf8Bytes = outputUtf8Bytes;
    }

    public int InputUtf8Bytes { get; }
    public int OutputUtf8Bytes { get; }
}
