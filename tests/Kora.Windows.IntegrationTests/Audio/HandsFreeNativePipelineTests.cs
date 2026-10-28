using System.Runtime.InteropServices;
using AwesomeAssertions;
using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class HandsFreeNativePipelineTests
{
    [Theory]
    [InlineData(0, 0, 160L)]
    [InlineData(1, 1, 320L)]
    [InlineData(3, 10, 1760L)]
    [InlineData(int.MaxValue, int.MaxValue, 343597383680L)]
    public void Inclusive_last_native_frame_maps_to_exclusive_original_PCM_end(int first, int last, long expected) =>
        new KeywordAudioSegment(first, last).CommandStartSample.Should().Be(expected);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void Invalid_native_spans_are_not_padded_or_reinterpreted(int first, int last)
    {
        var read = () => new KeywordAudioSegment(first, last).CommandStartSample;
        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Original_chunk_origin_excludes_preamble_and_whole_name_but_preserves_contiguous_command()
    {
        var fixture = new Fixture();
        await using var session = fixture.Session;
        session.Start();
        fixture.Microphone.Emit(Enumerable.Repeat((short)1, 160)
            .Concat(Enumerable.Repeat((short)2, 160)).ToArray());
        fixture.OpenCount.Should().Be(0);
        fixture.Detector.Next = new(1, 1);
        fixture.Microphone.Emit(Enumerable.Repeat((short)3, 320).ToArray());
        var wake = await session.WaitForWakeAsync(TestContext.Current.CancellationToken);
        wake.CommandStartSample.Should().Be(320);
        wake.Generation.Should().Be(-21);
        fixture.OpenCount.Should().Be(0);
        var capture = session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        await fixture.Capture.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        (await ReadSamplesAsync(fixture.Stream!, 320)).Should().AllBeEquivalentTo((short)3);
        fixture.Microphone.Emit(Enumerable.Repeat((short)4, 128).ToArray());
        (await ReadSamplesAsync(fixture.Stream!, 128)).Should().AllBeEquivalentTo((short)4);
        fixture.Detector.Samples.Should().Be(640, "post-wake command audio is not processed as another ambient utterance");
        fixture.Capture.Transcript("session help", 0.9f);
        fixture.Capture.Transcript("a second final", 1);
        fixture.Capture.Done.SetResult();
        var result = await capture;
        result.Should().NotBeNull();
        result!.Transcript.Should().Be("session help");
        result.Generation.Should().Be(wake.Generation);
        fixture.OpenCount.Should().Be(1);
        await session.DisposeAsync();
        session.IsQuiescent.Should().BeTrue();
        fixture.Microphone.Disposed.Should().BeTrue();
        fixture.Detector.Disposed.Should().BeTrue();
        fixture.Endpoint.Disposed.Should().BeTrue();
        fixture.Capture.Disposed.Should().BeTrue();
        fixture.Assets.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 1, 640)]
    [InlineData(2, 1, 640)]
    [InlineData(1, 10, 640)]
    [InlineData(0, 0, 40000)]
    public async Task Invalid_future_or_expired_span_never_opens_general_transcription(int first, int last, int count)
    {
        var fixture = new Fixture();
        await using var session = fixture.Session;
        session.Start();
        fixture.Detector.Next = new(first, last);
        fixture.Microphone.Emit(new short[count]);
        var wake = () => session.WaitForWakeAsync(TestContext.Current.CancellationToken);
        await wake.Should().ThrowAsync<InvalidDataException>();
        fixture.OpenCount.Should().Be(0);
        await session.DisposeAsync();
        session.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Invalidation_rejects_late_native_span_and_joins_ignoring_decoder_before_quiescence()
    {
        var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        fixture.Detector.Hold = release;
        fixture.Detector.Next = new(0, 0);
        await using var session = fixture.Session;
        session.Start();
        var callback = Task.Run(() => fixture.Microphone.Emit(new short[320]), TestContext.Current.CancellationToken);
        await fixture.Detector.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        session.Invalidate();
        var close = session.DisposeAsync().AsTask();
        close.IsCompleted.Should().BeFalse();
        session.IsQuiescent.Should().BeFalse();
        fixture.Detector.Disposed.Should().BeFalse();
        fixture.OpenCount.Should().Be(0);
        release.Set();
        await callback;
        await close;
        session.IsQuiescent.Should().BeTrue();
        var wake = () => session.WaitForWakeAsync(TestContext.Current.CancellationToken);
        await wake.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Ignoring_command_open_stays_owned_and_cannot_start_after_authority_loss()
    {
        var fixture = new Fixture { HoldOpen = true };
        await using var session = fixture.Session;
        var wake = await fixture.WakeAsync();
        var capture = session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        await fixture.OpenEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Admitted = false;
        session.Invalidate();
        var close = session.DisposeAsync().AsTask();
        close.IsCompleted.Should().BeFalse();
        session.IsQuiescent.Should().BeFalse();
        fixture.ReleaseOpen.SetResult(fixture.Capture);
        await close;
        await ((Func<Task>)(async () => await capture)).Should().ThrowAsync<OperationCanceledException>();
        fixture.Capture.Started.Task.IsCompleted.Should().BeFalse();
        fixture.Capture.Disposed.Should().BeTrue();
        session.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Ignoring_recognizer_release_keeps_ownership_and_rejects_late_final()
    {
        var fixture = new Fixture();
        fixture.Capture.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = fixture.Session;
        var wake = await fixture.WakeAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var capture = session.CaptureCommandAsync(wake, cancellation.Token);
        var queued = fixture.Capture.QueueTranscript("not authorized");
        cancellation.Cancel();
        var close = session.DisposeAsync().AsTask();
        await fixture.Capture.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        queued();
        fixture.Stream!.BufferedBytes.Should().Be(0);
        close.IsCompleted.Should().BeFalse();
        session.IsQuiescent.Should().BeFalse();
        await ((Func<Task>)(async () => await capture)).Should().ThrowAsync<OperationCanceledException>();
        fixture.Capture.Release.SetResult();
        await close;
        session.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1f, "session help")]
    [InlineData(float.NaN, "session help")]
    [InlineData(1.1f, "session help")]
    [InlineData(0.61f, "session help")]
    [InlineData(1f, " ")]
    public async Task Only_a_bounded_confident_final_can_leave_the_native_seam(float confidence, string text)
    {
        var fixture = new Fixture();
        await using var session = fixture.Session;
        var wake = await fixture.WakeAsync();
        var capture = session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        fixture.Capture.Transcript(text, confidence);
        fixture.Capture.Done.SetResult();
        (await capture).Should().BeNull();
    }

    [Fact]
    public async Task Live_playback_or_other_admission_loss_is_checked_after_ignoring_native_work()
    {
        var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        fixture.Detector.Hold = release;
        fixture.Detector.Next = new(0, 0);
        await using var session = fixture.Session;
        session.Start();
        var callback = Task.Run(() => fixture.Microphone.Emit(new short[320]), TestContext.Current.CancellationToken);
        await fixture.Detector.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Admitted = false;
        release.Set();
        await callback;
        var wake = () => session.WaitForWakeAsync(TestContext.Current.CancellationToken);
        await wake.Should().ThrowAsync<OperationCanceledException>();
        fixture.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Wake_identity_is_exact_and_command_interval_is_single_use()
    {
        var fixture = new Fixture();
        await using var session = fixture.Session;
        var wake = await fixture.WakeAsync();
        var stale = () => session.CaptureCommandAsync(wake with { Generation = -22 }, TestContext.Current.CancellationToken);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        fixture.OpenCount.Should().Be(0);
        var capture = session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        var replay = () => session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        await replay.Should().ThrowAsync<InvalidOperationException>();
        fixture.Capture.Done.SetResult();
        await capture;
        fixture.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task Post_name_endpoint_closes_bounded_PCM_without_including_later_ambient_audio()
    {
        var fixture = new Fixture();
        fixture.Endpoint.End = true;
        await using var session = fixture.Session;
        var wake = await fixture.WakeAsync();
        var capture = session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        await fixture.Capture.StopRequested.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Microphone.Emit(new short[512]);
        fixture.Endpoint.Samples.Should().Be(160);
        (await ReadSamplesAsync(fixture.Stream!, 160)).Should().HaveCount(160);
        var end = new byte[2];
        (await fixture.Stream!.ReadAsync(end, TestContext.Current.CancellationToken)).Should().Be(0);
        fixture.Capture.Done.SetResult();
        (await capture).Should().BeNull();
    }

    [Fact]
    public async Task Bounded_PCM_overflow_is_failure_not_a_silently_truncated_command()
    {
        var fixture = new Fixture();
        await using var session = fixture.Session;
        var wake = await fixture.WakeAsync();
        var capture = session.CaptureCommandAsync(wake, TestContext.Current.CancellationToken);
        fixture.Microphone.Emit(new short[32000]);
        await ((Func<Task>)(async () => await capture)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Stream!.BufferedBytes.Should().Be(0);
    }

    [Fact]
    public async Task Native_microphone_failure_is_explicit_and_never_opens_ASR()
    {
        var fixture = new Fixture();
        await using var session = fixture.Session;
        session.Start();
        fixture.Microphone.Fail();
        var wake = () => session.WaitForWakeAsync(TestContext.Current.CancellationToken);
        await wake.Should().ThrowAsync<InvalidOperationException>();
        fixture.OpenCount.Should().Be(0);
    }

    private static async Task<short[]> ReadSamplesAsync(BlockingAudioStream stream, int count)
    {
        var bytes = new byte[count * 2];
        await stream.ReadExactlyAsync(bytes, TestContext.Current.CancellationToken);
        return MemoryMarshal.Cast<byte, short>(bytes).ToArray();
    }

    private sealed class Fixture
    {
        public FakeMicrophone Microphone { get; } = new();
        public FakeDetector Detector { get; } = new();
        public FakeEndpoint Endpoint { get; } = new();
        public FakeAssets Assets { get; } = new();
        public FakeCapture Capture { get; } = new();
        public TaskCompletionSource OpenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IActivatedCapture> ReleaseOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Admitted { get; set; } = true;
        public bool HoldOpen { get; init; }
        public int OpenCount { get; private set; }
        public BlockingAudioStream? Stream { get; private set; }
        public WindowsHandsFreeAudioSession Session { get; }
        public Fixture()
        {
            Session = new(new(-21, new("synthetic", "Synthetic"), "Nova", new(1, SpeechInputSelection.Default))
            {
                CanCapture = () => Admitted,
            }, Microphone, Detector, Endpoint, Assets, stream =>
            {
                OpenCount++;
                Stream = stream;
                OpenEntered.TrySetResult();
                return HoldOpen ? ReleaseOpen.Task : Task.FromResult<IActivatedCapture>(Capture);
            });
        }
        public async Task<WakeWordObservation> WakeAsync()
        {
            Session.Start();
            Detector.Next = new(0, 0);
            Microphone.Emit(new short[320]);
            return await Session.WaitForWakeAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeMicrophone : IHandsFreeMicrophone
    {
        public event HandsFreeSamplesAvailable? Samples;
        public event EventHandler? Failed;
        public bool Disposed { get; private set; }
        public void Start() { }
        public void Emit(short[] samples) => Samples?.Invoke(samples);
        public void Fail() => Failed?.Invoke(this, EventArgs.Empty);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeDetector : IHandsFreeKeywordDetector
    {
        public KeywordAudioSegment? Next { get; set; }
        public int Samples { get; private set; }
        public bool Disposed { get; private set; }
        public ManualResetEventSlim? Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public KeywordAudioSegment? Accept(ReadOnlySpan<short> audio)
        {
            Samples += audio.Length;
            Entered.TrySetResult();
            Hold?.Wait(TestContext.Current.CancellationToken);
            return Next;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeEndpoint : IHandsFreeCommandEndpoint
    {
        public bool Disposed { get; private set; }
        public bool End { get; set; }
        public int Samples { get; private set; }
        public bool Accept(ReadOnlySpan<short> audio) { Samples += audio.Length; return End; }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeAssets : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeCapture : IActivatedCapture
    {
        public event ActivatedAudioAvailable? DataAvailable { add { } remove { } }
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler? SpeechDetected { add { } remove { } }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; set; }
        public bool Disposed { get; private set; }
        public Task Completion => Done.Task;
        public long? RecorderReleasedTimestamp => null;
        public void Start() => Started.TrySetResult();
        public Task ReleaseRecorder() => Task.CompletedTask;
        public void FinishRecognition(bool cancel) { StopRequested.TrySetResult(); if (cancel) { Done.TrySetResult(); } }
        public void Transcript(string text, float confidence) => TranscriptRecognized?.Invoke(this, new(text, confidence));
        public Action QueueTranscript(string text)
        {
            var handler = TranscriptRecognized;
            return () => handler?.Invoke(this, new(text, 1));
        }
        public async ValueTask DisposeAsync()
        {
            Closing.TrySetResult();
            if (Release is not null) { await Release.Task.WaitAsync(TestContext.Current.CancellationToken); }
            Disposed = true;
        }
    }
}
