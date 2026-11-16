using System.Collections.Immutable;

namespace Kora.Windows.Dependencies;

public sealed class ClaudeDeploymentApproval
{
    private readonly DevelopmentPreviewLease? preview;
    internal ClaudeDeploymentApproval(DevelopmentPreviewLease preview) : this(preview.ExpiresAt)
    { this.preview = preview; }
    public ClaudeDeploymentApproval(DateTimeOffset expiresAt,
        IEnumerable<ClaudeTextStreamProfile>? textStreams = null)
    {
        if (expiresAt == DateTimeOffset.MaxValue) { throw new InvalidDataException("Claude approval requires finite expiry."); }
        ExpiresAt = expiresAt;
        TextStreams = (textStreams ?? []).ToImmutableArray();
        if (TextStreams.Any(profile => profile.InputTokens is < 1 or > 8192
            || profile.OutputTokens is < 1 or > 4096) || TextStreams.Distinct().Count() != TextStreams.Length)
        { throw new InvalidDataException("Claude text-stream evidence requires unique exact bounded profiles."); }
    }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsDevelopmentPreview => preview is not null;
    internal bool HasCurrentDevelopmentPreview => preview?.IsCurrent == true;
    public System.Collections.Immutable.ImmutableArray<ClaudeTextStreamProfile> TextStreams { get; }
    public string Adapter => "anthropic-messages-meai-sdk-v5";
    public string ApiVersion => "2023-06-01";
}
