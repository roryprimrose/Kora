namespace Kora.Windows.Dependencies;

public sealed class ClaudeDeploymentApproval
{
    public ClaudeDeploymentApproval(DateTimeOffset expiresAt)
    {
        if (expiresAt == DateTimeOffset.MaxValue) { throw new InvalidDataException("Claude approval requires finite expiry."); }
        ExpiresAt = expiresAt;
    }
    public DateTimeOffset ExpiresAt { get; }
    public string Adapter => "anthropic-messages-https-text-v1";
    public string ApiVersion => "2023-06-01";
}
