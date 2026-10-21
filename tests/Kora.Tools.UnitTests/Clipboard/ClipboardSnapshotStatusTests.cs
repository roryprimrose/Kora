using AwesomeAssertions;
using System.Diagnostics;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardSnapshotStatusTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public ClipboardSnapshotStatusTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData("same")]
    [InlineData("different")]
    [InlineData("empty-id")]
    [InlineData("none")]
    [InlineData("replaced")]
    [InlineData("revoked")]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("cancel")]
    [InlineData("cancel-at-publication")]
    [InlineData("replace-at-publication")]
    [InlineData("revoke-at-publication")]
    [InlineData("dispose")]
    [InlineData("system")]
    [InlineData("invocation")]
    [InlineData("host-retired")]
    [InlineData("clear-restore")]
    [InlineData("owner-at-publication")]
    public async Task Only_live_exact_snapshot_metadata_can_be_published_without_native_io(string change)
    {
        var reader = new Reader();
        using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        using var capture = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var sourceEligible = true;
        await broker.CaptureAsync(() => sourceEligible, TestContext.Current.CancellationToken);
        var original = broker.Current!;
        var requested = change is "different" ? Guid.NewGuid() : change is "empty-id" ? Guid.Empty : original.SnapshotId;
        using var cancellation = new CancellationTokenSource();
        switch (change)
        {
            case "none": broker.Clear(); break;
            case "replaced": await broker.CaptureAsync(() => true, TestContext.Current.CancellationToken); break;
            case "revoked": broker.Revoke(() => true); break;
            case "owner": sourceEligible = false; break;
            case "dispose": broker.Dispose(); break;
            case "cancel": cancellation.Cancel(); break;
        }
        var request = HostRequest.Create(change is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (change is "invocation")
        {
            request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid()));
        }
        using var status = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var reads = reader.Calls;
        ClipboardSnapshotStatusResult? published = null;
        Activity? hostile = null;
        new ClipboardSnapshotStatus(broker).Execute(requested, () =>
        {
            if (change is "cancel-at-publication") { cancellation.Cancel(); }
            if (change is "replace-at-publication") { broker.Clear(); }
            if (change is "revoke-at-publication") { broker.Revoke(() => true); }
            if (change is "host-retired") { hostile = new Activity("untrusted").Start(); }
            if (change is "clear-restore") { broker.Clear(); }
            if (change is "owner-at-publication") { sourceEligible = false; }
            return change is not "input";
        }, result => published = result, cancellation.Token);
        hostile?.Dispose();
        published!.IsPresent.Should().Be(change is "same");
        published.ToString().Should().NotContain(reader.Text);
        reader.Calls.Should().Be(reads);
        if (change is "same")
        {
            broker.Current.Should().BeSameAs(original);
            published.SnapshotId.Should().Be(original.SnapshotId);
            published.Utf8Bytes.Should().Be(original.Utf8Bytes);
        }
    }

    [Fact]
    public async Task Status_has_correlated_terminal_trace_and_only_a_typed_presence_log()
    {
        var logger = new StatusLogger();
        var activities = new List<Activity>();
        using var observation = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(observation);
        using var broker = new ClipboardSnapshotBroker(new Reader(), TimeProvider.System, logger);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await broker.CaptureAsync(() => true, TestContext.Current.CancellationToken);
        var original = broker.Current!;
        logger.Messages.Clear();
        var changes = 0;
        broker.Changed += (_, _) => changes++;
        new ClipboardSnapshotStatus(broker).Execute(original.SnapshotId, () => true, _ => { }, CancellationToken.None);
        changes.Should().Be(0);
        broker.Current.Should().BeSameAs(original);
        logger.Messages.Should().ContainSingle().Which.Should().Contain("True");
        logger.Messages[0].Should().NotContain(original.Text).And.NotContain(original.SnapshotId.ToString("D"));
        logger.TraceId.Should().Be(root.Activity!.TraceId);
        activities.Should().HaveCount(2);
        activities[^1].ParentSpanId.Should().Be(root.Activity.SpanId);
        activities[^1].Status.Should().Be(ActivityStatusCode.Ok);
        activities.SelectMany(activity => activity.TagObjects).Should().NotContain(tag =>
            tag.Value != null && tag.Value.ToString()!.Contains(original.Text, StringComparison.Ordinal));
    }

    private sealed class StatusLogger : ILogger<ClipboardSnapshotBroker>
    {
        public List<string> Messages { get; } = [];
        public ActivityTraceId TraceId { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            TraceId = Activity.Current!.TraceId;
        }
    }

    [Fact]
    public void Hostless_status_is_rejected_even_with_a_spoofed_trace()
    {
        using var activity = new System.Diagnostics.Activity("untrusted").Start();
        using var broker = new ClipboardSnapshotBroker(new Reader(), TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        var action = () => new ClipboardSnapshotStatus(broker).Execute(Guid.NewGuid(), () => true, _ => { }, CancellationToken.None);
        action.Should().Throw<InvalidOperationException>();
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        public string Text => "private source title/path/body marker";
        public int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, Text, 1));
        }
    }
}
