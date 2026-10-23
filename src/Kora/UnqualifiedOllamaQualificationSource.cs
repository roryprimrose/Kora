using Kora.Windows.Dependencies;

namespace Kora;

/// <summary>No accepted exact runtime/model/process-use receipt ships in this build. Installation cannot issue one.</summary>
internal sealed class UnqualifiedOllamaQualificationSource : IWindowsOllamaQualificationSource
{
    public Task<WindowsOllamaQualification?> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<WindowsOllamaQualification?>(null);
    }
}
