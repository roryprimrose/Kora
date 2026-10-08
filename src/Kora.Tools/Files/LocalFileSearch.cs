using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFileSearch(
    LocalFilePreview preview, ILocalFileRetrieval retrieval, ISecurityAuditLog audit, TimeProvider time, ILogger<LocalFileSearch> logger)
{
    public async Task<LocalFileSearchResult> ExecuteAsync(LocalFileReference exactSource, string query,
        Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(canPresent);
        var context = HostActivity.RequireCurrent();
        if (context.Outcome != HostOperationOutcome.Unknown)
        {
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, time.GetUtcNow());
        }
        var request = context.Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var observedAt = time.GetUtcNow();
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "file.search.local", SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "file.search.selected-revision");
        LocalFileSearchResult result;
        try
        {
            audit.Write(auditEvent);
            result = await preview.SearchAsync(exactSource, canPresent, observedAt,
                (revision, token) => Task.Run(() => retrieval.Search(revision, exactSource, query, observedAt, token), token),
                outcome => audit.Write(auditEvent.WithOutcome(outcome.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch
                    ? SecurityAuditOutcome.Succeeded : outcome.Outcome == LocalFileSearchOutcome.Cancelled
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, outcome.Outcome.ToString().ToLowerInvariant())),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Unavailable, observedAt);
        }
        Report(logger, result.Outcome, result.Citations.Count, result.Truncated);
        activity.Complete(result.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch ? HostOperationOutcome.Completed
            : result.Outcome == LocalFileSearchOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }
}
