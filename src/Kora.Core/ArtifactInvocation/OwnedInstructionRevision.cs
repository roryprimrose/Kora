namespace Kora.Core.Artifacts;

/// <summary>Exact native digest, directory identity, file identity and filename digest encoding.</summary>
public static class OwnedInstructionRevision
{
    public static string Validate(string revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.Length != 227 || revision.Where((character, index) =>
            index is 64 or 113 or 162 ? character != '.' : character is not (>= '0' and <= '9' or >= 'a' and <= 'f')).Any())
        {
            throw new InvalidDataException("Enter an exact native revision: 64.48.48.64 lowercase hexadecimal characters. Empty means Any.");
        }
        return revision;
    }
}
