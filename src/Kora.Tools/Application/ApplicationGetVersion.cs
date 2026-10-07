using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Tools.Application;

public sealed class ApplicationGetVersion(IApplicationInfo application)
{
    internal CapabilityReply Execute()
    {
        var version = application.Version;
        return string.IsNullOrWhiteSpace(version) || version.Length > 128
            ? new(CapabilityOutcome.Failed, "invalid-version-observation")
            : new(CapabilityOutcome.Succeeded, "observed",
                Version: new(version, "Not observed by the current version provider."));
    }
}
