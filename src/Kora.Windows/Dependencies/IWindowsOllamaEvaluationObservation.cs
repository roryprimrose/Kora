namespace Kora.Windows.Dependencies;

/// <summary>Read-only observation of the supported running runtime and pinned model; not release qualification.</summary>
public interface IWindowsOllamaEvaluationObservation
{
    Task<WindowsOllamaAdmissionSubject?> ObserveEvaluationAsync(CancellationToken cancellationToken);
    Task<bool> IsEvaluationCurrentAsync(WindowsOllamaAdmissionSubject subject, CancellationToken cancellationToken);
}
