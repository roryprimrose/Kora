namespace Kora.Core.Maintenance;

/// <summary>Verifies a published canonical version without accepting a caller-supplied download address.</summary>
public interface IReleaseVersionMetadataClient : IReleaseMetadataClient
{
    /// <summary>Checks an exact version under the selected stable or stable-and-beta policy.</summary>
    Task<ReleaseCheck> CheckVersionAsync(ReleaseChannel channel, string currentVersion,
        ReleaseArchitecture architecture, ReleaseVersion version, CancellationToken cancellationToken);
}
