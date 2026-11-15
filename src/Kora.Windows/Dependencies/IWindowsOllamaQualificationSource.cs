namespace Kora.Windows.Dependencies;

/// <summary>
/// Trusted host receipts only; null means no release-qualified or explicitly evaluated runtime/profile is authorized.
/// Keep the immutable receipt instance stable while current, and revoke it before replacing/removing it.
/// </summary>
public interface IWindowsOllamaQualificationSource
{
    Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken);
}
