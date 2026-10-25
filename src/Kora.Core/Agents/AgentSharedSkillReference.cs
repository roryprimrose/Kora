namespace Kora.Core.Agents;

/// <summary>An exact registered shared-profile identity, never a source path or a name lookup.</summary>
public sealed record AgentSharedSkillReference
{
    public AgentSharedSkillReference(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var parts = identity.Split(':');
        if (parts.Length != 3 || parts[0] is not "shared-profile"
            || !Guid.TryParseExact(parts[1], "N", out var source) || source == Guid.Empty
            || !string.Equals(parts[1], source.ToString("N"), StringComparison.Ordinal))
        { throw new InvalidDataException("Declare exactly one canonical source-qualified shared skill identity."); }
        var target = parts[2].Split('@');
        if (target.Length != 2 || target[0].Length is 0 or > 64
            || target[0][0] is not (>= 'a' and <= 'z') || target[0][^1] == '-'
            || target[0].Contains("--", StringComparison.Ordinal)
            || target[0].Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
        { throw new InvalidDataException("Declare an exact compatible skill name and version, not a path or search."); }
        var version = target[1].Split('.');
        if (version.Length != 3 || version.Any(part => part.Length is 0 or > 4
            || part.Any(c => !char.IsAsciiDigit(c)) || part.Length > 1 && part[0] == '0'))
        { throw new InvalidDataException("Declare an exact shared skill version; latest substitution is unavailable."); }
        Identity = identity;
        SourceId = source;
    }

    public string Identity { get; }
    public Guid SourceId { get; }
}
