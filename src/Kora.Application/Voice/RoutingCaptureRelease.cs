using Kora.Core.Voice;

namespace Kora.Application.Voice;

internal sealed class RoutingCaptureRelease(
    IQuestionVoiceRecognitionService capture,
    HandsFreeVoiceContinuation.CaptureRelease policy)
{
    private readonly Lock gate = new();
    private TaskCompletionSource<long>? completion;
    private TaskCompletionSource? mutationCompleted;
    private long hostAdvance;
    private int mutations;
    private bool captureActive;
    private bool mutationFailed;
    private bool retired;
    private readonly List<Task> hostStops = [];

    internal IQuestionVoiceRecognitionService Capture => capture;
    internal bool IsCaptureActive { get { lock (gate) { return captureActive; } } }
    internal bool HasFailedRelease { get { lock (gate) { return completion?.Task.IsFaulted == true; } } }

    internal void BeginCapture()
    {
        lock (gate)
        {
            if (retired || mutationFailed || completion is not null && !completion.Task.IsCompletedSuccessfully
                || hostStops.Any(task => !task.IsCompletedSuccessfully))
            { throw new InvalidOperationException("The previous routing capture release is unconfirmed."); }
            completion = null;
            captureActive = false;
            hostStops.Clear();
        }
    }

    internal void CaptureStarted() { lock (gate) { captureActive = true; } }

    internal void Invalidate(Action invalidate)
    {
        lock (gate)
        {
            var outermost = mutations++ == 0;
            var before = capture.CaptureGeneration;
            if (outermost)
            {
                mutationCompleted = NewSignal();
                if (completion?.Task.IsCompletedSuccessfully == true) { completion = null; }
            }
            try
            {
                invalidate();
                if (outermost)
                {
                    var advance = checked(capture.CaptureGeneration - before);
                    if (advance < 0) { throw new InvalidOperationException("Host capture invalidation moved its generation backwards."); }
                    hostAdvance = checked(hostAdvance + advance);
                }
            }
            catch
            {
                mutationFailed = true;
                throw;
            }
            finally
            {
                if (--mutations == 0) { mutationCompleted!.TrySetResult(); }
            }
        }
    }

    internal Task StopHostAsync(Func<Task> stop)
    {
        lock (gate)
        {
            var joinHost = !captureActive;
            Task? stopping = null;
            Invalidate(() =>
            {
                stopping = stop() ?? throw new InvalidOperationException("Host capture stop did not return its owned completion.");
                if (joinHost) { hostStops.Add(stopping); }
            });
            return stopping!;
        }
    }

#pragma warning disable VSTHRD003 // Callers join this owner's single physical-release receipt without blocking a context.
    internal Task<long> JoinAsync()
    {
        lock (gate)
        {
            if (completion is not null) { return completion.Task; }
            captureActive = true;
            var pending = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = CompleteJoinAsync(pending);
            return pending.Task;
        }
    }
#pragma warning restore VSTHRD003

    internal Task<long> JoinRetiredAsync()
    {
        lock (gate)
        {
            retired = true;
            if (completion is null || completion.Task.IsCompleted)
            {
                completion = null;
                mutationFailed = false;
                hostStops.RemoveAll(task => task.IsCompleted);
            }
            return JoinAsync();
        }
    }

    private async Task CompleteJoinAsync(TaskCompletionSource<long> pending)
    {
        try
        {
            Task stopping;
            long expected;
            long admittedAdvance;
            while (true)
            {
                Task mutation;
                lock (gate)
                {
                    if (mutations == 0)
                    {
                        admittedAdvance = hostAdvance;
                        expected = policy.Expected(capture.CaptureGeneration);
                        stopping = capture.StopAsync();
                        break;
                    }
                    mutation = mutationCompleted!.Task;
                }
#pragma warning disable VSTHRD003 // Join only this owner's synchronous host-invalidation receipt.
                await mutation.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            await stopping.ConfigureAwait(false);
            Task[] hosts;
            lock (gate) { hosts = hostStops.ToArray(); }
#pragma warning disable VSTHRD003 // These earlier native host stops are independent of the shared routing completion.
            await Task.WhenAll(hosts).ConfigureAwait(false);
#pragma warning restore VSTHRD003
            while (true)
            {
                Task mutation;
                lock (gate)
                {
                    if (mutations == 0)
                    {
                        expected = checked(expected + hostAdvance - admittedAdvance);
                        if (mutationFailed || !capture.IsCaptureQuiescent || capture.CaptureGeneration != expected)
                        { throw new InvalidOperationException("Routing successor release was not physically confirmed for its exact generation."); }
                        pending.TrySetResult(expected);
                        return;
                    }
                    mutation = mutationCompleted!.Task;
                }
#pragma warning disable VSTHRD003 // A reentrant host invalidation must finish publishing its exact generation change.
                await mutation.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
        }
        catch (Exception exception)
        {
            pending.TrySetException(exception);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
