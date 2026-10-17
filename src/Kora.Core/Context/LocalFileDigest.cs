namespace Kora.Core.Context;

/// <summary>Canonical original-byte SHA-256 syntax shared by retained file references and predicates.</summary>
public static class LocalFileDigest
{
    public static bool IsCanonical(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
        && value.Any(character => character != '0');

    public static void Validate(string value)
    {
        if (!IsCanonical(value))
        { throw new InvalidDataException("A file digest must be exactly 64 lowercase hexadecimal characters and not all zero."); }
    }
}
