using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public async Task<SessionModelMetrics> ReadMetricsAsync(SessionWorkspaceEntry target, Func<bool> admitted,
        CancellationToken token)
    {
        var control = access.ControlRevision;
        void RequireCurrent()
        {
            token.ThrowIfCancellationRequested();
            if (!access.CanInspect || access.ControlRevision != control || !admitted())
            { throw new InvalidOperationException("Session metrics require unchanged private inspection admission."); }
        }
        RequireCurrent();
        var current = await workspace.ReadExactMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        RequireCurrent();
        if (current != target) { throw new InvalidOperationException("The selected metrics session changed. Refresh and inspect again."); }
        var snapshot = await (execution as ModelTurnHost
            ?? throw new InvalidOperationException("The model metrics owner is unavailable. No usage is inferred."))
            .ReadMetricsAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        var verified = await workspace.ReadExactMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        RequireCurrent();
        if (verified != target) { throw new InvalidOperationException("The metrics session changed during inspection."); }
        snapshot.Validate();
        return snapshot;
    }
}
