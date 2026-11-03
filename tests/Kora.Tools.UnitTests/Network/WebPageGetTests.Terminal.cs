using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Tools.Network;

namespace Kora.Tools.UnitTests.Network;

public sealed partial class WebPageGetTests
{
    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 200)]
    [InlineData(false, 404)]
    [InlineData(true, 404)]
    [InlineData(false, 302)]
    [InlineData(true, 302)]
    public async Task ResponseReleasePrecedesTerminalAudit(bool releaseFails, int statusCode)
    {
        var audit = new Audit();
        var released = false;
        var transport = new Transport(PublicAddress);
        transport.Responses.Enqueue(new(statusCode, new("https://example.com/"), "text/plain", "utf-8",
            null, null, [], new TerminalStream(onDispose: () =>
            {
                audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested);
                released = true;
                if (releaseFails) { throw new IOException("release failed"); }
            })));
        var execution = new WebPageGet(transport, audit).ExecuteAsync(new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser, static (_, _) => ValueTask.FromResult(true),
            TestContext.Current.CancellationToken);

        if (releaseFails)
        {
            await FluentActions.Awaiting(() => execution).Should().ThrowAsync<IOException>();
        }
        else
        {
            (await execution).Outcome.Should().Be(
                statusCode == 200 ? WebPageGetOutcome.Succeeded : WebPageGetOutcome.Failed);
        }

        released.Should().BeTrue();
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            releaseFails || statusCode != 200 ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationDuringReadOrReleaseDeniesTextAndSuccessReceipt(bool duringRead)
    {
        var current = true;
        var audit = new Audit();
        var transport = new Transport(PublicAddress);
        transport.Responses.Enqueue(new(200, new("https://example.com/"), "text/plain", "utf-8",
            null, null, [], new TerminalStream(
                onRead: () => { if (duringRead) { current = false; } },
                onDispose: () => { if (!duringRead) { current = false; } })));

        var result = await new WebPageGet(transport, audit).ExecuteAsync(new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser, (_, _) => ValueTask.FromResult(current),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(WebPageGetOutcome.Denied);
        result.Reason.Should().Be("address-not-authorized");
        result.Text.Should().BeNull();
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData(CancellationPhase.Authorize, 0)]
    [InlineData(CancellationPhase.DispatchAuthorize, 0)]
    [InlineData(CancellationPhase.Resolve, 0)]
    [InlineData(CancellationPhase.Send, 1)]
    [InlineData(CancellationPhase.Read, 1)]
    [InlineData(CancellationPhase.Release, 1)]
    [InlineData(CancellationPhase.TerminalAuthorize, 1)]
    public async Task IgnoredCancellationCannotDispatchLaterOrPublishSuccess(CancellationPhase phase, int sends)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var audit = new Audit();
        var released = false;
        var transport = new TerminalTransport(
            () => { if (phase == CancellationPhase.Resolve) { cancellation.Cancel(); } },
            () => { if (phase == CancellationPhase.Send) { cancellation.Cancel(); } },
            new TerminalStream(
                () => { if (phase == CancellationPhase.Read) { cancellation.Cancel(); } },
                () =>
                {
                    released = true;
                    if (phase == CancellationPhase.Release) { cancellation.Cancel(); }
                }));
        var checks = 0;

        await FluentActions.Awaiting(() => new WebPageGet(transport, audit).ExecuteAsync(
            new(new("https://example.com/")), SecurityAuditInitiator.LocalUser, (_, _) =>
            {
                checks++;
                if (phase == CancellationPhase.Authorize
                    || phase == CancellationPhase.DispatchAuthorize && checks == 2
                    || phase == CancellationPhase.TerminalAuthorize && checks == 3)
                { cancellation.Cancel(); }
                return ValueTask.FromResult(true);
            }, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        transport.Sends.Should().Be(sends);
        released.Should().Be(sends != 0);
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Cancelled);
    }

    [Fact]
    public async Task BodyRemainsOwnedUntilAnUncooperativeReadActuallyFinishes()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRead = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = false;
        var audit = new Audit();
        var transport = new TerminalTransport(static () => { }, static () => { },
            new PendingReadStream(reading, finishRead, () => released = true));
        var execution = new WebPageGet(transport, audit).ExecuteAsync(new(new("https://example.com/")),
            SecurityAuditInitiator.LocalUser, static (_, _) => ValueTask.FromResult(true), cancellation.Token);
        try
        {
            await reading.Task.WaitAsync(TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            execution.IsCompleted.Should().BeFalse();
            released.Should().BeFalse();
            audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested);
        }
        finally
        {
            finishRead.TrySetResult(0);
        }

        await FluentActions.Awaiting(() => execution).Should().ThrowAsync<OperationCanceledException>();
        released.Should().BeTrue();
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Cancelled);
    }

    public enum CancellationPhase { Authorize, Resolve, DispatchAuthorize, Send, Read, Release, TerminalAuthorize }

    private sealed class TerminalStream(Action? onRead = null, Action? onDispose = null)
        : MemoryStream("private retrieved text"u8.ToArray())
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            return base.ReadAsync(buffer, CancellationToken.None);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            onDispose?.Invoke();
        }
    }

    private sealed class PendingReadStream(
        TaskCompletionSource reading, TaskCompletionSource<int> finishRead, Action onDispose) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            reading.TrySetResult();
            return new(finishRead.Task);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            onDispose();
        }
    }

    private sealed class TerminalTransport(Action onResolve, Action onSend, Stream content) : IWebPageTransport
    {
        public int Sends { get; private set; }

        public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            onResolve();
            return Task.FromResult<IReadOnlyList<System.Net.IPAddress>>([PublicAddress]);
        }

        public Task<WebPageResponse> SendAsync(Uri address, System.Net.IPAddress endpoint, CancellationToken cancellationToken)
        {
            Sends++;
            onSend();
            return Task.FromResult(new WebPageResponse(200, address, "text/plain", "utf-8", 0, null, [], content));
        }
    }
}
