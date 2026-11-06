namespace Kora.Core.Artifacts;

public sealed record ArtifactDefinition
{
    public const int MaximumContentLength = 64 * 1024;
    public const int MaximumDescriptionLength = 1024;

    public ArtifactDefinition(
        string id,
        ArtifactKind kind,
        string name,
        string description,
        string commandName,
        IReadOnlyList<string> spokenNames,
        string source,
        string version,
        string digest,
        string content)
    {
        Id = ValidateId(id);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Kind = kind;
        Name = ValidateText(name, nameof(name), 128);
        Description = ValidateText(description, nameof(description), MaximumDescriptionLength);
        CommandName = ValidateCommandName(commandName);
        ArgumentNullException.ThrowIfNull(spokenNames);
        if (spokenNames.Count is 0 or > 8)
        {
            throw new ArgumentException("An artifact requires between one and eight spoken names.", nameof(spokenNames));
        }
        SpokenNames = spokenNames.Select(name => ValidateText(name, nameof(spokenNames), 128)).ToArray();
        if (SpokenNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != SpokenNames.Count)
        {
            throw new ArgumentException("Artifact spoken names must be unique.", nameof(spokenNames));
        }
        Source = ValidateIdentifier(source, nameof(source), 64);
        Version = ValidateIdentifier(version, nameof(version), 64);
        Digest = ArtifactDigest.Validate(digest);
        Content = ValidateText(content, nameof(content), MaximumContentLength);
    }

    public string Id { get; }
    public ArtifactKind Kind { get; }
    public string Name { get; }
    public string Description { get; }
    public string CommandName { get; }
    public IReadOnlyList<string> SpokenNames { get; }
    public string Source { get; }
    public string Version { get; }
    public string Digest { get; }
    public string Content { get; }
    public string? UseControlId { get; init; }
    public bool IsUseDisabled { get; init; }
    // Historical instruction-member names are shared by the two closed owned kinds.
    // Always qualify them with Kind; a display label or same filename in another kind is not identity.
    public string? OwnedInstructionName { get; init; }
    public bool IsUseEnabled { get; init; } = true;
    // An explicit use-disable is not source/access revocation. Only already captured content
    // may finish; all new selections and pending unadmitted decisions still require IsUseEnabled.
    public bool PreservesCapturedUse { get; init; }
    public bool RequiresInstructionReview { get; init; }
    public string? OwnedInstructionRevision { get; init; }
    public OwnedPromptTemplate? PromptTemplate { get; init; }
    public IReadOnlyList<string> DeclaredInstructionReferences { get; init; } = [];

    public static string ValidateId(string id) => ValidateIdentifier(id, nameof(id), 128);

    private static string ValidateIdentifier(string value, string parameterName, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximum || value.Any(character =>
            character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
        {
            throw new ArgumentException("Artifact identifiers require bounded lowercase ASCII.", parameterName);
        }
        return value;
    }

    private static string ValidateCommandName(string value)
    {
        ValidateIdentifier(value, nameof(value), 64);
        if (value.StartsWith('-') || value.EndsWith('-'))
        {
            throw new ArgumentException("Artifact command names cannot start or end with a hyphen.", nameof(value));
        }
        return value;
    }

    private static string ValidateText(string value, string parameterName, int maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximum || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Artifact text exceeds its bounds or contains NUL.", parameterName);
        }
        return value;
    }

}
