namespace Kora.Windows.Dependencies;

public sealed class CopilotEvaluationCandidate
{
    internal CopilotEvaluationCandidate(string executablePath, string sha256, DateTimeOffset expiresAt)
    { ExecutablePath = executablePath; Sha256 = sha256; ExpiresAt = expiresAt; }
    public string ExecutablePath { get; }
    public string Sha256 { get; }
    public DateTimeOffset ExpiresAt { get; }
}
