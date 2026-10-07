namespace Kora.Core.Maintenance;

public interface IReleaseMetadataClient
{
    Task<ReleaseCheck> CheckAsync(ReleaseChannel channel, string currentVersion,
        ReleaseArchitecture architecture, CancellationToken cancellationToken);
}
