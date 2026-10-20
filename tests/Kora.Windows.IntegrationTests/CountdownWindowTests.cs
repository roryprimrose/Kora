using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application;
using Kora.Application.Interaction;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CountdownWindowTests
{
    [WindowsFact]
    public async Task NativeTimerIsSingletonAndExpiryOnlyUpdatesVisualText()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var time = new Clock();
            using var state = new CountdownWorkflow(time, new ImmediateDispatcher(), NullLogger<CountdownWorkflow>.Instance);
            using var controller = new CountdownWindowController(f.Main, state);
            CountdownWindow? window = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<CountdownWindow>((view, _) => { window = view; opens++; });
            var title = f.Main.ResponseTitle;
            var body = f.Main.ResponseBody;
            await Run(f, "timer start 2");
            var transcript = f.Main.Transcript;
            window.Should().NotBeNull();
            Find<TextBlock>(window!, "CountdownStatus").Text.Should().Contain("2 seconds");
            time.Advance(1);
            Find<TextBlock>(window!, "CountdownStatus").Text.Should().Contain("1 seconds");
            f.Main.Transcript.Should().Be(transcript);
            Find<Button>(window!, "CountdownRefresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Main.Transcript.Should().Contain("1 seconds");
            await Run(f, "timer status");
            opens.Should().Be(1);
            transcript = f.Main.Transcript;
            time.Advance(1);
            Find<TextBlock>(window!, "CountdownStatus").Text.Should().Contain("complete");
            opens.Should().Be(1);
            f.Main.Transcript.Should().Be(transcript);
            f.Main.ResponseTitle.Should().Be(title);
            f.Main.ResponseBody.Should().Be(body);
            await Run(f, "timer start 5");
            Find<Button>(window!, "CountdownCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            state.Status.Should().Contain("cancelled");
            await Run(f, "timer start 5");
            window!.Close();
            state.Status.Should().Contain("cancelled");
            await Run(f, "timer status");
            opens.Should().Be(2);
            controller.Dispose();
            window.IsVisible.Should().BeFalse();
            await Run(f, "timer start 1");
            f.Main.Transcript.Should().Contain("unavailable");
        });
    }

    [WindowsFact]
    public async Task NativePrivateClosureCancelsAndCannotReplayCompletion()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = new NativeUxFixtureSession(parent);
            await f.InitializeAsync();
            var time = new Clock();
            using var state = new CountdownWorkflow(time, new ImmediateDispatcher(), NullLogger<CountdownWorkflow>.Instance);
            using var controller = new CountdownWindowController(f.Main, state);
            CountdownWindow? window = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<CountdownWindow>((view, _) => window = view);
            await Run(f, "timer start 1");
            f.Main.CloseForObservedPrivacyEvent("fixture", true);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
            window!.IsVisible.Should().BeFalse();
            state.Status.Should().Contain("cancelled");
            time.Advance(10);
            state.Status.Should().NotContain("complete");
            await Run(f, "timer status");
            f.Main.Transcript.Should().Contain("unavailable");
        });
    }

    private static async Task Run(NativeUxFixtureSession f, string command)
    {
        f.Main.CommandText = command;
        await f.Main.RunTypedCommand.ExecuteAsync();
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => string.Equals(control.Name, name, StringComparison.Ordinal));

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        private readonly List<Timer> timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(() => callback(state));
            timers.Add(timer);
            return timer;
        }
        internal void Advance(int seconds)
        {
            timestamp += seconds * TimeSpan.TicksPerSecond;
            foreach (var timer in timers.Where(timer => !timer.Disposed).ToArray()) { timer.Fire(); }
        }
        private sealed class Timer(Action callback) : ITimer
        {
            internal bool Disposed { get; private set; }
            internal void Fire() => callback();
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
