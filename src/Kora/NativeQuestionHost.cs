using Kora.Application.Interaction;
using Kora.Application.Communication;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class NativeQuestionHost : IDisposable
{
    private static readonly TimeSpan VersionQuestionLifetime = TimeSpan.FromMinutes(5);
    private readonly WindowsSqliteHostInteractionStore store;
    private readonly HostQuestionService questions;
    private readonly HostAuthorizationService authorization;
    private readonly HostQuestionReviewService reviews;
    private readonly TimeProvider time;
    private readonly ILogger<NativeQuestionViewModel> logger;
    private Func<bool> canInteract = static () => false;
    private Func<CallPolicyObservation>? callObservation;
    private SessionWorkspaceService? workspace;
    private NativeQuestionViewModel? current;
    private int waiting;
    private volatile bool disposed;

    internal NativeQuestionHost(WindowsSqliteHostInteractionStore store, TimeProvider time,
        ILogger<NativeQuestionViewModel> logger)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        var gated = new NativeInteractionStore(store, () => !disposed && canInteract());
        questions = new(gated, time);
        authorization = new(gated, time);
        reviews = new(gated, time);
    }

    internal void BindGate(Func<bool> gate) => canInteract = gate;
    internal void BindCallObservation(Func<CallPolicyObservation> observe) => callObservation = observe;
    internal void BindWorkspace(SessionWorkspaceService service) => workspace = service;

    internal async Task<HostTaskRecord> AskVersionAsync(Func<NativeQuestionViewModel, Task> present,
        CancellationToken cancellationToken)
    {
        var request = HostActivity.RequireCurrent().Request;
        if (disposed || request.Origin != RequestOrigin.LocalUi || !canInteract()
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        {
            throw new InvalidOperationException("Native privacy/ownership or current-run admission is unavailable.");
        }
        try { return await AskAdmittedVersionAsync(request, present, cancellationToken); }
        finally
        {
            if (current is { Completion.IsCompleted: false } pending)
            {
                pending.Close("Original current-run wait ended. Retained questions are read-only; start a fresh request.");
            }
            current = null;
            Volatile.Write(ref waiting, 0);
        }
    }

    private async Task<HostTaskRecord> AskAdmittedVersionAsync(HostRequest request,
        Func<NativeQuestionViewModel, Task> present, CancellationToken cancellationToken)
    {
        await store.CreateSessionAsync(request, cancellationToken);
        // This ordinary read has no admitted exact effect/content/containment authority.
        var policy = callObservation?.Invoke().Authorization(canInteract(), mandatoryGatesSatisfied: false)
            ?? new(canInteract(), false, false, true);
        await store.PublishTrustedSnapshotAsync(request, policy,
            proposal: null, expectedObservationRevision: 0, cancellationToken);
        var presented = await questions.CreateAsync(request,
            LocalVersionWait.CreateSpec(),
            time.GetUtcNow().Add(VersionQuestionLifetime), cancellationToken);
        if (presented.Outcome != HostInteractionOutcome.Presented || presented.Question is null)
        {
            throw new InvalidOperationException("The durable native question was not admitted.");
        }
        await store.AdmitVersionWaitAsync(presented.Question.Key, cancellationToken);
        if (disposed || !canInteract()) { throw new InvalidOperationException("The original native wait admission was withdrawn."); }
        var admittedWorkspace = workspace ?? throw new InvalidOperationException("The native task control workspace is not bound.");
        var state = new NativeQuestionViewModel(presented.Question, questions, authorization, reviews,
            () => !disposed && canInteract(), time, logger, async key =>
        {
            var cancelled = await admittedWorkspace.CancelTaskAsync(new(key.Request.SessionId, key.Request.TaskId,
                new(1), presented.Question.SessionGeneration, key.QuestionId, key.Revision),
                RequestOrigin.LocalUi, () => !disposed && canInteract(), cancellationToken);
            return new(HostInteractionOutcome.Cancelled, "pre-dispatch-work-cancelled", cancelled.Question);
        }, dismissible: true);
        // This one live continuation is the registration; persisted metadata cannot create one.
        current = state;
        var externallyCancelled = new TaskCompletionSource<HostTaskObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCancelled(HostTaskObservation observation)
        {
            if (observation.Task.Request == request)
            {
                externallyCancelled.TrySetResult(observation);
            }
        }
        admittedWorkspace.WaitingTaskCancelled += OnCancelled;
        var budget = state.Question.ExpiresAt - time.GetUtcNow();
        if (budget > VersionQuestionLifetime) { budget = VersionQuestionLifetime; }
        if (budget < TimeSpan.Zero) { budget = TimeSpan.Zero; }
        using var deadline = new CancellationTokenSource(budget, time);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var monitor = MonitorAsync(state, budget, lifetime.Token);
        try
        {
            var observation = await store.ReadTaskAsync(request.SessionId, request.TaskId, cancellationToken);
            if (observation?.Task.State == HostTaskState.Cancelled) { state.AcceptCommittedCancellation(observation); }
            var presentedTask = present(state);
            var first = await Task.WhenAny(presentedTask, state.Completion, externallyCancelled.Task).WaitAsync(lifetime.Token);
            if (first == externallyCancelled.Task)
            {
                state.AcceptCommittedCancellation(await externallyCancelled.Task);
            }
            if (first == presentedTask) { await presentedTask; }
            if (await Task.WhenAny(state.Completion, externallyCancelled.Task).WaitAsync(lifetime.Token) == externallyCancelled.Task)
            {
                state.AcceptCommittedCancellation(await externallyCancelled.Task);
            }
            var decision = await state.Completion;
            if (decision.Outcome is not (HostInteractionOutcome.Answered or HostInteractionOutcome.Cancelled))
            {
                throw new InvalidOperationException("The original native wait ended without an eligible answer or atomic cancellation.");
            }
            await presentedTask.WaitAsync(lifetime.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.Close("Original presentation/wait failed or was cancelled. Start a fresh request; no old task is replayed.");
            throw;
        }
        finally
        {
            admittedWorkspace.WaitingTaskCancelled -= OnCancelled;
            await lifetime.CancelAsync();
            await monitor;
        }
        var result = await state.Completion;
        if (result.Outcome == HostInteractionOutcome.Cancelled)
        {
            return (await store.ReadTaskAsync(request.SessionId, request.TaskId, cancellationToken))!.Task;
        }
        if (result.Outcome != HostInteractionOutcome.Answered || result.Question is not { } answered
            || answered.Key.Request != request || answered.Key.QuestionId != state.Key.QuestionId
            || answered.Status != QuestionStatus.Answered || answered.AnswerChannel != RequestOrigin.LocalUi
            || state.IsWithdrawn || disposed || !canInteract())
        {
            throw new InvalidOperationException("The exact native question did not produce an eligible committed answer.");
        }
        return await store.AdmitVersionDispatchAsync(answered.Key, () => !disposed && canInteract(), cancellationToken);
    }

    internal async Task<NativeQuestionViewModel?> ReturnToCurrentQuestionAsync(CancellationToken cancellationToken)
    {
        var state = current;
        if (disposed || state is null || state.Completion.IsCompleted || state.HasPendingOperation) { return null; }
        try
        {
            if (!await IsCurrentWaitAsync(state, cancellationToken))
            {
                state.Close("Recovery unavailable: the exact original wait is no longer live. Inspect status and start a fresh request.");
                return null;
            }
            if (state.HasPresentation) { await state.RefreshTargetAsync(); }
            else { await state.RestorePresentationAsync(); }
            if (!ReferenceEquals(current, state) || state.Completion.IsCompleted || disposed || !canInteract())
            {
                state.Close("Recovery withdrawn. The original workflow cannot be revived.");
                return null;
            }
            return state.HasPresentation && state.IsEditable ? state : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.Fail(exception);
            throw;
        }
    }

    private async Task<bool> IsCurrentWaitAsync(NativeQuestionViewModel state, CancellationToken token)
    {
        if (disposed || !canInteract() || state.Completion.IsCompleted || state.Question.ExpiresAt <= time.GetUtcNow()) { return false; }
        var key = state.Key;
        var session = await store.ReadSessionAsync(key.Request.SessionId, token);
        var observed = await store.ReadTaskAsync(key.Request.SessionId, key.Request.TaskId, token);
        if (observed?.Task.State == HostTaskState.Cancelled) { state.AcceptCommittedCancellation(observed); return false; }
        return !disposed && canInteract() && ReferenceEquals(current, state) && !state.Completion.IsCompleted
            && session is { IsActive: true } && session.Generation == state.Question.SessionGeneration
            && observed is { CurrentSource: true, Task.State: HostTaskState.IntentRecorded, Task.Revision.Value: 1 }
            && observed.Task.Request == key.Request && string.Equals(observed.Source, LocalVersionWait.Source, StringComparison.Ordinal)
            && observed.Generation == state.Question.SessionGeneration
            && observed.Question is { Status: QuestionStatus.Pending, Proposal: null } question
            && question.Key == state.Key && question.ExpiresAt == state.Question.ExpiresAt
            && question.SessionGeneration == state.Question.SessionGeneration && LocalVersionWait.Matches(question)
            && question.ExpiresAt > time.GetUtcNow();
    }

    private async Task MonitorAsync(NativeQuestionViewModel state, TimeSpan budget, CancellationToken token)
    {
        var started = time.GetTimestamp();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                state.RefreshEligibility();
                if (state.Completion.IsCompleted) { return; }
                if (time.GetElapsedTime(started) >= budget)
                {
                    state.Close("Original five-minute question lifetime ended. Start a fresh request; reopening never extends it.");
                    return;
                }
                var key = state.Key;
                if (!state.HasPendingOperation && !await IsCurrentWaitAsync(state, token)
                    && !state.HasPendingOperation && state.Key == key && !state.Completion.IsCompleted)
                {
                    state.Close("Original current-run wait unavailable. Inspect retained status and start a fresh request; nothing was replayed.");
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException) { state.Fail(exception); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        current?.Close("Native host disposed. No old question or continuation can be reopened.");
        current = null;
    }

    // Only a trusted host with an existing admitted record calls this; never resolve by focused window.
    internal NativeQuestionViewModel CreateState(HostQuestionRecord question,
        Func<HostQuestionKey, Task<HostInteractionDecision>>? cancel = null) =>
        new(question, questions, authorization, reviews, () => !disposed && canInteract(), time, logger, cancel);
}
