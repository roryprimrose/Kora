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
        Func<bool> canPresent, CancellationToken cancellationToken) =>
        ExecuteAsync(exactSource, query, LocalFileRetrievalMode.LexicalLines, canPresent, cancellationToken);

    public Task<LocalFileSearchResult> ExecuteAsync(LocalFileReference exactSource, string query,
        LocalFileRetrievalMode mode, Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        return ExecuteAsync(query, mode, (observedAt, commit) => preview.SearchAsync(exactSource, canPresent, observedAt,
            (revision, token) => Task.Run(() => Validate(retrieval.Search(revision, exactSource, query, mode, observedAt, token), mode), token),
            commit, cancellationToken), "file.search.local", "file.search.selected-revision", canPresent);
    }

    public Task<LocalFileSearchResult> ExecuteAsync(LocalFolderReference exactSource, string query,
        Func<bool> canPresent, CancellationToken cancellationToken) =>
        ExecuteAsync(exactSource, query, LocalFileRetrievalMode.LexicalLines, canPresent, cancellationToken);

    public Task<LocalFileSearchResult> ExecuteAsync(LocalFolderReference exactSource, string query,
        LocalFileRetrievalMode mode, Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        return ExecuteAsync(query, mode, (observedAt, commit) => preview.SearchAsync(exactSource, canPresent, observedAt,
            (revision, token) => Task.Run(() => Validate(retrieval.Search(revision, exactSource, query, mode, observedAt, token), mode), token),
            commit, cancellationToken), "folder.search.local", "folder.search.selected-revision", canPresent);
    }

    public Task<LocalFileSearchResult> ExecuteRetainedAsync(LocalFileRevision validatedSnapshot, string query,
        LocalFileRetrievalMode mode, Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validatedSnapshot);
        return ExecuteAsync(query, mode, async (observedAt, commit) =>
        {
            LocalFileSearchResult result;
            var request = HostActivity.RequireCurrent().Request;
            if (!canPresent() || request.SessionId != validatedSnapshot.Review.Request.SessionId
                || request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null)
            {
                result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, observedAt, mode);
            }
            else
            {
                try
                {
                    result = await Task.Run(() => Validate(retrieval.Search(validatedSnapshot, validatedSnapshot.Reference,
                        query, mode, observedAt, cancellationToken), mode), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Cancelled, observedAt, mode);
                }
                if (!canPresent()) { result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Stale, observedAt, mode); }
            }
            // The attachment workflow still owns validated storage resolution and its terminal disclosure fence.
            commit(result);
            return result;
        }, "session.file.search.local", "session.file.retained-revision", canPresent);
    }

    private static LocalFileSearchResult Validate(LocalFileSearchResult result, LocalFileRetrievalMode mode)
    {
        _ = LocalFileRetrievalPolicy.TryVersion(mode, out var version);
        if (result.Mode != mode || !string.Equals(result.PolicyVersion, version, StringComparison.Ordinal))
        {
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, result.ObservedAt, mode);
        }
        return result;
    }

    private async Task<LocalFileSearchResult> ExecuteAsync(string query, LocalFileRetrievalMode mode,
        Func<DateTimeOffset, Action<LocalFileSearchResult>, Task<LocalFileSearchResult>> search, string action, string target,
        Func<bool> canPresent)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(canPresent);
        var context = HostActivity.RequireCurrent();
        if (context.Outcome != HostOperationOutcome.Unknown)
        {
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, time.GetUtcNow(), mode);
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
            void Commit(LocalFileSearchResult outcome) => audit.Write(auditEvent.WithOutcome(
                outcome.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch
                    ? SecurityAuditOutcome.Succeeded : outcome.Outcome == LocalFileSearchOutcome.Cancelled
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, outcome.Outcome.ToString().ToLowerInvariant()));
            if (!LocalFileRetrievalPolicy.TryVersion(mode, out _))
            {
                result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Denied, observedAt, mode);
                Commit(result);
            }
            else
            {
                result = await search(observedAt, Commit).ConfigureAwait(false);
                result = result with { Mode = mode, PolicyVersion = mode == LocalFileRetrievalMode.LexicalLines
                    ? LocalFileRetrievalPolicy.Version : LocalFileRetrievalPolicy.MarkdownVersion };
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            result = LocalFileSearchResult.Empty(LocalFileSearchOutcome.Unavailable, observedAt, mode);
        }
        Report(logger, result.Outcome, result.Citations.Count, result.Truncated);
        activity.Complete(result.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch ? HostOperationOutcome.Completed
            : result.Outcome == LocalFileSearchOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }
}
