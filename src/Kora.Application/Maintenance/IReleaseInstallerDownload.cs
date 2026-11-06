namespace Kora.Application.Maintenance;

/// <summary>Keeps the verified installer open until the host has finished its launch request.</summary>
public interface IReleaseInstallerDownload : IAsyncDisposable
{
    /// <summary>Gets the exact local file, not a command, URL or executable lookup.</summary>
    string Path { get; }
}
