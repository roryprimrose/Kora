namespace Kora.Windows.Dependencies;

/// <summary>Exact host-owned process identity. Discovery alone does not authorize its use.</summary>
public sealed record WindowsOllamaRuntimeIdentity
{
    public WindowsOllamaRuntimeIdentity(int processId, DateTimeOffset startedAt, string executablePath, string executableSha256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.StartsWith(@"\\", StringComparison.Ordinal)
            || !WindowsOllamaToolEndpointBinding.IsDigest(executableSha256) || startedAt == DateTimeOffset.MinValue)
        { throw new ArgumentException("An exact local process start, executable path and SHA-256 are required.", nameof(executablePath)); }
        ProcessId = processId;
        StartedAt = startedAt;
        ExecutablePath = executablePath;
        ExecutableSha256 = executableSha256;
    }

    public int ProcessId { get; }
    public DateTimeOffset StartedAt { get; }
    public string ExecutablePath { get; }
    public string ExecutableSha256 { get; }
}
