using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Bounded original-source review and one-shot confirmation for the canonical retained-submit action.</summary>
public sealed class SessionRequestCommandService(
    SessionWorkspaceService workspace, SessionConversationService conversation,
    ISessionWorkspaceAccess access, TimeProvider time)
{
    private readonly Lock gate = new();
    private Pending? pending;
    private long epoch;

    internal void BindTaskResponseOutput(Func<Kora.Core.Voice.TaskResponseMode, RequestOrigin, TaskResponseOutputReview> review) =>
        conversation.BindTaskResponseOutput(review);

    public void Retire()
    {
        lock (gate) { pending = null; epoch++; }
    }

    public async Task<SessionCommandResult> ExecuteAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token, ForegroundConversationTarget? foregroundTarget = null,
        Action? expiredOrdinaryAccepted = null)
    {
        SessionConversationService.RequireOriginalSource(origin);
        if (command.IsConversationDispatch)
        { return await ExecuteDispatchAsync(command, origin, admitted, token).ConfigureAwait(false); }
        ActivityLink[] sourceLinks = HostActivity.Current?.Activity is { } source ? [new(source.Context)] : [];
        Pending? previous;
        long attempt;
        lock (gate) { previous = pending; pending = null; attempt = ++epoch; }
        var control = access.ControlRevision;
        bool Live() => !token.IsCancellationRequested && access.CanInspect && access.CanControl && access.ControlRevision == control && admitted()
            && (!command.RequestForegroundTarget || foregroundTarget?.IsCurrent == true);
        if (command.RequestForegroundTarget)
        {
            if (command.Operation != SessionCommandOperation.RequestReview || command.SessionId is not null
                || command.RequestActiveTarget || command.RequestTargetName is not null
                || command.RequestProvider != SessionRequestReview.ProviderChoice.Default
                || foregroundTarget is null || !Live())
            { throw new InvalidOperationException("No unique eligible foreground conversation is current. Show and focus an Active conversation, then request a fresh review. Nothing was submitted."); }
            var entry = foregroundTarget.Entry;
            command = command with { SessionId = entry.Authority.SessionId.Value,
                Generation = entry.Authority.Generation.Value, MetadataRevision = entry.Metadata?.Revision.Value ?? 0 };
        }

        if (command.RequestUsesPendingReview)
        {
            if (command.SessionId is not null || command.RequestReviewId is not null
                || command.Operation is not (SessionCommandOperation.RequestConfirm or SessionCommandOperation.RequestCancel)
                || previous is null)
            { throw new InvalidOperationException("No single current host-held request review is available. Review an explicit conversation first."); }
            command = command with
            { SessionId = previous.Review.Target.Authority.SessionId.Value, RequestReviewId = previous.Review.Id };
        }
        if (command.SessionId is null && command.Operation == SessionCommandOperation.RequestReview
            && (command.RequestActiveTarget || command.RequestTargetName is not null))
        {
            using var resolution = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
            try
            {
                var resolved = await ResolveReadableAsync(command, Live, token).ConfigureAwait(false);
                command = command with
                { SessionId = resolved.Authority.SessionId.Value, Generation = resolved.Authority.Generation.Value,
                    MetadataRevision = resolved.Metadata?.Revision.Value ?? 0 };
                resolution.Complete(HostOperationOutcome.Completed);
            }
            catch { resolution.Complete(HostOperationOutcome.Failed); throw; }
        }
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("An exact or unambiguously host-resolved conversation is required."));
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request, sourceLinks);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Original private input admission changed; initiate a fresh review."); }
            if (command.Operation == SessionCommandOperation.RequestReview)
            {
                if (command.RequestExpiredMessage is not null
                    && (origin != RequestOrigin.LocalUi || command.RequestFailedMessage is not null
                        || command.RequestBody is not null || command.RequestLocalProfile is not null
                        || command.RequestTaskResponseMode is not null
                        || command.RequestProvider != SessionRequestReview.ProviderChoice.Default
                        || command.RequestForegroundTarget || !command.RequestClaudeTools.IsEmpty))
                { throw new InvalidOperationException("Expired-text review requires a fresh native ordinary local request, not replay or another source route."); }
                if (!Enum.IsDefined(command.RequestProvider))
                { throw new InvalidOperationException("An exact supported request provider is required."); }
                if (!command.RequestClaudeTools.IsEmpty)
                {
                    if (command.RequestProvider != SessionRequestReview.ProviderChoice.Claude)
                    { throw new InvalidOperationException("Observation scope requires an explicit Claude request."); }
                    _ = ClaudeObservationTools.Constrain(command.RequestClaudeTools);
                }
                var body = command.RequestExpiredMessage?.Body ?? command.RequestFailedMessage?.Body ?? command.RequestBody
                    ?? throw new InvalidOperationException("An original request body is required.");
                ConversationText.Validate(body, submission: true);
                var target = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
                if (target.Authority.SessionId != session || !target.Authority.IsActive
                    || target.Authority.Generation.Value != command.Generation
                    || (target.Metadata?.Revision.Value ?? 0) != command.MetadataRevision || !Live()
                    || command.RequestForegroundTarget && target != foregroundTarget!.Entry)
                { throw new InvalidOperationException("Review requires the exact current Active session, generation and metadata revision."); }
                var review = new SessionRequestReview(Guid.NewGuid(), target, body, origin, time.GetUtcNow().AddSeconds(60),
                    command.RequestProvider) { ClaudeTools = command.RequestClaudeTools, LocalRequestProfile = command.RequestLocalProfile };
                TaskResponseOutputReview? output = null;
                if (command.RequestTaskResponseMode is { } outputMode)
                {
                    if (command.RequestProvider != SessionRequestReview.ProviderChoice.Default || command.RequestFailedMessage is not null
                        || command.RequestLocalProfile is not null || !command.RequestClaudeTools.IsEmpty)
                    { throw new InvalidOperationException("One-task output review applies only to a new ordinary request."); }
                    output = conversation.ReviewTaskResponseOutput(outputMode, origin);
                    review = review with { TaskResponseMode = output.Declared, ConfiguredTaskResponseMode = output.Configured };
                }
                LocalRequestProfileReview? localProfile = null;
                if (command.RequestLocalProfile is { } profile)
                {
                    if (command.RequestProvider != SessionRequestReview.ProviderChoice.Default || command.RequestFailedMessage is not null
                        || command.RequestForegroundTarget || !command.RequestClaudeTools.IsEmpty)
                    { throw new InvalidOperationException("Request-only local selection cannot combine with another provider or retry route."); }
                    localProfile = await conversation.ReviewLocalRequestProfileAsync(target, body, origin, profile, Live, token).ConfigureAwait(false);
                }
                ModelTurnHost.RetryProviderReview? retryProvider = null;
                Func<bool>? bareCurrent = null;
                ConversationPendingSnapshot? queue = null;
                if (command.RequestExpiredMessage is { } expired)
                {
                    bareCurrent = conversation.ReviewBareOrdinaryRequest(target);
                    var expiredSource = await workspace.ReadExpiredOrdinarySourceAsync(target, expired, Live, token).ConfigureAwait(false);
                    retryProvider = await conversation.ReviewCurrentLocalProviderAsync(target, Live, token).ConfigureAwait(false);
                    queue = await conversation.ReadPendingConversationAsync(target, Live, token).ConfigureAwait(false);
                    review = review with { ExpiredSource = expiredSource, CurrentLocalProvider = retryProvider.Selection,
                        RetryProviderProfile = retryProvider.Profile };
                }
                if (command.RequestFailedMessage is { } failed)
                {
                    if (origin != failed.Request.Origin)
                    { throw new InvalidOperationException("Failed-request retry requires current admission of its original channel. Retained voice content cannot create or renew activated-voice authority from a native button."); }
                    if (command.RequestForegroundTarget || command.RequestProvider != SessionRequestReview.ProviderChoice.Default
                        || command.RequestBody is not null)
                    { throw new InvalidOperationException("Failed-request review cannot replace its exact original body, provider or scope."); }
                    var failedSource = await workspace.ReadFailedRetrySourceAsync(target, failed, Live, token).ConfigureAwait(false);
                    review = review with { FailedSource = failedSource, Origin = failed.Request.Origin };
                    retryProvider = await conversation.ReviewRetryProviderAsync(review, Live, token).ConfigureAwait(false);
                    review = review with { RetryProviderProfile = retryProvider.Profile };
                }
                lock (gate)
                {
                    if (epoch != attempt || !Live()) { throw new InvalidOperationException("Review admission changed."); }
                    pending = new(review, Live, control, new(activity.Activity!.Context), command.RequestForegroundTarget,
                        retryProvider, localProfile, bareCurrent, queue, output);
                }
                activity.Complete(HostOperationOutcome.Completed);
                return new("review", output is not null
                    ? "This task only. Review unchanged input, exact conversation, original channel and declared/configured output. Confirm separately once; mandatory call/privacy/output policy wins. No saved preference, speech or submission yet."
                    : localProfile is not null
                    ? "This request only - LOCAL. Review the exact destination, complete profile and unchanged original input below. Confirm separately once with session request confirm, or cancel with session request cancel. No preference write, submission or dispatch yet. Subsequent requests inherit normally; no fallback."
                    : "Review the exact original body, target, provider, origin and expiry. Confirm separately; no content is retained or dispatched yet. Copilot and Claude additionally require separate native approval of each final physical send.")
                { RequestReview = review };
            }
            if (previous is null || previous.Review.Id != command.RequestReviewId
                || previous.Review.Target.Authority.SessionId != session)
            { throw new InvalidOperationException("The exact pending review is missing or consumed. Initiate a fresh review; no replay."); }
            if (command.Operation == SessionCommandOperation.RequestCancel)
            {
                activity.Complete(HostOperationOutcome.Completed);
                return new("cancelled", "The exact memory-only request review was discarded. No submission or session change.");
            }
            if (command.Operation != SessionCommandOperation.RequestConfirm)
            { throw new InvalidOperationException("Only explicit request review, confirm or cancel is supported."); }
            var accepted = false;
            var confirmedAt = time.GetUtcNow();
            bool OriginalLive()
            {
                lock (gate)
                {
                    return (accepted || epoch == attempt) && Live() && previous.SourceAdmitted()
                        && access.ControlRevision == previous.ControlRevision && time.GetUtcNow() < previous.Review.ExpiresAt
                        && (previous.Review.ExpiredSource is null || time.GetUtcNow() >= previous.Review.ExpiresAt.AddSeconds(-60))
                        && (previous.BareCurrent?.Invoke() ?? true);
                }
            }
            async Task Revalidate(CancellationToken cancellation)
            {
                if (!OriginalLive()) { throw new InvalidOperationException("The original request source or review expired. Review again; later confirmation grants no new authority."); }
                var actual = await workspace.ReadExactMetadataAsync(session, cancellation).ConfigureAwait(false);
                if (actual != previous.Review.Target || !OriginalLive())
                { throw new InvalidOperationException("The reviewed session generation, metadata or original admission changed."); }
                if (previous.LocalProfile is { } local) { await local.ValidateAsync(cancellation).ConfigureAwait(false); }
                if (previous.Review.ExpiredSource is { } expired)
                {
                    if (await workspace.ReadExpiredOrdinarySourceAsync(actual, expired.Message, OriginalLive, cancellation).ConfigureAwait(false) != expired)
                    { throw new InvalidOperationException("The exact expired original or no-dispatch evidence changed. Review again."); }
                    await previous.RetryProvider!.ValidateAsync(cancellation).ConfigureAwait(false);
                    if (!accepted && previous.Queue is { } queue)
                    {
                        var currentQueue = await conversation.ReadPendingConversationAsync(actual, OriginalLive, cancellation).ConfigureAwait(false);
                        if (currentQueue.Revision != queue.Revision)
                        { throw new InvalidOperationException("Submitted work or held dispatch changed. Review again; Resume remains an independent decision."); }
                    }
                }
                if (previous.Review.FailedSource is { } failed)
                {
                    if (await workspace.ReadFailedRetrySourceAsync(actual, failed.Message, OriginalLive, cancellation).ConfigureAwait(false) != failed)
                    { throw new InvalidOperationException("The original failed task or message revision changed. Review again."); }
                    await previous.RetryProvider!.ValidateAsync(cancellation).ConfigureAwait(false);
                }
            }
            await Revalidate(token).ConfigureAwait(false);
            // A local confirmation may confirm voice content, but cannot convert its original source to LocalUi.
            using var submission = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), previous.Review.Origin),
                HostActivityLayer.Application, HostOperation.Request, [previous.Cause, new(activity.Activity!.Context)]);
            ConversationSubmissionReceipt receipt;
            try
            {
                receipt = previous.Review.ExpiredSource is not null
                    ? await conversation.SubmitFreshOrdinaryAsync(previous.Review, previous.RetryProvider!, confirmedAt,
                        OriginalLive, Revalidate, [previous.Cause],
                        () => { lock (gate) { accepted = true; } expiredOrdinaryAccepted?.Invoke(); }, token).ConfigureAwait(false)
                    : previous.Output is { } output
                    ? await conversation.SubmitAsync(previous.Review.Target, previous.Review.Body, OriginalLive, token,
                        Revalidate, [previous.Cause], previous.Review.Origin,
                        accepted: () => { lock (gate) { accepted = true; } }, taskOutput: output).ConfigureAwait(false)
                    : previous.LocalProfile is { } local
                    ? await conversation.SubmitLocalRequestProfileAsync(local, OriginalLive, Revalidate,
                        [previous.Cause], token).ConfigureAwait(false)
                    : previous.RetryProvider is { } provider
                    ? await conversation.SubmitFailedRetryAsync(previous.Review, provider, OriginalLive, Revalidate,
                        [previous.Cause], () => { lock (gate) { accepted = true; } }, token).ConfigureAwait(false)
                    : previous.Foreground
                    ? await conversation.SubmitOriginalLocalAsync(previous.Review.Target, previous.Review.Body, OriginalLive,
                        Revalidate, [previous.Cause], previous.Review.Origin, token).ConfigureAwait(false)
                    : previous.Review.Provider == SessionRequestReview.ProviderChoice.Claude
                    ? await conversation.SubmitReviewedClaudeAsync(previous.Review.Target, previous.Review.Body, OriginalLive,
                        Revalidate, [previous.Cause], previous.Review.Origin, previous.Review.ClaudeTools, token).ConfigureAwait(false)
                    : previous.Review.Provider == SessionRequestReview.ProviderChoice.Copilot
                    ? await conversation.SubmitReviewedCopilotAsync(previous.Review.Target, previous.Review.Body, OriginalLive,
                        Revalidate, [previous.Cause], previous.Review.Origin, token).ConfigureAwait(false)
                    : await conversation.SubmitAsync(previous.Review.Target, previous.Review.Body, OriginalLive, token,
                        Revalidate, [previous.Cause], previous.Review.Origin,
                        accepted: () => { lock (gate) { accepted = true; } }).ConfigureAwait(false);
                submission.Complete(HostOperationOutcome.Completed);
            }
            catch
            {
                submission.Complete(HostOperationOutcome.Failed);
                throw;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return new("retained", (receipt.Message.Outcome is null
                ? "Exact original USER request accepted and pending in its original conversation/channel. Inspect submitted work for position or exact cancellation; no execution success, fallback or authority renewal is implied."
                : "Exact original USER request retained with its original channel and terminal HOST/task outcome. Inspect these receipts; no fallback or automatic replay.")
                + (previous.Output is { } completedOutput ? " " + completedOutput.Status : string.Empty))
            { RequestSubmission = receipt };
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private async Task<SessionCommandResult> ExecuteDispatchAsync(SessionCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        var control = access.ControlRevision;
        var created = time.GetUtcNow();
        bool Live() => access.CanInspect && access.CanControl && access.ControlRevision == control && admitted()
            && time.GetUtcNow() >= created && time.GetUtcNow() < created.AddSeconds(10);
        if (!Live()) { throw new InvalidOperationException("Original private dispatch-control input is unavailable."); }
        var session = new HostId<SessionIdentity>(command.SessionId
            ?? throw new InvalidOperationException("One exact existing conversation is required."));
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);
        try
        {
            var target = await workspace.ReadExactMetadataAsync(session, token).ConfigureAwait(false);
            if (target.Authority.SessionId != session || !target.Authority.IsActive || !Live())
            { throw new InvalidOperationException("Dispatch control requires the exact Active conversation and current original input. Done is not resumed."); }
            var snapshot = await conversation.ReadPendingConversationAsync(target, Live, token).ConfigureAwait(false);
            if (command.Operation != SessionCommandOperation.ConversationDispatchStatus)
            {
                await conversation.SetManualDispatchHoldAsync(snapshot.DispatchControl
                    ?? throw new InvalidOperationException("A preceding generation still owns submitted work. Current-generation dispatch control is unavailable."),
                    command.Operation == SessionCommandOperation.ConversationDispatchPause
                        ? ConversationDispatchHoldReason.OriginalUser : ConversationDispatchHoldReason.None,
                    Live, token, origin).ConfigureAwait(false);
                snapshot = await conversation.ReadPendingConversationAsync(target, Live, token).ConfigureAwait(false);
            }
            if (!Live()) { throw new InvalidOperationException("Dispatch-control receipt admission changed. Refresh exact work; no rollback is claimed."); }
            activity.Complete(HostOperationOutcome.Completed);
            return new("observed", "Manual dispatch hold: " + snapshot.ManualHoldReason
                + ". Independent recovery hold: " + snapshot.Paused
                + ". Current-generation manual control available: " + (snapshot.DispatchControl is not null)
                + ". Pending ordinary requests: " + snapshot.Turns.Count(item => item.Phase == ConversationPendingPhase.Pending)
                + ". Already dispatching/running work continues unchanged. Original FIFO, source, permission and expiry remain required. "
                + "No activity renewal, action approval, replay or diagnostic queue change.");
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private async Task<SessionWorkspaceEntry> ResolveReadableAsync(SessionCommand command, Func<bool> live, CancellationToken token)
    {
        if (!live() || command.RequestActiveTarget && command.RequestTargetName is not null)
        { throw new InvalidOperationException("One explicit original conversation selector is required."); }
        Guid? after = null;
        var cursors = new HashSet<Guid>();
        var subjects = new HashSet<Guid>();
        SessionWorkspaceEntry? match = null;
        for (var pageNumber = 0; pageNumber < 16; pageNumber++)
        {
            var page = await workspace.ReadMetadataAsync(after, SessionPage<SessionWorkspaceEntry>.MaximumRecords, token).ConfigureAwait(false);
            if (!live()) { throw new InvalidOperationException("Original conversation selection admission changed."); }
            if (page.Records.IsDefault || page.Records.Length > SessionPage<SessionWorkspaceEntry>.MaximumRecords)
            { throw new InvalidDataException("Conversation inventory page is incomplete or oversized."); }
            foreach (var entry in page.Records)
            {
                if (!subjects.Add(entry.Authority.SessionId.Value))
                { throw new InvalidDataException("Conversation inventory repeated a subject."); }
                if (entry.Authority.IsActive && (command.RequestActiveTarget
                    || string.Equals(entry.Metadata?.Name.Value, command.RequestTargetName!.Value, StringComparison.Ordinal)))
                {
                    if (match is not null) { throw new InvalidOperationException("Conversation selection is ambiguous. Choose an exact unique readable name in Sessions."); }
                    match = entry;
                }
            }
            if (page.Next is null)
            { return match ?? throw new InvalidOperationException("No matching current Active conversation is available. Refresh Sessions."); }
            if (!cursors.Add(page.Next.Value)) { throw new InvalidDataException("Conversation inventory cursor repeated."); }
            after = page.Next;
        }
        throw new InvalidOperationException("Conversation inventory exceeds the bounded complete resolution limit. Use native Sessions to choose the exact conversation.");
    }

    private sealed record Pending(SessionRequestReview Review, Func<bool> SourceAdmitted, long ControlRevision, ActivityLink Cause,
        bool Foreground, ModelTurnHost.RetryProviderReview? RetryProvider, LocalRequestProfileReview? LocalProfile,
        Func<bool>? BareCurrent, ConversationPendingSnapshot? Queue, TaskResponseOutputReview? Output);
}
