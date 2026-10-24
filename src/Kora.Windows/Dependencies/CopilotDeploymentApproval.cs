namespace Kora.Windows.Dependencies;

/// <summary>Host-issued enablement, not deserialized preferences or a user-supplied executable.</summary>
public sealed class CopilotDeploymentApproval
{
    public CopilotDeploymentApproval(string executablePath, string workingDirectory, string baseDirectory,
        IReadOnlyDictionary<string, string> environment, DateTimeOffset expiresAt)
    {
        if (!Path.IsPathFullyQualified(executablePath) || !Path.IsPathFullyQualified(workingDirectory)
            || !Path.IsPathFullyQualified(baseDirectory) || expiresAt == DateTimeOffset.MaxValue)
        { throw new InvalidDataException("Approved Copilot deployment requires owned absolute paths and finite expiry."); }
        ExecutablePath = executablePath;
        WorkingDirectory = workingDirectory;
        BaseDirectory = baseDirectory;
        Environment = CopilotExecutionEnvironment.Copy(environment);
        ExpiresAt = expiresAt;
    }
    public string ExecutablePath { get; }
    public string WorkingDirectory { get; }
    public string BaseDirectory { get; }
    public IReadOnlyDictionary<string, string> Environment { get; }
    public DateTimeOffset ExpiresAt { get; }
}
