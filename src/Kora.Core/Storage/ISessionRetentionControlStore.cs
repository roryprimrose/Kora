using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Exact native retention observation and atomic original-user hold control.</summary>
public interface ISessionRetentionControlStore
{
    /// <summary>Checks maintenance/read admission without cleanup and returns the authority revision for late-result fencing.</summary>
    ValueTask<long> ValidatePassiveInspectionAsync(CancellationToken token);

    /// <summary>Reads the recorded policy and exemption identity without maintenance or clock renewal.</summary>
    ValueTask<SessionRetentionObservation> ReadRetentionObservationAsync(
        HostId<SessionIdentity> session, CancellationToken token);

    /// <summary>Changes one hold only if the displayed state, generation and exemption audit identity still match.</summary>
    ValueTask<SessionRetentionObservation> SetRetentionHoldAsync(
        HostRequest request, SessionRetentionObservation expected, bool perpetual,
        Func<bool> admitted, CancellationToken token);
}
