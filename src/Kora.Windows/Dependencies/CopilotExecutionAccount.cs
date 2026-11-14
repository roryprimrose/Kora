using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Volatile enablement supplied by the host after account, terms, cost and runtime approval.</summary>
public sealed class CopilotExecutionAccount
{
    public CopilotExecutionAccount(ProviderProfileTuple profile, string login, string token,
        string executablePath, string workingDirectory, string baseDirectory,
        IReadOnlyDictionary<string, string> environment, DateTimeOffset expiresAt, bool isEvaluation = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (profile.Service != ProviderService.Copilot || !Path.IsPathFullyQualified(executablePath)
            || !Path.IsPathFullyQualified(workingDirectory) || !Path.IsPathFullyQualified(baseDirectory)
            || expiresAt == DateTimeOffset.MaxValue)
        {
            throw new InvalidDataException("Copilot enablement requires an exact profile and host-owned absolute runtime paths.");
        }
        Profile = profile;
        Login = login;
        Token = token;
        ExecutablePath = executablePath;
        WorkingDirectory = workingDirectory;
        BaseDirectory = baseDirectory;
        Environment = CopilotExecutionEnvironment.Copy(environment);
        ExpiresAt = expiresAt;
        IsEvaluation = isEvaluation;
    }

    public ProviderProfileTuple Profile { get; }
    public string Login { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsEvaluation { get; }
    internal string Token { get; }
    internal string ExecutablePath { get; }
    internal string WorkingDirectory { get; }
    internal string BaseDirectory { get; }
    internal IReadOnlyDictionary<string, string> Environment { get; }
}
