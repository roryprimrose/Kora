namespace Kora.Windows.Dependencies;

/// <summary>
/// Trusted host evidence only; null means no authorized, qualified runtime/profile.
/// Keep the immutable receipt instance stable while current, and revoke it before replacing/removing it.
/// </summary>
public interface IWindowsOllamaQualificationSource
{
    Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken);
}
