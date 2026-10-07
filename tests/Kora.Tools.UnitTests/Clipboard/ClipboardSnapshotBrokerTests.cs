using System.Diagnostics;
using AwesomeAssertions;
using Kora.Tools.Clipboard;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardSnapshotBrokerTests : IDisposable
{
    private readonly ActivityListener listener;
    private readonly List<Activity> activities = [];

    public ClipboardSnapshotBrokerTests()
    {
        listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
    }
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Explicit_capture_and_same_id_reuse_never_refresh_text_or_transmit_content()
    {
        var reader = new Reader();
        var logger = new CaptureLogger();
        using var service = new ClipboardSnapshotBroker(reader, TimeProvider.System, logger);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        service.Current.Should().BeNull();
        reader.Calls.Should().Be(0);
        using var host = Host();
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Captured);
        var snapshot = service.Current!;
        snapshot.Request.Should().Be(host.Request);
        reader.Result = new(ClipboardOutcome.Captured, "new clipboard", 2);
        service.Reuse(snapshot.SnapshotId, () => true).Should().Be(ClipboardOutcome.Reused);
        service.Current.Should().BeSameAs(snapshot);
        service.Current!.Text.Should().Be("private secret marker");
        reader.Calls.Should().Be(1);
        logger.Events.Should().NotContain(message => message.Contains("private", StringComparison.Ordinal)
            || message.Contains("secret marker", StringComparison.Ordinal));
        notifications.Should().Be(3);
        service.Reuse(Guid.NewGuid(), () => true).Should().Be(ClipboardOutcome.Stale);
        service.Clear();
        service.Current.Should().BeNull();
        service.Reuse(snapshot.SnapshotId, () => true).Should().Be(ClipboardOutcome.Stale);
    }

    [Theory]
    [InlineData(ClipboardOutcome.Empty)]
    [InlineData(ClipboardOutcome.UnsupportedFormat)]
    [InlineData(ClipboardOutcome.Oversize)]
    [InlineData(ClipboardOutcome.InvalidText)]
    [InlineData(ClipboardOutcome.Busy)]
    [InlineData(ClipboardOutcome.AccessDenied)]
    [InlineData(ClipboardOutcome.Changed)]
    [InlineData(ClipboardOutcome.Unavailable)]
    public async Task Every_native_failure_is_explicit_with_no_snapshot(ClipboardOutcome outcome)
    {
        var reader = new Reader { Result = new(outcome) };
        using var service = Service(reader);
        using var host = Host();
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(outcome);
        service.Current.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_or_oversize_success_shaped_reader_data_is_rejected()
    {
        foreach (var read in new[]
        {
            new ClipboardReadResult(ClipboardOutcome.Captured),
            new(ClipboardOutcome.Captured, "x", 0),
            new(ClipboardOutcome.Captured, "", 1),
            new(ClipboardOutcome.Captured, "\ud800", 1),
            new(ClipboardOutcome.Captured, "x\0y", 1),
            new(ClipboardOutcome.Captured, new string('x', ClipboardSnapshot.MaximumUtf8Bytes + 1), 1),
        })
        {
            using var service = Service(new Reader { Result = read });
            using var host = Host();
            (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(read.Text switch
            {
                null or "x" => ClipboardOutcome.Unavailable,
                "" => ClipboardOutcome.Empty,
                "\ud800" or "x\0y" => ClipboardOutcome.InvalidText,
                _ => ClipboardOutcome.Oversize,
            });
            service.Current.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task Nonhuman_origin_cannot_capture_or_reuse(RequestOrigin origin)
    {
        var reader = new Reader();
        using var service = Service(reader);
        using var host = Host(origin);
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Denied);
        service.Reuse(Guid.NewGuid(), () => true).Should().Be(ClipboardOutcome.Denied);
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Closed_gate_disposal_and_cancelled_request_never_read()
    {
        var reader = new Reader();
        using var service = Service(reader);
        using var host = Host();
        (await service.CaptureAsync(() => false, CancellationToken.None)).Should().Be(ClipboardOutcome.Denied);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (await service.CaptureAsync(() => true, cancellation.Token)).Should().Be(ClipboardOutcome.Cancelled);
        service.Reuse(Guid.NewGuid(), () => false).Should().Be(ClipboardOutcome.Denied);
        service.Dispose();
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Denied);
        service.Reuse(Guid.NewGuid(), () => true).Should().Be(ClipboardOutcome.Denied);
        reader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    [InlineData("cancel")]
    public async Task Late_reader_completion_cannot_present_after_generation_or_eligibility_loss(string reason)
    {
        var reader = new Reader { Completion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var service = Service(reader);
        using var cancellation = new CancellationTokenSource();
        using var host = Host();
        var eligible = true;
        var pending = service.CaptureAsync(() => eligible, cancellation.Token);
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Busy);
        switch (reason)
        {
            case "clear": service.Clear(); break;
            case "privacy": eligible = false; break;
            case "dispose": service.Dispose(); break;
            default: cancellation.Cancel(); break;
        }
        reader.Completion.SetResult(reader.Result);
        (await pending).Should().Be(ClipboardOutcome.Cancelled);
        service.Current.Should().BeNull();
        if (string.Equals(reason, "clear", StringComparison.Ordinal))
        {
            reader.Completion = null;
            (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Captured);
        }
    }

    [Fact]
    public async Task Presentation_gate_is_revalidated_and_old_content_is_cleared()
    {
        using var service = Service(new Reader());
        using var host = Host();
        var allowed = true;
        await service.CaptureAsync(() => allowed, CancellationToken.None);
        var id = service.Current!.SnapshotId;
        allowed = false;
        service.Current.Should().BeNull();
        service.Reuse(id, () => true).Should().Be(ClipboardOutcome.Stale);
    }

    [Fact]
    public async Task Capture_requires_live_host_context_not_an_incoming_activity()
    {
        using var activity = new Activity("untrusted").Start();
        using var service = Service(new Reader());
        var run = () => service.CaptureAsync(() => true, CancellationToken.None);
        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("io")]
    public async Task Read_failure_is_explicit_and_logs_only_failure_type(string type)
    {
        var logger = new CaptureLogger();
        var exception = type switch
        {
            "io" => new IOException("secret marker"),
            _ => (Exception)new InvalidOperationException("secret marker"),
        };
        using var service = new ClipboardSnapshotBroker(new Reader { Failure = exception }, TimeProvider.System, logger);
        service.Changed += (_, _) => { };
        using var host = Host();
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Unavailable);
        service.Current.Should().BeNull();
        logger.Events.Should().NotContain(message => message.Contains("secret marker", StringComparison.Ordinal));
        activities.Single(activity => string.Equals(activity.OperationName, "tool.invoke", StringComparison.Ordinal))
            .Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task A_reuse_race_cannot_retarget_a_cleared_snapshot()
    {
        using var service = Service(new Reader());
        using var host = Host();
        await service.CaptureAsync(() => true, CancellationToken.None);
        var id = service.Current!.SnapshotId;
        service.Reuse(id, () => { service.Clear(); return true; }).Should().Be(ClipboardOutcome.Stale);
    }

    private static HostActivity Host(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);

    [Fact]
    public async Task Cancellation_does_not_claim_native_quiescence_until_the_reader_releases_resources()
    {
        var reader = new Reader { Completion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var service = Service(reader);
        service.IsReading.Should().BeFalse();
        service.WaitForQuiescenceAsync().IsCompleted.Should().BeTrue();
        using var host = Host();
        var pending = service.CaptureAsync(() => true, CancellationToken.None);
        service.IsReading.Should().BeTrue();
        service.IsQuiescent.Should().BeFalse();
        var disposal = service.DisposeAsync();
        disposal.IsCompleted.Should().BeFalse();
        service.Current.Should().BeNull();
        reader.Completion.SetResult(reader.Result);
        (await pending).Should().Be(ClipboardOutcome.Cancelled);
        await disposal;
        service.IsReading.Should().BeFalse();
        service.IsQuiescent.Should().BeTrue();
        service.WaitForQuiescenceAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_native_release_is_latched_and_never_claims_clean_quiescence_or_retries(bool duringCancel)
    {
        var reader = new Reader { Result = new(ClipboardOutcome.Captured, "must not present", 1, ResourcesReleased: false) };
        var service = Service(reader);
        using var host = Host();
        service.IsQuiescent.Should().BeTrue();
        if (duringCancel) { reader.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        var capture = service.CaptureAsync(() => true, CancellationToken.None);
        var wait = service.WaitForQuiescenceAsync();
        if (duringCancel)
        {
            service.Clear();
            wait.IsCompleted.Should().BeFalse();
            reader.Completion!.SetResult(reader.Result);
        }
        (await capture).Should().Be(ClipboardOutcome.Unavailable);
        service.Current.Should().BeNull();
        service.IsQuiescent.Should().BeFalse();
        (await service.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Unavailable);
        reader.Calls.Should().Be(1);
        var awaitWait = () => wait;
        await awaitWait.Should().ThrowAsync<InvalidOperationException>();
        var awaitQuiescence = () => service.WaitForQuiescenceAsync();
        await awaitQuiescence.Should().ThrowAsync<InvalidOperationException>();
        var dispose = async () => await service.DisposeAsync();
        await dispose.Should().ThrowAsync<InvalidOperationException>();
    }
    private static ClipboardSnapshotBroker Service(Reader reader) =>
        new(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);

    private sealed class Reader : IPlainTextClipboardReader
    {
        public ClipboardReadResult Result { get; set; } = new(ClipboardOutcome.Captured, "private secret marker", 1);
        public TaskCompletionSource<ClipboardReadResult>? Completion { get; set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) { return Task.FromException<ClipboardReadResult>(Failure); }
            return Completion?.Task ?? Task.FromResult(Result);
        }
    }

    private sealed class CaptureLogger : ILogger<ClipboardSnapshotBroker>
    {
        public List<string> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add(formatter(state, exception));
    }
}
