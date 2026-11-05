namespace Kora.Core.Maintenance;

/// <summary>Starts only the reviewed installer after final identity and interactive-host checks.</summary>
public interface IReleaseInstallerLauncher
{
    /// <summary>Requests ordinary installer startup; it does not report installation success.</summary>
    Task LaunchAsync(string path, ReleaseAsset installer, Func<bool> admitted, CancellationToken cancellationToken);
}
