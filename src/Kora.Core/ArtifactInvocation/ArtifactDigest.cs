namespace Kora.Core.Artifacts;

public static class ArtifactDigest
{
    public static string Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character =>
            character is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
        {
            throw new ArgumentException("An artifact requires a lowercase SHA-256 digest.", nameof(value));
        }
        return value;
    }
}
