using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Observes the exact qualified subject and current Windows account/session; never starts or installs a runtime.</summary>
public interface IWindowsOllamaAdmissionObservation
{
    /// <summary>Returns no subject when process, account, endpoint or native model metadata cannot be verified.</summary>
    Task<WindowsOllamaAdmissionSubject?> Observe(LocalModelQualificationEvidence evidence, CancellationToken cancellationToken);

    /// <summary>Checks an existing exact subject without selecting a replacement.</summary>
    Task<bool> IsCurrent(LocalModelQualificationEvidence evidence, WindowsOllamaAdmissionSubject subject, CancellationToken cancellationToken);
}