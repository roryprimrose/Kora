using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFileSearch(
    LocalFilePreview preview, ILocalFileRetrieval retrieval, ISecurityAuditLog audit, TimeProvider time, ILogger<LocalFileSearch> logger)
{
    public Task<LocalFileSearchResult> ExecuteAsync(LocalFileReference exactSource, string query,
        Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        return ExecuteAsync(query, (observedAt, commit) => preview.SearchAsync(exactSource, canPresent, observedAt,
            (revision, token) => Task.Run(() => retrieval.Search(revision, exactSource, query, observedAt, token), token),
            commit, cancellationToken), "file.search.local", "file.search.selected-revision", canPresent);
    }

    public Task<LocalFileSearchResult> ExecuteAsync(LocalFolderReference exactSource, string query,
        Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        return ExecuteAsync(query, (observedAt, commit) => preview.SearchAsync(exactSource, canPresent, observedAt,
            (revision, token) => Task.Run(() => retrieval.Search(revision, exactSource, query, observedAt, token), token),
            commit, cancellationToken), "folder.search.local", "folder.search.selected-revision", canPresent);
    }

    private async Task<LocalFileSearchResult> ExecuteAsync(string query,
        Func<DateTimeOffset, Action<LocalFileSearchResult>, Task<LocalFileSearchResult>> search, string action, string target,
        Func<bool> canPresent)
    {
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
            action, SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            target);
        LocalFileSearchResult result;
        try
        {
            audit.Write(auditEvent);
            result = await search(observedAt,
                outcome => audit.Write(auditEvent.WithOutcome(outcome.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch
                    ? SecurityAuditOutcome.Succeeded : outcome.Outcome == LocalFileSearchOutcome.Cancelled
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, outcome.Outcome.ToString().ToLowerInvariant())))
                .ConfigureAwait(false);
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
