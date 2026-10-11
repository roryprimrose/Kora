using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Supported per-user installation metadata only. Never probes an endpoint or starts a process.</summary>
public sealed class WindowsOllamaInstallationObservation : ILocalProviderInstallationObservation
{
    private readonly Func<string, FileAttributes> attributes;

    public WindowsOllamaInstallationObservation() : this(File.GetAttributes) { }
    internal WindowsOllamaInstallationObservation(Func<string, FileAttributes> attributes) => this.attributes = attributes;

    public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalProviderInstallation result;
        try
        {
            var value = attributes(WindowsOllamaSetupService.InstalledExecutablePath);
            result = !value.HasFlag(FileAttributes.Directory) && !value.HasFlag(FileAttributes.ReparsePoint)
                ? LocalProviderInstallation.Installed : LocalProviderInstallation.Unknown;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        { result = LocalProviderInstallation.Missing; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { result = LocalProviderInstallation.Unknown; }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(result);
    }
}
