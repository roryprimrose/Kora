namespace Kora.Core.Dependencies;

public sealed record SpeechComponentIdentity
{
    public SpeechComponentIdentity(string componentId, string revision)
    {
        ValidateToken(componentId);
        ValidateToken(revision);
        ComponentId = componentId;
        Revision = revision;
    }

    public string ComponentId { get; }
    public string Revision { get; }
    public string StorageName => $"component-{ComponentId}.{Revision}";

    internal static void ValidateToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 64 || value.Any(character => character is not (>= 'a' and <= 'z'
            or >= '0' and <= '9' or '-')) || value[0] == '-' || value[^1] == '-')
        {
            throw new ArgumentException("Component identities require bounded canonical lowercase tokens.", nameof(value));
        }
    }
}
