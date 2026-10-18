using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Reads bounded content-free hypothetical retention impact, without mutation authority.</summary>
public interface ISessionRetentionPlannerReader
{
    /// <summary>Signals that a previously observed source may no longer be current.</summary>
    event EventHandler? Changed;

    /// <summary>Observes the exact proposed policy at one fixed cutoff.</summary>
    ValueTask<SessionRetentionPlanResult> ReadAsync(SessionRetentionSettings proposed, DateTimeOffset cutoff,
        HostRequest request, CancellationToken cancellationToken);

    /// <summary>Checks the exact source and lifetime of an observation.</summary>
    bool IsCurrent(SessionRetentionPlanResult result);
}
