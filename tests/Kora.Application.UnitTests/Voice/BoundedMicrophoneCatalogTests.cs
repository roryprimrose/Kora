using AwesomeAssertions;
using Kora.Application.UnitTests.Maintenance;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Voice;

public sealed class BoundedMicrophoneCatalogTests
{
    private static MicrophoneCatalogSnapshot Snapshot() => new([new("mic", "Headset")], null,
        new(WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed, 0, ["mic"], "mic", null),
        new(MicrophoneAccessState.Allowed, "owned synthetic"));
    [Fact]
    public async Task Refresh_is_metadata_only_and_a_completed_operation_can_be_retried()
    {
        var snapshot = Snapshot();
        var calls = 0;
        var catalog = new BoundedMicrophoneCatalog(() =>
        {
            calls++;
            return Task.FromResult(snapshot);
        }, TimeProvider.System, NullLogger.Instance);

        (await catalog.RefreshAsync(CancellationToken.None)).Should().BeSameAs(snapshot);
        (await catalog.RefreshAsync(CancellationToken.None)).Should().BeSameAs(snapshot);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Five_second_deadline_and_cancellation_never_accumulate_native_workers_or_apply_late_results()
    {
        var clock = new ReleaseFixture.Clock();
        var completion = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var catalog = new BoundedMicrophoneCatalog(() =>
        {
            calls++;
            return completion.Task;
        }, clock, NullLogger.Instance);
        var pending = catalog.RefreshAsync(CancellationToken.None);
        clock.Timers.Single().Due.Should().Be(TimeSpan.FromSeconds(5));
        clock.Timers.Single().Fire();
        await pending.Invoking(async task => await task).Should().ThrowAsync<TimeoutException>();
        await catalog.Invoking(value => value.RefreshAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        completion.SetResult(Snapshot());
        (await catalog.RefreshAsync(CancellationToken.None)).Devices.Should().ContainSingle();
        calls.Should().Be(2);

        var held = new TaskCompletionSource<MicrophoneCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new EnabledLogger();
        var cancelled = new BoundedMicrophoneCatalog(() => held.Task, clock, logger);
        using var cancellation = new CancellationTokenSource();
        var waiting = cancelled.RefreshAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await waiting.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        held.SetException(new IOException("late owned synthetic failure"));
        await cancelled.Invoking(value => value.RefreshAsync(CancellationToken.None)).Should().ThrowAsync<IOException>();
    }

    private sealed class EnabledLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }
}
