using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

public sealed partial class LocalContextTurnWorkflow
{
    private sealed record ContextSubmission(
        ModelContextEnvelope? Context, LocalContextTurnReason? Rejection, CancellationToken Token);

    private sealed class ContextRejectedException(LocalContextTurnReason reason) : InvalidOperationException
    {
        internal LocalContextTurnReason Reason { get; } = reason;
    }

    private sealed class AdmittedContextTurn(HostRequest request, HostRequest sourceRequest) : IAdmittedModelContextTurn
    {
        private readonly Lock gate = new();
        private readonly CancellationTokenSource lifetime = new();
        private readonly TaskCompletionSource<ModelContextAdmission> admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<ContextSubmission>? submission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<LocalContextTurnReceipt>? completion;
        private Task? disposal;
        private bool used;
        private bool retired;

        public HostRequest Request => request;
        public HostRevision SessionGeneration { get; private set; }
        // The issuer binds completion before publishing either the admission or its rejection.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
            Justification = "Returns this lease's owning lifecycle task, bound before publication; no synchronous or UI-thread join.")]
        public Task<LocalContextTurnReceipt> Completion => completion!;
        internal CancellationToken Revocation => lifetime.Token;

        internal void BindCompletion(Task<LocalContextTurnReceipt> pending) => completion = pending;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
            Justification = "This lease owns the asynchronously continued admission handshake; no UI-thread join or external task.")]
        internal Task<ModelContextAdmission> ReadAdmissionAsync() => admission.Task;
        internal void RejectIfNotAdmitted(LocalContextTurnReceipt rejection)
        {
            if (!admission.Task.IsCompleted) { admission.TrySetResult(new(rejection)); }
        }

        internal async Task<ContextSubmission> ReceiveAsync(HostRevision generation, CancellationToken token)
        {
            TaskCompletionSource<ContextSubmission> pending;
            lock (gate)
            {
                SessionGeneration = generation;
                pending = submission!;
                admission.SetResult(new(this));
            }
            try { return await pending.Task.WaitAsync(token).ConfigureAwait(false); }
            finally
            {
                // The lease must not retain a completed TCS containing source bodies after the worker consumes it.
                lock (gate) { submission = null; }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
            Justification = "Returns the owning host lifecycle task after releasing its typed submission handshake; no UI affinity.")]
        public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            lock (gate)
            {
                if (used || retired)
                {
                    return Task.FromResult(new LocalContextTurnReceipt(request,
                        ModelTurnOutcome.Denied, LocalContextTurnReason.TargetAlreadyUsed));
                }
                used = true;
                var current = HostActivity.Current;
                var rejection = current is null || current.Activity!.IsStopped
                    || current.Outcome != HostOperationOutcome.Unknown || !ReferenceEquals(current.Request, sourceRequest)
                    ? LocalContextTurnReason.OriginalInputRequired : (LocalContextTurnReason?)null;
                submission!.SetResult(new(rejection is null ? context : null, rejection, token));
                return Completion;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (gate) { return new(disposal ??= CloseAsync()); }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
            Justification = "Disposal asynchronously waits for this lease's owned lifecycle and physical resource termination.")]
        private async Task CloseAsync()
        {
            Task cancelled;
            lock (gate) { cancelled = retired ? Task.CompletedTask : lifetime.CancelAsync(); }
            await cancelled.ConfigureAwait(false);
            await Completion.ConfigureAwait(false);
        }

        internal void Retire()
        {
            lock (gate)
            {
                retired = true;
                submission = null;
                lifetime.Dispose();
            }
        }
    }
}