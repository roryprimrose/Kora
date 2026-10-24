using Kora.Core.Auditing;

namespace Kora.Core.Voice;

/// <summary>Resolves an exact host-bound response through host-owned speech admission.</summary>
public interface ISpeechReadOncePlayback
{
    /// <summary>Requests one playback without changing preferences or granting reusable authority.</summary>
    /// <param name="target">The exact host-issued response and policy revision.</param>
    /// <param name="initiator">The original host input lane; it cannot override the current host request origin.</param>
    /// <param name="cancellationToken">Cancellation of this invocation.</param>
    Task ReadOnceAsync(ResponseSpeechTarget target, SecurityAuditInitiator initiator, CancellationToken cancellationToken);
}
