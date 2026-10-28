using System.Collections.Immutable;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Mcp;
using Kora.Windows.Mcp;

namespace Kora.Windows.IntegrationTests;

public sealed class McpTransportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Production_default_is_explicitly_unavailable_before_account_dns_or_http()
    {
        var broker = new WindowsMcpConnectionBroker();
        using var f = new McpTransportFixture();
        await FluentActions.Awaiting(() => broker.ReadAccountsAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => broker.ConnectAsync(f.Account, Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Exact_wire_and_delegated_destination_flow_through_real_connection()
    {
        using var f = new McpTransportFixture();
        (await f.Broker.ReadAccountsAsync(Token)).Should().ContainSingle().Which.Reference.Should().Be(f.Account);
        await using (var connection = await f.Broker.ConnectAsync(f.Account, Token))
        {
            connection.Account.Should().Be(f.Account);
            connection.Endpoint.Should().Be(f.Account.Destination);
            connection.RemoteAddresses.Should().Equal(IPAddress.Parse("8.8.8.8"));
            (await connection.IsCurrentAsync(Token)).Should().BeTrue();
            var initialized = await connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.InitializeRequest()), 16384, Token);
            McpProtocol.ValidateInitialization(initialized.AsSpan());
            await connection.NotifyAsync(McpTransportFixture.Wire(McpProtocol.InitializedNotification()), Token);
            var discovered = await connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.DiscoveryRequest()), 16384, Token);
            var tool = McpProtocol.ReadDiscovery(discovered.AsSpan()).Should().ContainSingle().Subject;
            var invocation = McpProtocol.InvocationRequest(tool.Name, tool.Schema, """{"query":"référence"}""");
            var result = await connection.ExchangeAsync(McpTransportFixture.Wire(invocation), 1536, Token);
            McpProtocol.ReadInvocationResult(result.AsSpan()).Text.Should().Be("reference 42");
            f.Requests.Select(request => request.Bytes).Should().BeEquivalentTo(new[]
            {
                McpProtocol.InitializeRequest(), McpProtocol.InitializedNotification(), McpProtocol.DiscoveryRequest(), invocation,
            }, options => options.WithStrictOrdering());
            f.Requests.Should().OnlyContain(request => request.Address == f.Account.Destination.Address
                && request.Method == HttpMethod.Post && request.Authorization == "Bearer fake-delegated-token"
                && request.ContentType == "application/json" && request.Accept == "application/json, text/event-stream"
                && request.Protocol == "2025-06-18" && request.Version == HttpVersion.Version11
                && request.VersionPolicy == HttpVersionPolicy.RequestVersionExact);
            f.Resolutions.Should().Be(1);
        }
        f.HandlerDisposed.Should().BeTrue();
        f.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task Redirect_and_authentication_failures_never_retry_or_forward_credentials(int status)
    {
        using var f = new McpTransportFixture();
        f.Send = (_, _) =>
        {
            var response = McpTransportFixture.Json("{}", (HttpStatusCode)status);
            response.Headers.Location = new("https://other.example/mcp");
            response.Headers.WwwAuthenticate.ParseAdd("Bearer");
            return Task.FromResult(response);
        };
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<InvalidDataException>();
        f.Requests.Should().ContainSingle().Which.Address.Should().Be(f.Account.Destination.Address);
        f.Acquisitions.Should().Be(1);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("encoding")]
    [InlineData("sse")]
    [InlineData("charset")]
    [InlineData("redirect")]
    public async Task Unsupported_http_profiles_are_refused(string profile)
    {
        using var f = new McpTransportFixture();
        f.Send = (_, _) =>
        {
            var response = McpTransportFixture.Json(McpTransportFixture.Initialization);
            switch (profile)
            {
                case "session": response.Headers.Add("Mcp-Session-Id", "unapproved-session"); break;
                case "encoding": response.Content.Headers.ContentEncoding.Add("gzip"); break;
                case "sse": response.Content.Headers.ContentType!.MediaType = "text/event-stream"; break;
                case "charset": response.Content.Headers.ContentType!.CharSet = "utf-16"; break;
                case "redirect": response.Headers.Location = new("/elsewhere", UriKind.Relative); break;
            }
            return Task.FromResult(response);
        };
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<InvalidDataException>();
        f.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(1536)]
    [InlineData(16384)]
    public async Task Complete_utf8_byte_bounds_accept_exact_limit_and_refuse_one_more(int limit)
    {
        using var f = new McpTransportFixture();
        var bytes = Encoding.UTF8.GetBytes(new string('é', limit / 2));
        f.Send = (_, _) => Task.FromResult(JsonBytes(bytes));
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        (await connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.InitializeRequest()), limit, Token))
            .Should().Equal(bytes);
        f.Send = (_, _) => Task.FromResult(JsonBytes([.. bytes, 32]));
        await FluentActions.Awaiting(() => connection.ExchangeAsync(
            McpTransportFixture.Wire(McpProtocol.InitializeRequest()), limit, Token)).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16385)]
    public async Task Invalid_request_bounds_do_not_send(int limit)
    {
        using var f = new McpTransportFixture();
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => connection.ExchangeAsync(
            McpTransportFixture.Wire(McpProtocol.InitializeRequest()), limit, Token)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => connection.ExchangeAsync([], 1536, Token)).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Awaiting(() => connection.ExchangeAsync(
            ImmutableArray.Create(new byte[16385]), 1536, Token)).Should().ThrowAsync<InvalidDataException>();
        f.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8,10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Unknown_or_nonpublic_dns_fails_before_http_and_disposes_lease(string dns)
    {
        using var f = new McpTransportFixture();
        f.Resolve = (_, _) => Task.FromResult(dns.Length == 0 ? [] : dns.Split(',').Select(IPAddress.Parse).ToArray());
        await FluentActions.Awaiting(() => f.Broker.ConnectAsync(f.Account, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Connected.Should().BeFalse();
        f.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Exact_account_revision_secret_and_endpoint_must_match_before_dns()
    {
        using var f = new McpTransportFixture();
        McpAccountReference[] others =
        [
            new(Guid.NewGuid(), f.Account.Revision, f.Account.SecretReference, f.Account.Destination),
            new(f.Account.AccountId, new(2), f.Account.SecretReference, f.Account.Destination),
            new(f.Account.AccountId, f.Account.Revision, Guid.NewGuid(), f.Account.Destination),
            new(f.Account.AccountId, f.Account.Revision, f.Account.SecretReference, new(new("https://other.example/mcp"))),
        ];
        foreach (var account in others)
        {
            await FluentActions.Awaiting(() => f.Broker.ConnectAsync(account, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Resolutions.Should().Be(0);
        f.Requests.Should().BeEmpty();
        f.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid\r\nHeader: value")]
    public async Task Missing_or_malformed_credential_never_falls_back_to_anonymous(string credential)
    {
        using var f = new McpTransportFixture();
        f.Lease.Credential = credential;
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<InvalidOperationException>();
        f.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Qualification_or_egress_withdrawal_during_dns_never_creates_http_transport()
    {
        using var f = new McpTransportFixture();
        f.Resolve = (_, _) =>
        {
            f.Lease.Current = false;
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        };
        await FluentActions.Awaiting(() => f.Broker.ConnectAsync(f.Account, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Connected.Should().BeFalse();
        f.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Changed_account_admission_refuses_send_and_disclosure()
    {
        using var f = new McpTransportFixture();
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        f.Lease.Current = false;
        (await connection.IsCurrentAsync(Token)).Should().BeFalse();
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<InvalidOperationException>();
        f.Requests.Should().BeEmpty();
        f.Lease.Current = true;
        f.Send = (_, _) =>
        {
            f.Lease.Current = false;
            return Task.FromResult(McpTransportFixture.Json(McpTransportFixture.Initialization));
        };
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<InvalidOperationException>();
        f.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("withdrawal")]
    [InlineData("disposal")]
    public async Task Cancellation_suppresses_late_bytes_and_disposal_joins_actual_work(string reason)
    {
        using var f = new McpTransportFixture();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = async (_, token) =>
        {
            entered.SetResult(token);
            await release.Task;
            return McpTransportFixture.Json(McpTransportFixture.Initialization);
        };
        var connection = await f.Broker.ConnectAsync(f.Account, Token);
        var pending = connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.InitializeRequest()), 16384, caller.Token);
        var actualToken = await entered.Task.WaitAsync(Token);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = actualToken.Register(() => cancelled.TrySetResult());
        var queued = Initialize(connection);
        if (string.Equals(reason, "caller", StringComparison.Ordinal)) { await caller.CancelAsync(); }
        if (string.Equals(reason, "withdrawal", StringComparison.Ordinal)) { await f.Lease.Withdrawn.CancelAsync(); }
        var disposal = connection.DisposeAsync().AsTask();
        try
        {
            await cancelled.Task.WaitAsync(Token);
            actualToken.IsCancellationRequested.Should().BeTrue();
            await FluentActions.Awaiting(() => queued.WaitAsync(Token)).Should().ThrowAsync<OperationCanceledException>();
            queued.IsCompleted.Should().BeTrue("disposal must cancel queued work before the active worker is released");
            pending.IsCompleted.Should().BeFalse();
            disposal.IsCompleted.Should().BeFalse();
            f.Disposed.Should().BeFalse();
            f.HandlerDisposed.Should().BeFalse();
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
                await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();
            }
            finally { await disposal; }
        }
        await connection.DisposeAsync();
        f.Disposed.Should().BeTrue();
        f.HandlerDisposed.Should().BeTrue();
        f.Requests.Should().ContainSingle();
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Caller_cancellation_does_not_cancel_an_independent_queued_exchange()
    {
        using var f = new McpTransportFixture();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = 0;
        f.Send = async (_, _) =>
        {
            if (Interlocked.Increment(ref sends) == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            return McpTransportFixture.Json(McpTransportFixture.Initialization);
        };
        var connection = await f.Broker.ConnectAsync(f.Account, Token);
        var pending = connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.InitializeRequest()), 16384, caller.Token);
        var queued = Initialize(connection);
        try
        {
            await entered.Task.WaitAsync(Token);
            await caller.CancelAsync();
            pending.IsCompleted.Should().BeFalse();
            queued.IsCompleted.Should().BeFalse();
            f.Requests.Should().ContainSingle();
            f.Disposed.Should().BeFalse();
            f.HandlerDisposed.Should().BeFalse();
            release.SetResult();
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
            McpProtocol.ValidateInitialization((await queued.WaitAsync(Token)).AsSpan());
            sends.Should().Be(2);
            f.Requests.Should().HaveCount(2);
            f.Disposed.Should().BeFalse();
            f.HandlerDisposed.Should().BeFalse();
        }
        finally
        {
            release.TrySetResult();
            try { await connection.DisposeAsync(); }
            finally
            {
                await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
                await queued;
            }
        }
        f.Disposed.Should().BeTrue();
        f.HandlerDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connection_withdrawal_or_existing_deadline_cancels_waiters_without_disposal_or_late_disclosure(bool timeout)
    {
        using var f = new McpTransportFixture();
        var clock = new ExchangeClock();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = async (_, token) =>
        {
            entered.SetResult(token);
            await release.Task;
            return McpTransportFixture.Json(McpTransportFixture.Initialization);
        };
        var connection = timeout ? f.CreateConnection(clock) : await f.Broker.ConnectAsync(f.Account, Token);
        var pending = Initialize(connection);
        var actualToken = await entered.Task.WaitAsync(Token);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = actualToken.Register(() => cancelled.TrySetResult());
        var queued = Initialize(connection);
        try
        {
            if (timeout)
            {
                clock.Timers.Should().HaveCount(2);
                clock.Timers.Should().OnlyContain(timer => timer.Due == TimeSpan.FromSeconds(30) && !timer.Disposed);
                clock.Fire();
            }
            else { await f.Lease.Withdrawn.CancelAsync(); }
            await cancelled.Task.WaitAsync(Token);
            await FluentActions.Awaiting(() => queued.WaitAsync(Token)).Should().ThrowAsync<OperationCanceledException>();
            pending.IsCompleted.Should().BeFalse("cancelling a wait does not join a cancellation-ignoring send");
            f.Requests.Should().ContainSingle();
            f.HandlerDisposed.Should().BeFalse();
            f.Disposed.Should().BeFalse();
            if (!timeout)
            {
                await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<OperationCanceledException>();
                f.Requests.Should().ContainSingle("withdrawal closes new admission even before disposal starts");
            }
            var disposal = connection.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            release.SetResult();
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
            await disposal;
            f.Requests.Should().ContainSingle();
            f.HandlerDisposed.Should().BeTrue();
            f.Disposed.Should().BeTrue();
            if (timeout) { clock.Timers.Should().OnlyContain(timer => timer.Disposed); }
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
                await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();
            }
            finally { await connection.DisposeAsync(); }
        }
    }

    [Fact]
    public void Production_handler_disables_all_ambient_or_forwarding_paths()
    {
        using var f = new McpTransportFixture();
        using var handler = WindowsMcpConnectionBroker.CreateHandler(f.Account.Destination, [IPAddress.Parse("8.8.8.8")]);
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
        handler.Credentials.Should().BeNull();
        handler.PreAuthenticate.Should().BeFalse();
        handler.AutomaticDecompression.Should().Be(DecompressionMethods.None);
        handler.ActivityHeadersPropagator.Should().BeNull();
        handler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
        handler.ConnectCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_disposal_shares_actual_cleanup_and_surfaces_lease_failure()
    {
        using var f = new McpTransportFixture();
        f.Lease.FailDisposal = true;
        var connection = await f.Broker.ConnectAsync(f.Account, Token);
        var first = connection.DisposeAsync().AsTask();
        var second = connection.DisposeAsync().AsTask();
        first.Should().BeSameAs(second);
        await FluentActions.Awaiting(() => first).Should().ThrowAsync<InvalidOperationException>();
        f.HandlerDisposed.Should().BeTrue();
        f.Disposed.Should().BeTrue();
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<ObjectDisposedException>();
    }

    private static Task<ImmutableArray<byte>> Initialize(IMcpConnection connection) =>
        connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.InitializeRequest()), 16384, Token);

    [Fact]
    public async Task Unqualified_source_does_not_acquire_a_credential_or_resolve_dns()
    {
        using var f = new McpTransportFixture { Qualified = false };
        await FluentActions.Awaiting(() => f.Broker.ReadAccountsAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => f.Broker.ConnectAsync(f.Account, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Resolutions.Should().Be(0);
        f.Connected.Should().BeFalse();
    }

    [Fact]
    public async Task Mutable_dns_observations_cannot_change_the_socket_pin()
    {
        using var f = new McpTransportFixture();
        var resolved = IPAddress.Parse("2606:4700:4700::1111");
        f.Resolve = (_, _) => Task.FromResult(new[] { resolved });
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        resolved.ScopeId = 10;
        connection.RemoteAddresses[0].ScopeId = 11;
        connection.RemoteAddresses[0].ScopeId.Should().Be(0);
        f.Pinned![0].ScopeId.Should().Be(0);
        f.Resolutions.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Declared_oversize_and_empty_exchange_bodies_are_refused(bool empty)
    {
        using var f = new McpTransportFixture();
        f.Send = (_, _) => Task.FromResult(McpTransportFixture.Json(empty ? string.Empty : new string('x', 1537)));
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => connection.ExchangeAsync(
            McpTransportFixture.Wire(McpProtocol.InitializeRequest()), 1536, Token)).Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(200, "")]
    [InlineData(202, "x")]
    public async Task Initialized_notification_requires_202_without_response_bytes(int status, string body)
    {
        using var f = new McpTransportFixture();
        f.Send = (_, _) => Task.FromResult(McpTransportFixture.Json(body, (HttpStatusCode)status));
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => connection.NotifyAsync(
            McpTransportFixture.Wire(McpProtocol.InitializedNotification()), Token)).Should().ThrowAsync<InvalidDataException>();
        f.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Admission_withdrawal_at_end_of_body_prevents_returning_complete_bytes()
    {
        using var f = new McpTransportFixture();
        f.Send = (_, _) => Task.FromResult(JsonBytes(Encoding.UTF8.GetBytes(McpTransportFixture.Initialization),
            () => f.Lease.Current = false));
        await using var connection = await f.Broker.ConnectAsync(f.Account, Token);
        await FluentActions.Awaiting(() => Initialize(connection)).Should().ThrowAsync<InvalidOperationException>();
        f.Requests.Should().ContainSingle();
    }

    private static HttpResponseMessage JsonBytes(byte[] bytes, Action? onEnd = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnseekableStream(bytes, onEnd)),
        };
        response.Content.Headers.ContentType = new("application/json");
        return response;
    }

    private sealed class UnseekableStream(byte[] bytes, Action? onEnd = null) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (read == 0) { onEnd?.Invoke(); }
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class ExchangeClock : TimeProvider
    {
        internal List<ExchangeTimer> Timers { get; } = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ExchangeTimer(callback, state, dueTime);
            Timers.Add(timer);
            return timer;
        }
        internal void Fire()
        {
            foreach (var timer in Timers.ToArray()) { timer.Fire(); }
        }
        internal sealed class ExchangeTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            internal TimeSpan Due { get; private set; } = due;
            internal bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return !Disposed; }
            internal void Fire() { if (!Disposed) { callback(state); } }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
