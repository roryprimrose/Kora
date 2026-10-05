namespace Kora.Core.Voice;

public interface ISpeechProviderManager
{
    Task InstallProviderAsync(
        string providerId,
        IProgress<SpeechProviderInstallProgress> progress,
        CancellationToken cancellationToken = default);

    Task RemoveProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}