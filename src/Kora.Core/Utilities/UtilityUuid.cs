namespace Kora.Core.Utilities;

/// <summary>Standalone format facts, never a host identity or authority.</summary>
public sealed record UtilityUuid
{
    private UtilityUuid(string canonical) => Canonical = canonical;

    public string Canonical { get; }
    // Canonical text uses wire byte order, unlike Guid.ToByteArray().
    public int VersionNibble => Hex(Canonical[14]);
    public bool IsRfcVariant => (Hex(Canonical[19]) & 12) == 8;

    public static UtilityUuid Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 36) { throw Invalid(); }
        for (var index = 0; index < text.Length; index++)
        {
            if (index is 8 or 13 or 18 or 23)
            {
                if (text[index] != '-') { throw Invalid(); }
            }
            else if (text[index] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            { throw Invalid(); }
        }
        if (string.Equals(text, "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal)) { throw Invalid(); }
        return new(text);
    }

    private static int Hex(char value) => value <= '9' ? value - '0' : value - 'a' + 10;
    private static InvalidDataException Invalid() =>
        new("Enter exactly one nonempty lowercase canonical D-format UUID (36 ASCII characters); no repair.");
}
