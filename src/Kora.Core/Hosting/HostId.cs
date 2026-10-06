namespace Kora.Core.Hosting;

public readonly record struct HostId<T>
{
    [System.Text.Json.Serialization.JsonConstructor]
    public HostId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A host identity cannot be empty.", nameof(value));
        }
        Value = value;
    }

    public Guid Value { get; }

    public void Validate()
    {
        if (Value == Guid.Empty)
        {
            throw new InvalidDataException("A required host identity is missing.");
        }
    }
}
