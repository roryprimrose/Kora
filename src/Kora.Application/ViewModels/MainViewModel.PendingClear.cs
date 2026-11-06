using System.Text;

using Kora.Application.Diagnostics;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private PendingClearInput? pendingClearInput;
    private ConversationPendingClearSnapshot? pendingClearSnapshot;
    public bool HasPendingClearReview => pendingClearSnapshot is not null;
    public bool HasSessionOperationReview => HasPendingClearReview || HasForegroundRequestReview;
    public string PendingClearReviewText => pendingClearSnapshot is { } snapshot
        ? Encoding.UTF8.GetString(SessionCommandResult.Serialize(
            new("review", Kora.Application.Hosting.SessionConversationService.PendingClearImpact) { PendingClearReview = snapshot }))
        : string.Empty;

    private void SetPendingClearReview(ConversationPendingClearSnapshot? snapshot)
    {
        pendingClearSnapshot = snapshot;
        OnPropertyChanged(nameof(HasPendingClearReview));
        OnPropertyChanged(nameof(HasSessionOperationReview));
        OnPropertyChanged(nameof(PendingClearReviewText));
    }

    public Func<Task> CapturePendingClearDecision(bool confirm)
    {
        var snapshot = pendingClearSnapshot;
        var fence = pendingClearInput;
        return async () =>
        {
            bool Current() => fence is not null && !fence.Retired && fence.Source()
                && fence.Input == Interlocked.Read(ref workspaceSurfaceInputRevision)
                && fence.Composer == Interlocked.Read(ref attachmentCatalogueComposerRevision);
            if (snapshot is null || fence is null || fence.Decision || !ReferenceEquals(snapshot, pendingClearSnapshot) || !Current())
            { ReportHostInteractionFailure("The exact pending-only clear review or original input changed. Review again; no cancellation was authorized."); return; }
            fence.Decision = true;
            SetPendingClearReview(null);
            try
            {
                var command = new SessionCommand(confirm ? SessionCommandOperation.PendingClearConfirm : SessionCommandOperation.PendingClearCancel,
                    snapshot.Session.Value) { PendingClearReviewId = snapshot.Id };
                var result = await (sessionRequests ?? throw new InvalidOperationException("The canonical ordinary pending-clear service is unavailable."))
                    .ExecuteAsync(command, snapshot.Origin, Current, CancellationToken.None);
                if (Current()) { ShowInformation("Pending-only clear " + result.Outcome + ".", Encoding.UTF8.GetString(SessionCommandResult.Serialize(result))); }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ApplicationLog.Error(logger, exception, "Confirming an original pending-only clear review");
                if (!disposed && IsHostInputEligible)
                { ShowFailure("Pending-only clear unconfirmed.", exception.Message + " Inspect exact per-original receipts; no rollback or replay is claimed."); }
            }
        };
    }

    private sealed class PendingClearInput(RequestOrigin origin, long input, long composer, Func<bool> source)
    {
        internal RequestOrigin Origin { get; } = origin;
        internal long Input = input;
        internal long Composer = composer;
        internal bool Retired;
        internal bool Decision;
        internal Func<bool> Source { get; } = source;
    }

    private Func<bool> BindPendingClearInput(SessionCommand command, RequestOrigin origin, Func<bool> source)
    {
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var composer = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        PendingClearInput fence;
        if (command.Operation == SessionCommandOperation.PendingClearReview)
        {
            RetirePendingClearInput();
            fence = pendingClearInput = new(origin, input, composer, source);
        }
        else
        {
            var previous = pendingClearInput;
            if (previous is null || previous.Decision || previous.Origin != origin
                || input != previous.Input + 1
                || composer != previous.Composer + (origin == RequestOrigin.LocalUi ? 1 : 0))
            {
                RetirePendingClearInput();
                return () => false;
            }
            fence = previous;
            fence.Input = input;
            fence.Composer = composer;
            fence.Decision = true;
        }
        return () => !fence.Retired && fence.Source() && source()
            && fence.Input == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && fence.Composer == Interlocked.Read(ref attachmentCatalogueComposerRevision);
    }

    private void RetirePendingClearInput()
    {
        if (pendingClearInput is { } input) { input.Retired = true; pendingClearInput = null; }
        SetPendingClearReview(null);
    }
}
