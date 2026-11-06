using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed partial class ActivatedVoiceRecognitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_privacy_cleanup_cannot_reopen_resource_work_after_stop_confirms_release(bool deviceRemoved)
    {
        using var privacy = new FakePrivacy();
        var native = new FakeCapture { DisposeGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var factory = new FakeFactory();
        factory.OpenResult.SetResult(native);
        var logger = new CleanupCallbackLogger(Logger);
        await using var service = new WindowsVoiceRecognitionService(logger, privacy, factory, VoiceCaptureLimits.Default);
        await BeginCaptureAsync(service);
        var lateAudio = native.QueueAudio([1, 2, 3]);
        var lateTranscript = native.QueueTranscript("PRIVATE_LATE");
        var transcripts = new List<VoiceTranscriptEventArgs>();
        service.TranscriptRecognized += (_, args) => transcripts.Add(args);
        var cleanup = new DeferredCleanupContext();
        var originalContext = SynchronizationContext.Current;
        Task stopping = Task.CompletedTask;
        service.RecognitionFailed += (_, _) =>
        {
            // The host stop and detached privacy cleanup have independent continuations.
            SynchronizationContext.SetSynchronizationContext(originalContext);
            try { stopping = service.StopAsync(TestContext.Current.CancellationToken); }
            finally { SynchronizationContext.SetSynchronizationContext(cleanup); }
        };
        try
        {
            SynchronizationContext.SetSynchronizationContext(cleanup);
            try
            {
                privacy.Set(deviceRemoved
                    ? Ready with { ActiveMicrophoneIds = [] }
                    : Ready with { SessionState = WindowsSessionState.Locked });
            }
            finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
            await native.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            cleanup.Count.Should().Be(1);
            service.IsCaptureQuiescent.Should().BeFalse("recognizer disposal still owns native work");
            native.DisposeGate.SetResult();
#pragma warning disable VSTHRD003 // The failure callback owns the exact stop receipt.
            await stopping;
#pragma warning restore VSTHRD003
            service.IsCaptureQuiescent.Should().BeTrue();

            // A stale native callback holds the capture gate while the delayed cleanup runs,
            // preventing a redundant resource task from winning this assertion by scheduling.
            logger.OnLog = () =>
            {
                cleanup.RunNext();
                service.IsCaptureQuiescent.Should().BeTrue("resource-free cleanup must not revoke a completed stop receipt");
            };
            lateAudio();
            (await cleanup.WaitForCallbackAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            cleanup.RunNext();
            cleanup.Count.Should().Be(0);
            lateTranscript();
            transcripts.Should().BeEmpty();
            service.IsListening.Should().BeFalse();
            factory.OpenCount.Should().Be(1);
            factory.Stream!.BufferedBytes.Should().Be(0);
            native.Disposed.Task.IsCompletedSuccessfully.Should().BeTrue();
        }
        finally
        {
            logger.OnLog = null;
            native.DisposeGate.TrySetResult();
            while (cleanup.Count != 0) { cleanup.RunNext(); }
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class DeferredCleanupContext : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> callbacks =
            Channel.CreateUnbounded<(SendOrPostCallback Callback, object? State)>();
        public int Count => callbacks.Reader.Count;
        public override void Post(SendOrPostCallback d, object? state) =>
            callbacks.Writer.TryWrite((d, state)).Should().BeTrue();
        public ValueTask<bool> WaitForCallbackAsync(CancellationToken token) => callbacks.Reader.WaitToReadAsync(token);
        public void RunNext()
        {
            callbacks.Reader.TryRead(out var next).Should().BeTrue();
            next.Callback(next.State);
        }
    }

    private sealed class CleanupCallbackLogger(ILogger<WindowsVoiceRecognitionService> inner)
        : ILogger<WindowsVoiceRecognitionService>
    {
        public Action? OnLog { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
            var callback = OnLog;
            OnLog = null;
            callback?.Invoke();
        }
    }
}
