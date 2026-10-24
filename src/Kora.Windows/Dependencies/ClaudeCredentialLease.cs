using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Opaque host approval bound to the actual credential, not a claimed Console login.</summary>
public sealed class ClaudeCredentialLease
{
    public ClaudeCredentialLease(ProviderAccountId account, string workspace, string credentialSha256, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (string.IsNullOrWhiteSpace(workspace) || workspace.Length > 128
            || workspace.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_')
            || credentialSha256.Length != 64 || credentialSha256.Any(character => !Uri.IsHexDigit(character))
            || expiresAt == DateTimeOffset.MaxValue)
        { throw new InvalidDataException("An exact approved workspace/credential binding and finite expiry are required."); }
        Account = account; Workspace = workspace; CredentialSha256 = credentialSha256; ExpiresAt = expiresAt;
    }
    public ProviderAccountId Account { get; }
    public string Workspace { get; }
    internal string CredentialSha256 { get; }
    public DateTimeOffset ExpiresAt { get; }
}
