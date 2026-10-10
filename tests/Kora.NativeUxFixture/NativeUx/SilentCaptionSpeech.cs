using Kora.Core.Voice;

namespace Kora.NativeUxFixture;

// This fixture models playback identity only. It has no native audio or synthesis implementation.
internal sealed class SilentCaptionSpeech : ITextToSpeechService
{
    private static readonly SpeechVoice Voice = new("fixture.silent", "Synthetic silent caption voice", "en-US", SpeechVoiceGender.Female);
    private static readonly AudioOutputDevice Output = new("fixture.silent", "Synthetic identity only - no audio endpoint");
    private TaskCompletionSource? completion;
    private TaskCompletionSource? started;
    private bool disposed;
    private long generation;

    internal Guid? PlaybackId { get; private set; }
    internal string? ExactText { get; private set; }
    internal int AdmittedRequests { get; private set; }
    public bool IsSpeaking => PlaybackId is not null && completion is { Task.IsCompleted: false };
    public SpeechPlaybackFrame PlaybackFrame { get; private set; } = SpeechPlaybackFrame.Inactive;

    internal Task Arm()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completion is { Task.IsCompleted: false }) { throw new InvalidOperationException("A synthetic response is already pending."); }
        PlaybackFrame = SpeechPlaybackFrame.Inactive;
        PlaybackId = null;
        ExactText = null;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return started.Task;
    }

    public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, Guid playbackId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (completion is not { Task.IsCompleted: false } || started is null || started.Task.IsCompleted
            || playbackId == Guid.Empty || voice != Voice || outputDevice != Output || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Only one explicitly armed synthetic caption response is admitted.");
        }
        PlaybackId = playbackId;
        ExactText = text;
        generation++;
        AdmittedRequests++;
        started.SetResult();
        return completion.Task.WaitAsync(cancellationToken);
    }

    internal void ObservePlayback()
    {
        if (!IsSpeaking || PlaybackId is null) { throw new InvalidOperationException("Synthetic playback has not been admitted."); }
        PlaybackFrame = new(true, 0.5, PlaybackId, generation);
    }

    internal void Complete()
    {
        if (!IsSpeaking || PlaybackId is null) { throw new InvalidOperationException("No synthetic playback can complete."); }
        PlaybackFrame = SpeechPlaybackFrame.Inactive;
        completion!.SetResult();
    }

    public void InvalidateOutput()
    {
        PlaybackFrame = SpeechPlaybackFrame.Inactive;
        // Arming is not output. The ordinary response path first retires its previous source.
        if (PlaybackId is not null) { completion?.TrySetCanceled(); }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InvalidateOutput();
        return Task.CompletedTask;
    }

    public IReadOnlyList<SpeechProvider> GetProviders() =>
        [new(SpeechProviderIds.Windows, "Synthetic silent Windows-provider contract", "No installed engine or endpoint is used.", true, true, null, Voice.Id)];
    public IReadOnlyList<SpeechVoice> GetVoices() => [Voice];
    public SpeechVoice GetDefaultVoice() => Voice;
    public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => [Output];
    public AudioOutputDevice GetDefaultOutputDevice() => Output;
    public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Identity-free or unarmed fixture speech is prohibited.");
    public Task InstallProviderAsync(string providerId, IProgress<SpeechProviderInstallProgress> progress, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The silent caption fixture cannot install providers.");
    public Task RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The silent caption fixture cannot remove providers.");
    public ValueTask DisposeAsync()
    {
        if (!disposed) { InvalidateOutput(); completion?.TrySetCanceled(); disposed = true; }
        return ValueTask.CompletedTask;
    }
}
