using Kora.Core.Diagnostics;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class SpeechPreparationCoordinator(
    ISpeechPreparationService service,
    ILogger<SpeechPreparationCoordinator> logger) : IDisposable
{
    private readonly Lock gate = new();
    private Task pending = Task.CompletedTask;
    private Preparation? cancellation;
    private SpeechVoice? requestedVoice;
    private long revision;
    private bool disposed;
    private string status = string.Empty;

    public event EventHandler? Changed;

    public string Status { get { lock (gate) { return status; } } }
    public bool IsQuiescent { get { lock (gate) { return pending.IsCompleted; } } }

    public void Request(SpeechVoice? voice, Func<bool>? remainsEligible = null)
    {
        var eligibility = remainsEligible ?? (static () => false);
        if (voice is not null && !string.Equals(voice.ProviderId, SpeechProviderIds.Kokoro, StringComparison.Ordinal))
        {
            voice = null;
        }
        lock (gate)
        {
            if (disposed) { return; }
            if (requestedVoice == voice && (voice is not null || status.Length == 0)) { return; }
            requestedVoice = voice;
            var currentRevision = ++revision;
            if (cancellation is not null) { CancelPreparation(cancellation); }
            status = voice is null ? string.Empty : "Preparing selected speech voice...";
            if (voice is not null)
            {
                var preparation = new Preparation();
                cancellation = preparation;
                var previous = pending;
                var selected = voice;
                var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Runtime);
#pragma warning disable VSTHRD003 // The worker joins the prior owned task without using the caller's synchronization context.
                pending = Task.Run(() => PrepareAsync(previous, selected, preparation, currentRevision, begin, eligibility));
#pragma warning restore VSTHRD003
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task WaitForQuiescenceAsync()
    {
#pragma warning disable VSTHRD003 // Exposes the owned preparation task so shutdown can join native work.
        lock (gate) { return pending; }
#pragma warning restore VSTHRD003
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            revision++;
            requestedVoice = null;
            status = string.Empty;
            if (cancellation is not null) { CancelPreparation(cancellation); }
        }
    }

    private void CancelPreparation(Preparation preparation)
    {
        lock (gate)
        {
            preparation.RetirementRevision = revision;
            if (!preparation.Source.IsCancellationRequested)
            {
                preparation.Callbacks = preparation.Source.CancelAsync();
            }
        }
    }

    private async Task PrepareAsync(Task previous, SpeechVoice voice, Preparation preparation,
        long currentRevision, Func<HostActivity> begin, Func<bool> remainsEligible)
    {
        using var activity = begin();
        var source = preparation.Source;
        var result = string.Empty;
        var failed = false;
        try
        {
#pragma warning disable VSTHRD003 // Join the previously owned native preparation before starting its replacement.
            await previous.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (!remainsEligible()) { CancelPreparation(preparation); }
            source.Token.ThrowIfCancellationRequested();
            await service.PrepareAsync(voice, source.Token).ConfigureAwait(false);
            if (!remainsEligible()) { CancelPreparation(preparation); }
            source.Token.ThrowIfCancellationRequested();
            result = "Selected speech voice is prepared.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            failed = true;
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            PreparationFailed(exception);
            result = "Speech preparation failed. Preview can retry; refresh readiness to retry preparation.";
            failed = true;
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            Task callbacks;
            lock (gate)
            {
                if (ReferenceEquals(cancellation, preparation)) { cancellation = null; }
                callbacks = preparation.Callbacks;
            }
            var cancellationFailed = false;
            try
            {
#pragma warning disable VSTHRD003 // Joins this preparation's owned cancellation callbacks before disposal, replacement or shutdown quiescence.
                await callbacks.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                PreparationCancellationFailed(exception);
                result = "Speech preparation cancellation failed. Native work and cancellation callbacks have finished; refresh readiness to retry.";
                failed = cancellationFailed = true;
                activity.Complete(HostOperationOutcome.Failed);
            }
            preparation.Dispose();
            bool notify;
            lock (gate)
            {
                notify = !disposed && (currentRevision == revision
                    || cancellationFailed && preparation.RetirementRevision == revision && requestedVoice is null);
                if (notify)
                {
                    status = result;
                    if (failed) { requestedVoice = null; }
                }
            }
            if (notify) { Changed?.Invoke(this, EventArgs.Empty); }
        }
    }

    private sealed class Preparation : IDisposable
    {
        internal CancellationTokenSource Source { get; } = new();
        internal Task Callbacks { get; set; } = Task.CompletedTask;
        internal long RetirementRevision { get; set; }
        public void Dispose() => Source.Dispose();
    }
}
