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
    private CancellationTokenSource? cancellation;
    private SpeechVoice? requestedVoice;
    private long revision;
    private bool disposed;
    private string status = string.Empty;

    public event EventHandler? Changed;

    public string Status { get { lock (gate) { return status; } } }
    public bool IsQuiescent { get { lock (gate) { return pending.IsCompleted; } } }

    public void Request(SpeechVoice? voice, Func<bool>? remainsEligible = null)
    {
        if (voice is not null && !string.Equals(voice.ProviderId, SpeechProviderIds.Kokoro, StringComparison.Ordinal))
        {
            voice = null;
        }
        lock (gate)
        {
            if (disposed) { return; }
            if (requestedVoice == voice && (voice is not null || status.Length == 0)) { return; }
            cancellation?.Cancel();
            requestedVoice = voice;
            var currentRevision = ++revision;
            status = voice is null ? string.Empty : "Preparing selected speech voice...";
            if (voice is not null)
            {
                var source = new CancellationTokenSource();
                cancellation = source;
                var previous = pending;
                var selected = voice;
                var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Runtime);
#pragma warning disable VSTHRD003 // The worker joins the prior owned task without using the caller's synchronization context.
                pending = Task.Run(() => PrepareAsync(previous, selected, source, currentRevision, begin, remainsEligible));
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
            cancellation?.Cancel();
        }
    }

    private async Task PrepareAsync(Task previous, SpeechVoice voice, CancellationTokenSource source,
        long currentRevision, Func<HostActivity> begin, Func<bool>? remainsEligible)
    {
        using var activity = begin();
        var result = string.Empty;
        var failed = false;
        try
        {
#pragma warning disable VSTHRD003 // Join the previously owned native preparation before starting its replacement.
            await previous.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (remainsEligible?.Invoke() != true) { await source.CancelAsync().ConfigureAwait(false); }
            source.Token.ThrowIfCancellationRequested();
            await service.PrepareAsync(voice, source.Token).ConfigureAwait(false);
            if (remainsEligible?.Invoke() != true) { await source.CancelAsync().ConfigureAwait(false); }
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
            bool notify;
            lock (gate)
            {
                if (ReferenceEquals(cancellation, source)) { cancellation = null; }
                source.Dispose();
                notify = !disposed && currentRevision == revision;
                if (notify)
                {
                    status = result;
                    if (failed) { requestedVoice = null; }
                }
            }
            if (notify) { Changed?.Invoke(this, EventArgs.Empty); }
        }
    }
}
