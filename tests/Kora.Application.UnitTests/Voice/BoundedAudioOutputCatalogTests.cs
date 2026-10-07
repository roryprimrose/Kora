using AwesomeAssertions;
using Kora.Application.UnitTests.Maintenance;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Voice;

public sealed class BoundedAudioOutputCatalogTests
{
    [Fact]
    public async Task Five_second_metadata_deadline_cancellation_and_late_fault_never_accumulate_workers_or_publish_late_output()
    {
        var clock = new ReleaseFixture.Clock();
        var completion = new TaskCompletionSource<AudioOutputCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var catalog = new BoundedAudioOutputCatalog(() => { calls++; return completion.Task; }, clock, NullLogger.Instance);
        var pending = catalog.RefreshAsync(TestContext.Current.CancellationToken);
        clock.Timers.Single().Due.Should().Be(TimeSpan.FromSeconds(5));
        clock.Timers.Single().Fire();
        await pending.Invoking(async task => await task).Should().ThrowAsync<TimeoutException>();
        await catalog.Invoking(value => value.RefreshAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        completion.SetResult(new([new("one", "Output")], null));
        (await catalog.RefreshAsync(TestContext.Current.CancellationToken)).Devices.Should().ContainSingle();
        calls.Should().Be(2);
        var late = new TaskCompletionSource<AudioOutputCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new BoundedAudioOutputCatalog(() => late.Task, clock, new EnabledLogger());
        using var cancellation = new CancellationTokenSource();
        var waiting = cancelled.RefreshAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await waiting.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
        late.SetException(new IOException("owned synthetic late fault"));
        await cancelled.Invoking(value => value.RefreshAsync(CancellationToken.None)).Should().ThrowAsync<IOException>();
    }

    private sealed class EnabledLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
