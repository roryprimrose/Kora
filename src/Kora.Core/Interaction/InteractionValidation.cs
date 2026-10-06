namespace Kora.Core.Interaction;

internal static class InteractionValidation
{
    public static string Identifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128 || value.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
        {
            throw new ArgumentException("An interaction identifier must be bounded lowercase ASCII.", nameof(value));
        }
        return value;
    }

    public static string Text(string value, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > maximum)
        {
            throw new ArgumentException("Interaction text exceeds its bound.", nameof(value));
        }
        return value;
    }

    public static string Digest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || value.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
        {
            throw new ArgumentException("A canonical lowercase SHA-256 digest is required.", nameof(value));
        }
        return value;
    }
}
