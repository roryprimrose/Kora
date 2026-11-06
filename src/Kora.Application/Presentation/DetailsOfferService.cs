using System.Security.Cryptography;
using System.Text;

using Kora.Application.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;

namespace Kora.Application.Presentation;

/// <summary>Owns one source-bound presentation question, without execution or microphone authority.</summary>
public sealed class DetailsOfferService(IDetailsOfferStore store, TimeProvider time) : IAsyncDisposable
{
    private readonly HostQuestionService questions = new(store, time);
    private readonly Lock gate = new();
    private volatile CancellationTokenSource? lifetime;
    private sealed class PendingOffer(HostQuestionRecord question, Func<bool> admitted, DetailsOfferRequest request)
    {
        internal HostQuestionRecord Question { get; set; } = question;
        internal Func<bool> Admitted { get; } = admitted;
        internal DetailsOfferRequest Request { get; } = request;
        internal TaskCompletionSource<HostQuestionRecord> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private volatile PendingOffer? pending;
    private volatile Func<bool>? sourceCurrent;
    private bool active;
    private bool answering;
    private TaskCompletionSource? joined;
    private TaskCompletionSource? answerJoined;
    private volatile bool disposed;

    public Task RunAsync(DetailsOfferRequest request, Func<bool> admitted,
        Func<HostQuestionRecord, Func<bool>, Task> present, Func<Func<bool>, Task<bool>> reveal, CancellationToken token)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active || answering) { throw new InvalidOperationException("Another exact Details offer already owns the target."); }
            if (!request.TryConsume()) { throw new InvalidOperationException("This exact original request's offer attempt was already consumed."); }
            active = true;
            joined = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return RunConsumedAsync(request, admitted, present, reveal, completion, token);
    }

    private async Task RunConsumedAsync(DetailsOfferRequest request, Func<bool> admitted,
        Func<HostQuestionRecord, Func<bool>, Task> present, Func<Func<bool>, Task<bool>> reveal,
        TaskCompletionSource completion, CancellationToken token)
    {
        try { await RunCoreAsync(request, admitted, present, reveal, token).ConfigureAwait(false); }
        finally
        {
            try { request.Finish(); }
            finally
            {
                lock (gate) { active = false; completion.TrySetResult(); }
            }
        }
    }

    private async Task RunCoreAsync(DetailsOfferRequest request, Func<bool> admitted,
        Func<HostQuestionRecord, Func<bool>, Task> present, Func<Func<bool>, Task<bool>> reveal, CancellationToken token)
    {
        var message = request.Receipt.Message;
        var response = message.AssistantResponse ?? throw new InvalidOperationException("An actual retained answer is required.");
        if (!request.IsCurrent() || !admitted() || !request.Receipt.Task.IsTerminal
            || !AnswerPresentation.From(response.Body).HasDetails)
        { throw new InvalidOperationException("The opted original result is no longer eligible for an exact Details offer."); }
        var created = time.GetUtcNow();
        var stamp = time.GetTimestamp();
        var control = new HostRequest(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.HostSystem);
        var source = new DetailsOfferBinding(message.Request, message.Generation, request.Target.Metadata?.Revision.Value ?? 0,
            message.Sequence, message.Revision, response.Provenance.Turn.Value,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.Body))), request.OptIn, created, created.AddSeconds(60));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60), time);
        using var owned = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        lock (gate) { lifetime = owned; }
        var revoked = false;
        bool Live()
        {
            if (revoked) { return false; }
            if (disposed || owned.IsCancellationRequested || !request.IsCurrent() || !admitted()
                || time.GetUtcNow() < created || time.GetElapsedTime(stamp) >= TimeSpan.FromSeconds(60)
                || pending is { } offer && time.GetUtcNow() >= offer.Question.ExpiresAt)
            { revoked = true; return false; }
            return true;
        }
        sourceCurrent = Live;
        using var host = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Presentation, request.Causes);
        try
        {
            var question = await store.AdmitDetailsOfferAsync(source, Live, token).ConfigureAwait(false);
            var binding = question.DetailsOffer
                ?? throw new InvalidDataException("Details admission returned no authoritative source binding.");
            binding.Validate(question);
            if (question.Key.Request != control || question.Key.Revision.Value != 1
                || question.Status != QuestionStatus.Pending || question.Draft is not null || question.AnswerChannel is not null
                || binding.ExpiresAt > source.ExpiresAt
                || binding != source with { RunId = binding.RunId, ExpiresAt = binding.ExpiresAt })
            { throw new InvalidDataException("Details admission did not preserve the exact new control and original source."); }
            request.BindPresentation(control);
            var offered = new PendingOffer(question, Live, request);
            lock (gate) { pending = offered; }
            if (!Live()) { throw new OperationCanceledException("Details presentation was withdrawn."); }
            await present(question, Live).ConfigureAwait(false);
            if (!Live()) { throw new OperationCanceledException("Details presentation was withdrawn before acknowledgement."); }
            var accepted = await offered.Answer.Task.WaitAsync(owned.Token).ConfigureAwait(false);
            if (!Live()) { throw new OperationCanceledException("The exact offer source was retired."); }
            var yes = accepted.Draft!.Choices.SequenceEqual(["yes"]);
            var revealed = yes && await reveal(Live).ConfigureAwait(false);
            if (yes && !revealed) { throw new InvalidOperationException("Exact Details reveal was not acknowledged; no retry was admitted."); }
            await store.CompleteDetailsOfferAsync(accepted.Key, revealed, Live, CancellationToken.None).ConfigureAwait(false);
            if (revealed) { request.ConfirmReveal(); }
            pending = null;
            host.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) when (owned.IsCancellationRequested || !Live())
        { host.Complete(HostOperationOutcome.Cancelled); }
        finally
        {
            try
            {
                if (pending is { } unfinished)
                { await store.CompleteDetailsOfferAsync(unfinished.Question.Key, false, static () => false, CancellationToken.None).ConfigureAwait(false); }
            }
            finally
            {
                lock (gate) { pending = null; sourceCurrent = null; lifetime = null; }
            }
        }
    }

    public Task AnswerAsync(HostQuestionKey expected, bool yes, RequestOrigin channel, CancellationToken token)
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (answering) { throw new InvalidOperationException("The exact Details answer is still being recorded."); }
            answering = true;
            answerJoined = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return RecordAnswerAsync(expected, yes, channel, completion, token);
    }

    private async Task RecordAnswerAsync(HostQuestionKey expected, bool yes, RequestOrigin channel,
        TaskCompletionSource completion, CancellationToken token)
    {
        try { await AnswerCoreAsync(expected, yes, channel, token).ConfigureAwait(false); }
        finally { lock (gate) { answering = false; completion.TrySetResult(); } }
    }

    private async Task AnswerCoreAsync(HostQuestionKey expected, bool yes, RequestOrigin channel, CancellationToken token)
    {
        var offered = pending;
        if (offered is null || offered.Question.Key != expected || !offered.Admitted())
        { throw new InvalidOperationException("The exact foreground Details question is no longer answerable."); }
        System.Diagnostics.ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var host = HostActivity.BeginRoot(expected.Request, HostActivityLayer.Application, HostOperation.Presentation, causes);
        var value = new QuestionAnswer([yes ? "yes" : "not-now"]);
        var decision = channel switch
        {
            RequestOrigin.LocalUi => await questions.SubmitNativeReplyAsync(expected, offered.Question.SessionGeneration, value, offered.Admitted, token).ConfigureAwait(false),
            RequestOrigin.ActivatedVoice => await questions.SubmitReplyAsync(expected, offered.Question.SessionGeneration, value, offered.Admitted, token).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Details answers require original native/typed or activated input."),
        };
        if (decision is not { Outcome: HostInteractionOutcome.Answered, Question: { } accepted })
        { throw new InvalidOperationException("The exact Details answer was not accepted; inspect the original question."); }
        lock (gate)
        {
            if (!ReferenceEquals(pending, offered))
            { throw new InvalidOperationException("The accepted answer's presentation was withdrawn; inspect the recorded question."); }
            offered.Question = accepted;
            offered.Request.LinkAnswer(host.Activity!.Context);
            offered.Answer.TrySetResult(accepted);
        }
        host.Complete(HostOperationOutcome.Completed);
    }

    public bool Revalidate()
    {
        var live = sourceCurrent?.Invoke() == true;
        if (!live) { Withdraw(); }
        return live;
    }

    public async Task<AdmittedDetailContent> ReadDetailAsync(DetailsOfferRequest request,
        Kora.Application.Hosting.SessionWorkspaceService workspace, Func<bool> admitted, CancellationToken token)
    {
        var control = request.PresentationRequest;
        if (control is null || HostActivity.RequireCurrent().Request != control)
        { throw new InvalidOperationException("The exact host presentation control must own this read; no user channel is relabelled."); }
        if (!request.IsRevealConfirmed)
        {
            var offered = pending;
            if (offered is null || !offered.Admitted() || offered.Question.Key.Request != control)
            { throw new InvalidOperationException("The live exact offer source is unavailable."); }
            var decision = await new InteractionTransaction(store).RunAsync(control, "details.offer.read", snapshot =>
            {
                var canonical = snapshot.Questions.SingleOrDefault(question => question.Key == offered.Question.Key)
                    ?? throw new InvalidDataException("The exact Details answer is missing.");
                var valid = admitted() && request.IsCurrent() && InteractionTransaction.CanInteract(snapshot)
                    && canonical.Status == QuestionStatus.Answered && canonical.Draft!.Choices.SequenceEqual(["yes"])
                    && canonical.DetailsOffer == offered.Question.DetailsOffer;
                return (snapshot, new(valid ? HostInteractionOutcome.Presented : HostInteractionOutcome.Denied,
                    valid ? "exact-details-read-admitted" : "exact-details-read-denied", canonical));
            }, token).ConfigureAwait(false);
            if (decision.Outcome != HostInteractionOutcome.Presented)
            { throw new InvalidOperationException("Only a current accepted Yes may reveal this exact source."); }
        }
        return await workspace.ReadOfferedDetailsAsync(request, admitted, token).ConfigureAwait(false);
    }

    public void Withdraw() { lock (gate) { lifetime?.Cancel(); } }

    public async ValueTask DisposeAsync()
    {
        Task quiescence;
        lock (gate)
        {
            disposed = true;
            lifetime?.Cancel();
            quiescence = Task.WhenAll(joined?.Task ?? Task.CompletedTask, answerJoined?.Task ?? Task.CompletedTask);
        }
#pragma warning disable VSTHRD003 // Joins only this owner's exact already-started presentation operation.
        await quiescence.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }
}