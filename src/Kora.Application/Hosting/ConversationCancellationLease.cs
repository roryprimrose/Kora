using System.Diagnostics;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

internal sealed class ConversationCancellationLease(
    ConversationCancellationTarget target, Func<bool> admitted,
    Action<ConversationExecutionStatus> publish, ActivityLink link, CancellationToken caller) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller);
    private ConversationExecutionPhase phase = ConversationExecutionPhase.Waiting;
    private ConversationCancellationTarget currentTarget = target;
    private ModelTurnOutcome? outcome;
    private HostTaskState? taskState;
    private ConversationExecutionProgress progress = new(ConversationExecutionStep.GeneratingAnswer, 0, 0);
    private bool accepted;
    private Task signal = Task.CompletedTask;
    private readonly TaskCompletionSource<ConversationExecutionStatus> physicalCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal ConversationCancellationTarget Target { get { lock (gate) { return currentTarget; } } }
    internal ActivityLink Link => link;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Observes this host-owned lease's asynchronously signalled physical completion, never UI-affine work.")]
    internal Task<ConversationExecutionStatus> JoinPhysicalCompletionAsync() => physicalCompletion.Task;
    internal void ReleasePhysicalCompletion(bool confirmed)
    {
        physicalCompletion.TrySetResult(confirmed ? Status : new(Target, ConversationExecutionPhase.Unconfirmed));
    }
    internal async Task WaitForPhysicalResourcesAsync(Func<Task> join)
    {
        try { await join().ConfigureAwait(false); }
        catch (Exception exception)
        {
            physicalCompletion.TrySetException(exception);
            throw;
        }
    }
    internal CancellationToken Token => cancellation.Token;
    internal bool CancellationAccepted { get { lock (gate) { return accepted; } } }
    internal bool CanContinue { get { lock (gate) { return !accepted && admitted(); } } }
    internal ConversationExecutionStatus Status
    {
        get
        {
            lock (gate)
            {
                return new(currentTarget, phase, outcome, taskState,
                    phase == ConversationExecutionPhase.Running ? progress : null);
            }
        }
    }

    internal void PresentExecutionStep(ConversationExecutionStep step)
    {
        ConversationExecutionStatus status;
        lock (gate)
        {
            if (phase != ConversationExecutionPhase.Running || !CanContinue || cancellation.IsCancellationRequested)
            { return; }
            progress = new(step, progress.ObservationCount + (step == ConversationExecutionStep.ReadingObservation ? 1 : 0),
                progress.Revision + 1);
            status = Status;
        }
        publish(status);
    }

    internal void Dispatched(HostRevision revision)
    {
        lock (gate)
        {
            currentTarget = currentTarget with { TaskRevision = revision };
            if (!accepted) { phase = ConversationExecutionPhase.Running; }
        }
    }

    internal Task RequestCancellationAsync(Action recordAcceptance)
    {
        lock (gate)
        {
            if (accepted) { return Task.CompletedTask; }
            if (phase is not (ConversationExecutionPhase.Running or ConversationExecutionPhase.Waiting) || !admitted())
            { throw new InvalidOperationException("The exact turn no longer accepts cancellation; terminal storage may already own the outcome."); }
            // Required audit precedes the authority change. A failed audit cannot claim acceptance.
            recordAcceptance();
            accepted = true;
            phase = ConversationExecutionPhase.Cancelling;
            signal = cancellation.CancelAsync();
        }
        publish(Status);
        return signal;
    }

    internal bool BeginCompletion()
    {
        bool cancelled;
        lock (gate)
        {
            cancelled = accepted;
            if (!cancelled) { phase = ConversationExecutionPhase.Completing; }
        }
        publish(Status);
        return cancelled;
    }

    internal void Complete(ModelTurnOutcome terminal, HostTaskState terminalTaskState)
    {
        lock (gate)
        {
            phase = ConversationExecutionPhase.Terminal;
            outcome = terminal;
            taskState = terminalTaskState;
        }
        publish(Status);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Joins this lease's CancelAsync callbacks off the UI context before disposing their token source.")]
    internal Task JoinCancellationAsync() => signal;

    public async ValueTask DisposeAsync()
    {
        try { await JoinCancellationAsync().ConfigureAwait(false); }
        finally { cancellation.Dispose(); }
    }
}