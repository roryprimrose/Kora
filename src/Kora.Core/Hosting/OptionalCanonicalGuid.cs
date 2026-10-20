namespace Kora.Core.Hosting;

/// <summary>Canonical optional historical correlation input; never establishes host authority.</summary>
public static class OptionalCanonicalGuid
{
    /// <summary>Uses the same canonical codec for an explicitly required exact identity.</summary>
    public static Guid ParseRequired(string text) =>
        Parse(text) ?? throw new InvalidDataException("Enter a nonempty canonical lowercase D-format GUID.");

    public static Guid? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty
            || !string.Equals(text, value.ToString("D"), StringComparison.Ordinal))
        { throw new InvalidDataException("Enter a nonempty canonical lowercase D-format GUID; empty means Any. No whitespace or repair."); }
        return value;
    }

    public static void Validate(Guid? value)
    {
        if (value == Guid.Empty)
        { throw new InvalidDataException("A present historical correlation ID must be nonempty."); }
    }
}
