namespace Kora.Core.Skills;

public sealed record SharedSkillSource
{
    public const int MaximumSources = 4;

    public SharedSkillSource(Guid id, string profileRelativeRoot, string directoryIdentity)
    {
        if (id == Guid.Empty) { throw new InvalidDataException("A host-owned source identity is required."); }
        Id = id;
        ProfileRelativeRoot = ValidateRelativePath(profileRelativeRoot);
        ArgumentNullException.ThrowIfNull(directoryIdentity);
        if (directoryIdentity.Length != 48 || directoryIdentity.Any(c => !char.IsAsciiHexDigit(c)))
        {
            throw new InvalidDataException("A verified directory identity is required.");
        }
        DirectoryIdentity = directoryIdentity;
    }

    public Guid Id { get; }
    public string ProfileRelativeRoot { get; }
    public string DirectoryIdentity { get; }

    public static string ValidateRelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var parts = path.Split('\\');
        if (path.Length is 0 or > 240 || parts.Length is < 2 or > 8
            || parts.Any(part => part.Length is 0 or > 80 || part is "." or ".."
                || part.EndsWith(' ') || part.EndsWith('.')
                || part.Any(c => char.IsControl(c) || c is '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
                || IsDeviceName(part)))
        {
            throw new InvalidDataException("Select a bounded, unaliased root below the profile; the profile itself is not a source.");
        }
        return path;
    }

    private static bool IsDeviceName(string part)
    {
        var stem = part.Split('.')[0];
        return stem.Equals("con", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("prn", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("aux", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("nul", StringComparison.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("com", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '0' and <= '9';
    }
}
