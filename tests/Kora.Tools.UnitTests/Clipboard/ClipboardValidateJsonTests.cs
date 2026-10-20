using System.Diagnostics;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardValidateJsonTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public ClipboardValidateJsonTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task ExactSourceSyntaxDoesNotReadAgainMutateRenewOrPublishContentToActivities()
    {
        var activities = new List<Activity>();
        using var captureListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(captureListener);
        using var host = Host();
        var reader = new Reader();
        var clock = new Clock();
        using var broker = Broker(reader, clock);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var source = broker.Current!;
        var notifications = 0;
        broker.Changed += (_, _) => notifications++;
        var action = new ClipboardValidateJson(broker, clock, NullLogger<ClipboardValidateJson>.Instance);
        action.Execute(source, () => true, TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardJsonSyntaxResult(ClipboardJsonSyntaxOutcome.Valid, JsonValueKind.Object));
        broker.Current.Should().BeSameAs(source);
        source.Text.Should().Be(reader.Text);
        source.CapturedAt.Should().Be(DateTimeOffset.UnixEpoch);
        notifications.Should().Be(0);
        reader.Calls.Should().Be(1);
        activities.Should().NotBeEmpty();
        activities.Should().OnlyContain(activity => activity.Tags.All(tag => !tag.Value!.Contains("private", StringComparison.Ordinal)));
        activities.Last().Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Theory]
    [InlineData("cancel-before")]
    [InlineData("cancel-parse")]
    [InlineData("cancel-after")]
    [InlineData("revoke")]
    [InlineData("dispose")]
    [InlineData("replace")]
    [InlineData("deny-before")]
    [InlineData("deny-after")]
    [InlineData("throw")]
    [InlineData("unexpected-cancel")]
    public async Task AdmissionAndFinalPublicationFenceReentrancyAndUnexpectedFailures(string change)
    {
        using var host = Host();
        var clock = new Clock();
        var reader = new Reader();
        using var broker = Broker(reader, clock);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var source = broker.Current!;
        var eligible = change is not "deny-before";
        var parseCalls = 0;
        Task<ClipboardOutcome>? replacement = null;
        var logger = new CaptureLogger();
        using var cancellation = new CancellationTokenSource();
        if (change is "cancel-before") { cancellation.Cancel(); }
        var action = new ClipboardValidateJson(broker, clock, logger, (exact, token) =>
        {
            exact.Should().BeSameAs(source);
            parseCalls++;
            switch (change)
            {
                case "cancel-parse": cancellation.Cancel(); token.ThrowIfCancellationRequested(); break;
                case "cancel-after": cancellation.Cancel(); break;
                case "revoke": broker.Clear(); break;
                case "dispose": broker.Dispose(); break;
                case "replace": replacement = broker.CaptureAsync(() => true, CancellationToken.None); break;
                case "deny-after": eligible = false; break;
                case "throw": throw new InvalidOperationException("private body/property/token/path");
                case "unexpected-cancel": throw new OperationCanceledException("private exception");
            }
            return new(ClipboardJsonSyntaxOutcome.Valid, JsonValueKind.Object);
        });
        var result = action.Execute(source, () => eligible, cancellation.Token);
        if (replacement is not null) { (await replacement).Should().Be(ClipboardOutcome.Captured); }
        result.Outcome.Should().Be(change switch
        {
            "deny-before" => ClipboardJsonSyntaxOutcome.Unavailable,
            "throw" or "unexpected-cancel" => ClipboardJsonSyntaxOutcome.Failed,
            _ => ClipboardJsonSyntaxOutcome.Cancelled,
        });
        result.RootKind.Should().BeNull();
        parseCalls.Should().Be(change is "cancel-before" or "deny-before" ? 0 : 1);
        logger.Messages.Should().HaveCount(change is "throw" or "unexpected-cancel" ? 1 : 0);
        logger.Messages.Should().NotContain(message => message.Contains("private", StringComparison.Ordinal));
        reader.Calls.Should().Be(change is "replace" ? 2 : 1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(899)]
    [InlineData(900)]
    public async Task OriginalCaptureLifetimeNeverRenewsAndRetiredAdmissionCannotResurrect(int seconds)
    {
        using var host = Host();
        var clock = new Clock();
        using var broker = Broker(new Reader(), clock);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var source = broker.Current!;
        var action = new ClipboardValidateJson(broker, clock, NullLogger<ClipboardValidateJson>.Instance);
        var initiallyDenied = false;
        var deniedAdmission = action.CaptureEligibility(source, () => initiallyDenied);
        initiallyDenied = true;
        deniedAdmission().Should().BeFalse();
        var eligible = true;
        var admission = action.CaptureEligibility(source, () => eligible);
        clock.Now = DateTimeOffset.UnixEpoch.AddSeconds(seconds);
        var allowed = seconds is >= 0 and < 900;
        admission().Should().Be(allowed);
        action.Execute(source, admission, CancellationToken.None).Outcome.Should().Be(allowed
            ? ClipboardJsonSyntaxOutcome.Valid : ClipboardJsonSyntaxOutcome.Unavailable);
        eligible = false;
        admission().Should().BeFalse();
        eligible = true;
        clock.Now = DateTimeOffset.UnixEpoch;
        admission().Should().BeFalse();
        broker.Current.Should().BeSameAs(source);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task NoVoiceModelOrHostSystemRouteCanParse(RequestOrigin origin)
    {
        var clock = new Clock();
        using var broker = Broker(new Reader(), clock);
        ClipboardSnapshot source;
        using (Host()) { await broker.CaptureAsync(() => true, CancellationToken.None); source = broker.Current!; }
        using var request = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        new ClipboardValidateJson(broker, clock, NullLogger<ClipboardValidateJson>.Instance)
            .Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Unavailable);
    }

    [Fact]
    public async Task DifferentExactObjectCannotBorrowCurrentIdAndOriginalRevocationStaysAuthoritative()
    {
        using var host = Host();
        var clock = new Clock();
        using var broker = Broker(new Reader(), clock);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var source = broker.Current!;
        var copy = new ClipboardSnapshot(source.SourceId, source.SnapshotId, source.Request, source.Text, source.Version, source.CapturedAt);
        var action = new ClipboardValidateJson(broker, clock, NullLogger<ClipboardValidateJson>.Instance);
        action.Execute(copy, () => true, CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Unavailable);
        var admission = action.CaptureEligibility(source, () => true);
        broker.Clear();
        admission().Should().BeFalse();
    }

    private static ClipboardSnapshotBroker Broker(Reader reader, Clock clock) =>
        new(reader, clock, NullLogger<ClipboardSnapshotBroker>.Instance);
    private static HostActivity Host() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
    private sealed class Reader : IPlainTextClipboardReader
    {
        internal string Text => "{\"private\":1}";
        internal int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, Text, 1)); }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CaptureLogger : ILogger<ClipboardValidateJson>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { exception.Should().BeNull(); Messages.Add(formatter(state, exception)); }
    }
}
