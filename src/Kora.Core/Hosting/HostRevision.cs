namespace Kora.Core.Hosting;

public readonly record struct HostRevision
{
    [System.Text.Json.Serialization.JsonConstructor]
    public HostRevision(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public long Value { get; }
}
