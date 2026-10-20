using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Tools.Clipboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardInspectWebUrlTests
{
    [Fact]
    public async Task Inspection_uses_exact_capture_without_read_reuse_history_or_other_authority()
    {
        using var listener = Listen();
        var reader = new Reader();
        using var broker = Broker(reader);
        using var host = Host();
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var source = broker.Current!;
        var action = new ClipboardInspectWebUrl(broker, NullLogger<ClipboardInspectWebUrl>.Instance);
        var notifications = 0;
        broker.Changed += (_, _) => notifications++;
        action.Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Accepted);
        reader.Reads.Should().Be(1);
        notifications.Should().Be(0);
        broker.Current.Should().BeSameAs(source);
        source.Text.Should().Be("https://example.com/private?token=secret#hidden");
        source.Request.Should().BeSameAs(host.Request);
        source.CapturedAt.Should().Be(broker.Current!.CapturedAt);
        reader.Text = "not a URL";
        action.Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Accepted);
        broker.Clear();
        action.Execute(source, () => true, CancellationToken.None).Should().Be(new WebUrlSyntaxResult(WebUrlSyntaxOutcome.Stale));
        await broker.CaptureAsync(() => true, CancellationToken.None);
        action.Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
        action.Execute(broker.Current!, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.InvalidSyntax);
        broker.Dispose();
        action.Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eligibility_and_cancellation_are_checked_before_parse_and_before_publication(bool afterParse)
    {
        using var listener = Listen();
        using var host = Host();
        using var broker = Broker(new Reader());
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var source = broker.Current!;
        var action = new ClipboardInspectWebUrl(broker, NullLogger<ClipboardInspectWebUrl>.Instance);
        var checks = 0;
        bool Eligible() => ++checks <= (afterParse ? 1 : 0);
        action.Execute(source, Eligible, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
        using var cancel = new CancellationTokenSource();
        checks = 0;
        bool Cancel()
        {
            if (++checks == (afterParse ? 2 : 1)) { cancel.Cancel(); }
            return true;
        }
        if (!afterParse) { cancel.Cancel(); }
        action.Execute(source, Cancel, cancel.Token).Outcome.Should().Be(WebUrlSyntaxOutcome.Cancelled);
    }

    [Fact]
    public async Task Reentrancy_is_not_queued_and_failures_are_logged_without_exception_content()
    {
        using var listener = Listen();
        using var host = Host();
        var privacy = true;
        using var broker = Broker(new Reader());
        await broker.CaptureAsync(() => privacy, CancellationToken.None);
        var source = broker.Current!;
        var logger = new SafeLogger();
        var action = new ClipboardInspectWebUrl(broker, logger);
        bool Reenter()
        {
            action.Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Busy);
            return true;
        }
        action.Execute(source, Reenter, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
        action.Execute(source, () => throw new InvalidOperationException("https://secret:password@private/path?token#fragment"),
            CancellationToken.None).Should().Be(new WebUrlSyntaxResult(WebUrlSyntaxOutcome.Failed));
        logger.Events.Should().ContainSingle().Which.Should().NotContain("password").And.NotContain("https").And.NotContain("token");
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
        privacy = false;
        action.Execute(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
        privacy = true;
        broker.Current.Should().BeNull();
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
    private static HostActivity Host() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
    private static ClipboardSnapshotBroker Broker(Reader reader) => new(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
    private sealed class Reader : IPlainTextClipboardReader
    {
        public string Text { get; set; } = "https://example.com/private?token=secret#hidden";
        public int Reads { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, Text, 1));
        }
    }
    private sealed class SafeLogger : ILogger<ClipboardInspectWebUrl>
    {
        public List<string> Events { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
