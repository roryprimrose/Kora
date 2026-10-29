using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;

using Microsoft.Extensions.Logging;

namespace Kora.Tools.SessionControl;

public sealed partial class FixedSessionActionBroker(
    IFixedSessionEffect effect, ISecurityAuditLog audit, ILogger<FixedSessionActionBroker> logger)
{
    public string? CurrentIdentityDigest => effect.CurrentIdentityDigest;

    internal Task<FixedSessionEffectResult> ExecuteAsync(FixedSessionAction action,
        string identityDigest, Func<CancellationToken, ValueTask<bool>> authorize,
        Func<bool> current, CancellationToken token) => action switch
        {
            FixedSessionAction.Lock => new LockSession(this).ExecuteAsync(identityDigest, authorize, current, token),
            FixedSessionAction.Shutdown => new ShutdownComputer(this).ExecuteAsync(identityDigest, authorize, current, token),
            FixedSessionAction.Restart => new RestartComputer(this).ExecuteAsync(identityDigest, authorize, current, token),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    internal async Task<FixedSessionEffectResult> RequestAsync(FixedSessionAction action,
        string identityDigest, Func<CancellationToken, ValueTask<bool>> authorize,
        Func<bool> current, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(current);
        var host = HostActivity.RequireCurrent();
        if (host.Request.InvocationId is null
            || host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("A fresh original-user invocation is required for a fixed session effect."); }
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            action switch
            {
                FixedSessionAction.Lock => "session.lock",
                FixedSessionAction.Shutdown => "computer.shutdown",
                FixedSessionAction.Restart => "computer.restart",
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            }, SecurityAuditOutcome.Requested,
            host.Request.Origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "windows.current-session");
        // Required audit delivery is synchronous; failure prevents the native request.
        audit.Write(auditEvent);
        if (!await CurrentAsync().ConfigureAwait(false))
        { return Finish(new(FixedSessionEffectOutcome.Denied)); }
        FixedSessionEffectResult result;
        try { result = effect.Request(action); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            NativeRequestUnconfirmed(logger, exception);
            result = new(FixedSessionEffectOutcome.Unconfirmed);
        }
        try { return Finish(result); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            TerminalAuditUnconfirmed(logger, exception);
            return new(FixedSessionEffectOutcome.Unconfirmed, result.NativeError, AuditConfirmed: false);
        }

        async ValueTask<bool> CurrentAsync()
        {
            token.ThrowIfCancellationRequested();
            return current() && string.Equals(identityDigest, effect.CurrentIdentityDigest, StringComparison.Ordinal)
                && await authorize(token).ConfigureAwait(false)
                && current() && string.Equals(identityDigest, effect.CurrentIdentityDigest, StringComparison.Ordinal);
        }

        FixedSessionEffectResult Finish(FixedSessionEffectResult result)
        {
            audit.Write(auditEvent.WithOutcome(result.Outcome switch
            {
                FixedSessionEffectOutcome.RequestAccepted => SecurityAuditOutcome.Unknown,
                FixedSessionEffectOutcome.Denied => SecurityAuditOutcome.Denied,
                FixedSessionEffectOutcome.Failed => SecurityAuditOutcome.Failed,
                _ => SecurityAuditOutcome.Unknown,
            }, result.Outcome == FixedSessionEffectOutcome.RequestAccepted
                    ? "request-accepted-physical-state-unobserved" : "native-request-not-confirmed"));
            return result;
        }
    }
}