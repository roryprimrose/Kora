using Kora.Application.Interaction;
using Kora.Application.Communication;
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

    internal async Task AskVersionAsync(Func<NativeQuestionViewModel, Task> present,
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
            new("Show the local Kora version and private-storage disclosure?",
                QuestionKind.SingleChoice, [new("show", "Show local version")], purpose: "local-version", sourceId: "host"),
            time.GetUtcNow().AddMinutes(5), cancellationToken);
        if (presented.Outcome != HostInteractionOutcome.Presented || presented.Question is null)
        {
            throw new InvalidOperationException("The durable native question was not admitted.");
        }
        var state = CreateState(presented.Question);
        await present(state);
        var result = await state.Completion;
        if (result.Outcome == HostInteractionOutcome.Cancelled)
        {
            throw new OperationCanceledException("The local version question was explicitly cancelled.", cancellationToken);
        }
        if (result.Outcome != HostInteractionOutcome.Answered || result.Question is not { } answered
            || answered.Key.Request != request || answered.Key.QuestionId != state.Key.QuestionId
            || answered.Status != QuestionStatus.Answered || answered.AnswerChannel != RequestOrigin.LocalUi
            || !canInteract())
        {
            throw new InvalidOperationException("The exact native question did not produce an eligible committed answer.");
        }
    }

    // Only a trusted host with an existing admitted record calls this; never resolve by focused window.
    internal NativeQuestionViewModel CreateState(HostQuestionRecord question) =>
        new(question, questions, authorization, reviews, () => canInteract(), time, logger);
}
