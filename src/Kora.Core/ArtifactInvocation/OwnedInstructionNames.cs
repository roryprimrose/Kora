namespace Kora.Core.Artifacts;

public static class OwnedInstructionNames
{
    public static string ReadName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".."
            || name.EndsWith('.') || name.EndsWith(' ')
            || name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        { throw new InvalidDataException("Select an exact bounded single owned instruction filename, not a path or alias."); }
        return name;
    }

    public static string WriteName(string name)
    {
        ReadName(name);
        if (name.Length > 64 || name.StartsWith('-') || name.EndsWith('-')
            || name.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            || name is "con" or "prn" or "aux" or "nul"
            || Enumerable.Range(1, 9).Any(index => string.Equals(name, "com" + index, StringComparison.Ordinal)
                || string.Equals(name, "lpt" + index, StringComparison.Ordinal)))
        { throw new InvalidDataException("Writing requires a canonical lowercase ASCII owned ID (maximum 64). This original name is read-only; external editing is required, fork/rename is not implemented."); }
        return name;
    }
}
