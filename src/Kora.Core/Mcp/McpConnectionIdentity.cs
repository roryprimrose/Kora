using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kora.Core.Mcp;

/// <summary>An exact transport identity. A local process is never a delegated HTTPS account.</summary>
public sealed record McpConnectionIdentity
{
    public McpConnectionIdentity(McpAccountReference account)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Digest = Hash(JsonSerializer.Serialize(account));
    }

    public McpConnectionIdentity(McpStdioEndpoint stdio)
    {
        Stdio = stdio ?? throw new ArgumentNullException(nameof(stdio));
        Digest = stdio.Digest;
    }

    public McpAccountReference? Account { get; }
    public McpStdioEndpoint? Stdio { get; }
    public string Digest { get; }
    public string NativeDestination => Account?.Destination.Address.AbsoluteUri ?? Stdio!.NativeText;
    public bool Equals(McpConnectionIdentity? other) => other is not null && string.Equals(Digest, other.Digest, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Digest);
    public override string ToString() => nameof(McpConnectionIdentity);
    public static implicit operator McpConnectionIdentity(McpAccountReference account) => new(account);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    /// <summary>Separate request-bound native process authority, never inferred from a bounded-read grant.</summary>
    public sealed class McpProcessAuthorization(McpConnectionIdentity identity,
        string nativeIdentity, Func<CancellationToken, Task> validateLaunch, Func<CancellationToken, Task> validateCleanup)
    {
        public McpConnectionIdentity Identity { get; } = identity;
        public string NativeIdentity { get; } = nativeIdentity;
        public Task ValidateLaunchAsync(CancellationToken token) => validateLaunch(token);
        public Task ValidateCleanupAsync(CancellationToken token) => validateCleanup(token);
        public override string ToString() => nameof(McpProcessAuthorization);
    }
}