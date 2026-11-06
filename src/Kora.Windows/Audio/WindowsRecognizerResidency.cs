using Kora.Core.Voice;

namespace Kora.Windows.Audio;

internal sealed class WindowsRecognizerResidency(IWindowsActivatedNativeFactory native)
{
    private readonly Lock gate = new();
    private IWindowsActivatedRecognizer? engine;
    private Key? key;
    private TaskCompletionSource? returned;
    private Task? retirement;
    private bool retiring;
    private Exception? failure;

    public bool IsAvailable { get { lock (gate) { return failure is null && !retiring; } } }
    public bool IsQuiescent { get { lock (gate) { return returned is null && failure is null && !retiring; } } }

    public async Task<Lease> AcquireAsync(SpeechInputGeneration profile, IReadOnlyList<string> phrases, TimeSpan speechStartWindow,
        TimeSpan trailingSilence, Func<bool> canOpen)
    {
        var requested = new Key(profile, phrases.ToArray(), speechStartWindow, trailingSilence);
        lock (gate)
        {
            if (failure is not null || retiring || returned is not null)
            {
                throw new InvalidOperationException("The Windows recognizer is owned, retiring or quarantined. No fallback is permitted.", failure);
            }
            returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var cancelledBeforeCreation = false;
        try
        {
            if (engine is not null && !requested.Matches(key))
            {
                await engine.DisposeAsync().ConfigureAwait(false);
                engine = null;
            }
            if (!canOpen() || !IsAvailable)
            {
                cancelledBeforeCreation = true;
                await ReturnAsync(SpeechInputResidency.CaptureLifetime).ConfigureAwait(false);
                throw new OperationCanceledException("Model acquisition was retired before replacement.");
            }
            engine ??= native.CreateRecognizer(requested.Phrases, speechStartWindow, trailingSilence);
            key = requested;
            return new(engine, this, profile.Selection.Residency);
        }
        catch (OperationCanceledException) when (cancelledBeforeCreation) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Quarantine(exception);
            throw;
        }
    }

    public Task RetireAsync()
    {
        lock (gate)
        {
            if (failure is not null) { return Task.FromException(new InvalidOperationException("Windows model release is unconfirmed; input remains quarantined.", failure)); }
#pragma warning disable VSTHRD003 // Concurrent retirees join the same owned native operation.
            if (retiring) { return retirement!; }
            retiring = true;
            var pending = returned?.Task ?? Task.CompletedTask;
            retirement = Task.Run(() => RetireCoreAsync(pending), CancellationToken.None);
            return retirement;
#pragma warning restore VSTHRD003
        }
    }

    private async Task RetireCoreAsync(Task pending)
    {
        try
        {
#pragma warning disable VSTHRD003 // The lease owns native completion, not a dispatcher-bound operation.
            await pending.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (engine is not null) { await engine.DisposeAsync().ConfigureAwait(false); }
            lock (gate)
            {
                engine = null;
                key = null;
                retiring = false;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Quarantine(exception);
            throw;
        }
    }

    private async ValueTask ReturnAsync(SpeechInputResidency residency)
    {
        try
        {
            bool keep;
            lock (gate) { keep = residency == SpeechInputResidency.KeepLoaded && !retiring && failure is null; }
            if (!keep)
            {
                if (engine is not null) { await engine.DisposeAsync().ConfigureAwait(false); }
                engine = null;
                key = null;
            }
            lock (gate)
            {
                var receipt = returned!;
                returned = null;
                receipt.TrySetResult();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Quarantine(exception);
            throw;
        }
    }

    private void Quarantine(Exception exception)
    {
        lock (gate)
        {
            failure ??= exception;
            returned?.TrySetException(exception);
        }
    }

    private sealed record Key(SpeechInputGeneration Profile, string[] Phrases, TimeSpan SpeechStartWindow, TimeSpan TrailingSilence)
    {
        public bool Matches(Key? other) => other is not null && Profile == other.Profile
            && SpeechStartWindow == other.SpeechStartWindow && TrailingSilence == other.TrailingSilence
            && Phrases.SequenceEqual(other.Phrases, StringComparer.Ordinal);
    }

    internal sealed class Lease(IWindowsActivatedRecognizer recognizer, WindowsRecognizerResidency? owner,
        SpeechInputResidency residency = SpeechInputResidency.CaptureLifetime)
    {
        public IWindowsActivatedRecognizer Recognizer { get; } = recognizer;
        public void Quarantine(Exception exception) => owner?.Quarantine(exception);
        public ValueTask ReturnAsync() => owner is null ? Recognizer.DisposeAsync() : owner.ReturnAsync(residency);
        public ValueTask DiscardAsync() => owner is null ? Recognizer.DisposeAsync() : owner.ReturnAsync(SpeechInputResidency.CaptureLifetime);
    }
}
