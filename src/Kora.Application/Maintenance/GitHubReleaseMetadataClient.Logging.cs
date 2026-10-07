using Kora.Core.Maintenance;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Maintenance;

public sealed partial class GitHubReleaseMetadataClient
{
    [LoggerMessage(320, LogLevel.Information, "Canonical metadata check on {Channel} ended {Availability}.")]
    private static partial void CheckOutcome(ILogger logger, ReleaseChannel channel, ReleaseAvailability availability);
}
