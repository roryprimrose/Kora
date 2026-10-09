using Kora.Application.Interaction;
using Kora.Application.Communication;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class NativeQuestionHost
{
    private readonly WindowsSqliteHostInteractionStore store;
    private readonly HostQuestionService questions;
    private readonly HostAuthorizationService authorization;
    private readonly HostQuestionReviewService reviews;
    private readonly TimeProvider time;
    private readonly ILogger<NativeQuestionViewModel> logger;
    private Func<bool> canInteract = static () => false;
    private Func<CallPolicyObservation>? callObservation;
    private SessionWorkspaceService? workspace;

    internal NativeQuestionHost(WindowsSqliteHostInteractionStore store, TimeProvider time,
        ILogger<NativeQuestionViewModel> logger)
    {
        this.store = store;
        this.time = time;
        this.logger = logger;
        var gated = new NativeInteractionStore(store, () => canInteract());
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
        if (!canInteract()) { throw new InvalidOperationException("Native privacy/ownership admission is unavailable."); }
        await store.CreateSessionAsync(request, cancellationToken);
        // This ordinary read has no admitted exact effect/content/containment authority.
        var policy = callObservation?.Invoke().Authorization(canInteract(), mandatoryGatesSatisfied: false)
            ?? new(canInteract(), false, false, true);
        await store.PublishTrustedSnapshotAsync(request, policy,
            proposal: null, expectedObservationRevision: 0, cancellationToken);
        var presented = await questions.CreateAsync(request,
            LocalVersionWait.CreateSpec(),
            time.GetUtcNow().AddMinutes(5), cancellationToken);
        if (presented.Outcome != HostInteractionOutcome.Presented || presented.Question is null)
        {
            throw new InvalidOperationException("The durable native question was not admitted.");
        }
        await store.AdmitVersionWaitAsync(presented.Question.Key, cancellationToken);
        var admittedWorkspace = workspace ?? throw new InvalidOperationException("The native task control workspace is not bound.");
        var state = CreateState(presented.Question, async key =>
        {
            var cancelled = await admittedWorkspace.CancelTaskAsync(new(key.Request.SessionId, key.Request.TaskId,
                new(1), presented.Question.SessionGeneration, key.QuestionId, key.Revision),
                RequestOrigin.LocalUi, () => canInteract(), cancellationToken);
            return new(HostInteractionOutcome.Cancelled, "pre-dispatch-work-cancelled", cancelled.Question);
        });
        var externallyCancelled = new TaskCompletionSource<HostTaskObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCancelled(HostTaskObservation observation)
        {
            if (observation.Task.Request == request) { externallyCancelled.TrySetResult(observation); }
        }
        admittedWorkspace.WaitingTaskCancelled += OnCancelled;
        try
        {
            var current = await store.ReadTaskAsync(request.SessionId, request.TaskId, cancellationToken);
            if (current?.Task.State == HostTaskState.Cancelled) { state.AcceptCommittedCancellation(current); }
            var presentedTask = present(state);
            if (await Task.WhenAny(presentedTask, externallyCancelled.Task) == externallyCancelled.Task)
            {
                state.AcceptCommittedCancellation(await externallyCancelled.Task);
            }
            await presentedTask;
        }
        finally { admittedWorkspace.WaitingTaskCancelled -= OnCancelled; }
        var result = await state.Completion;
        if (result.Outcome == HostInteractionOutcome.Cancelled)
        {
            return (await store.ReadTaskAsync(request.SessionId, request.TaskId, cancellationToken))!.Task;
        }
        if (result.Outcome != HostInteractionOutcome.Answered || result.Question is not { } answered
            || answered.Key.Request != request || answered.Key.QuestionId != state.Key.QuestionId
            || answered.Status != QuestionStatus.Answered || answered.AnswerChannel != RequestOrigin.LocalUi
            || !canInteract())
        {
            throw new InvalidOperationException("The exact native question did not produce an eligible committed answer.");
        }
        return await store.AdmitVersionDispatchAsync(answered.Key, () => canInteract(), cancellationToken);
    }

    // Only a trusted host with an existing admitted record calls this; never resolve by focused window.
    internal NativeQuestionViewModel CreateState(HostQuestionRecord question,
        Func<HostQuestionKey, Task<HostInteractionDecision>>? cancel = null) =>
        new(question, questions, authorization, reviews, () => canInteract(), time, logger, cancel);
}
