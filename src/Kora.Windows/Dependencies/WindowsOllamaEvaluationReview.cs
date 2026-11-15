namespace Kora.Windows.Dependencies;

/// <summary>Volatile native review of one exact runtime. This does not authorize inference.</summary>
public sealed class WindowsOllamaEvaluationReview
{
    internal WindowsOllamaEvaluationReview(WindowsOllamaAdmissionSubject subject, DateTimeOffset expiresAt)
    {
        Subject = subject;
        ExpiresAt = expiresAt;
    }

    public WindowsOllamaAdmissionSubject Subject { get; }
    public DateTimeOffset ExpiresAt { get; }
}
