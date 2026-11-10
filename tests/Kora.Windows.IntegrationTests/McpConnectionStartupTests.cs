using System.Net;
using AwesomeAssertions;
using Kora.Core.Mcp;
using Kora.Windows.Mcp;

namespace Kora.Windows.IntegrationTests;

public sealed class McpConnectionStartupTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("account", "deadline", false)]
    [InlineData("account", "deadline", true)]
    [InlineData("account", "caller", false)]
    [InlineData("account", "caller", true)]
    [InlineData("account", "withdrawal", false)]
    [InlineData("account", "withdrawal", true)]
    [InlineData("initial", "deadline", false)]
    [InlineData("initial", "deadline", true)]
    [InlineData("initial", "caller", false)]
    [InlineData("initial", "caller", true)]
    [InlineData("initial", "withdrawal", false)]
    [InlineData("initial", "withdrawal", true)]
    [InlineData("dns", "deadline", false)]
    [InlineData("dns", "deadline", true)]
    [InlineData("dns", "caller", false)]
    [InlineData("dns", "caller", true)]
    [InlineData("dns", "withdrawal", false)]
    [InlineData("dns", "withdrawal", true)]
    [InlineData("final", "deadline", false)]
    [InlineData("final", "deadline", true)]
    [InlineData("final", "caller", false)]
    [InlineData("final", "caller", true)]
    [InlineData("final", "withdrawal", false)]
    [InlineData("final", "withdrawal", true)]
    public async Task Startup_cancellation_joins_each_stage_and_never_publishes_late_connection(
        string stage, string reason, bool ignoresCancellation)
    {
        var clock = new StartupClock();
        using var f = new McpTransportFixture(clock);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Acquire = token => string.Equals(stage, "account", StringComparison.Ordinal)
            ? Block<McpAccountLease>(f.Lease, token) : Task.FromResult<McpAccountLease>(f.Lease);
        f.Lease.Validate = token => f.Lease.Validations == (string.Equals(stage, "initial", StringComparison.Ordinal) ? 1 : 2)
            && stage is "initial" or "final" ? Block(true, token) : Task.FromResult(true);
        f.Resolve = (_, token) => string.Equals(stage, "dns", StringComparison.Ordinal)
            ? Block(new[] { IPAddress.Parse("8.8.8.8") }, token)
            : Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        var pending = f.Broker.ConnectAsync(f.Account, caller.Token);
        try
        {
            var actualToken = await entered.Task.WaitAsync(Token);
            clock.Advance(TimeSpan.FromSeconds(29));
            actualToken.IsCancellationRequested.Should().BeFalse();
            pending.IsCompleted.Should().BeFalse();
            if (string.Equals(reason, "deadline", StringComparison.Ordinal)) { clock.Advance(TimeSpan.FromSeconds(1)); }
            else if (string.Equals(reason, "caller", StringComparison.Ordinal)) { await caller.CancelAsync(); }
            else { await f.Lease.Withdrawn.CancelAsync(); }

            var withdrawalBeforeAcquisition = stage is "account" && reason is "withdrawal";
            actualToken.IsCancellationRequested.Should().Be(!withdrawalBeforeAcquisition);
            f.HandlerCreations.Should().Be(0);
            f.Requests.Should().BeEmpty();
            if (ignoresCancellation || withdrawalBeforeAcquisition)
            {
                pending.IsCompleted.Should().BeFalse("cancellation is not physical startup completion");
                f.Lease.Disposals.Should().Be(0);
                clock.Timers.Should().ContainSingle();
                f.HandlerDisposed.Should().BeFalse();
                f.Lease.CredentialReads.Should().Be(0);
            }
            else
            {
                await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
            }
            release.TrySetResult();
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
            f.Lease.Disposals.Should().Be(stage is "account" && !ignoresCancellation
                && !withdrawalBeforeAcquisition ? 0 : 1);
            f.Acquisitions.Should().Be(1);
            f.Resolutions.Should().Be(stage is "dns" or "final" ? 1 : 0);
            f.HandlerCreations.Should().Be(0);
            f.Lease.CredentialReads.Should().Be(0);
            f.Requests.Should().BeEmpty();
            clock.Timers.Should().ContainSingle().Which.Disposed.Should().BeTrue();
        }
        finally
        {
            release.TrySetResult();
            try { await using var unexpected = await pending; }
            catch (OperationCanceledException) { }
        }

        async Task<T> Block<T>(T value, CancellationToken token)
        {
            entered.SetResult(token);
            if (ignoresCancellation) { await release.Task; }
            else { await release.Task.WaitAsync(token); }
            return value;
        }
    }

    [Fact]
    public async Task One_deadline_spans_acquisition_both_qualification_checks_and_dns()
    {
        var clock = new StartupClock();
        using var f = new McpTransportFixture(clock);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Acquire = _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            return Task.FromResult<McpAccountLease>(f.Lease);
        };
        f.Resolve = (_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(9));
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        };
        f.Lease.Validate = async token =>
        {
            if (f.Lease.Validations == 1) { clock.Advance(TimeSpan.FromSeconds(10)); }
            else
            {
                entered.SetResult(token);
                await release.Task;
            }
            return true;
        };
        var pending = f.Broker.ConnectAsync(f.Account, Token);
        try
        {
            var actualToken = await entered.Task.WaitAsync(Token);
            actualToken.IsCancellationRequested.Should().BeFalse();
            clock.Advance(TimeSpan.FromSeconds(1));
            actualToken.IsCancellationRequested.Should().BeTrue();
            pending.IsCompleted.Should().BeFalse();
            f.Lease.Disposals.Should().Be(0);
            release.SetResult();
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
            f.Lease.Disposals.Should().Be(1);
            f.Acquisitions.Should().Be(1);
            f.Resolutions.Should().Be(1);
            f.Lease.Validations.Should().Be(2);
            f.HandlerCreations.Should().Be(0);
            f.Requests.Should().BeEmpty();
        }
        finally
        {
            release.TrySetResult();
            try { await using var unexpected = await pending; }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Timely_startup_transfers_lease_and_exchange_gets_its_own_full_deadline()
    {
        var clock = new StartupClock();
        using var f = new McpTransportFixture(clock);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.Acquire = _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(9));
            return Task.FromResult<McpAccountLease>(f.Lease);
        };
        f.Lease.Validate = _ =>
        {
            clock.Advance(TimeSpan.FromSeconds(f.Lease.Validations == 1 ? 7 : 5));
            return Task.FromResult(true);
        };
        f.Resolve = (_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(8));
            return Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        };
        var connection = await f.Broker.ConnectAsync(f.Account, caller.Token);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Lease.Validate = _ => Task.FromResult(true);
        f.Send = async (_, token) =>
        {
            entered.SetResult(token);
            await release.Task;
            return McpTransportFixture.Json(McpTransportFixture.Initialization);
        };
        Task? pending = null;
        try
        {
            f.Lease.Disposals.Should().Be(0);
            f.Lease.CredentialReads.Should().Be(0);
            f.Requests.Should().BeEmpty();
            clock.Timers.Should().ContainSingle().Which.Disposed.Should().BeTrue();
            await caller.CancelAsync();
            clock.Advance(TimeSpan.FromSeconds(30));
            connection.AdmissionWithdrawn.IsCancellationRequested.Should().BeFalse();
            pending = connection.ExchangeAsync(McpTransportFixture.Wire(McpProtocol.InitializeRequest()), 16384, Token);
            var actualToken = await entered.Task.WaitAsync(Token);
            clock.Advance(TimeSpan.FromSeconds(29));
            actualToken.IsCancellationRequested.Should().BeFalse();
            clock.Advance(TimeSpan.FromSeconds(1));
            actualToken.IsCancellationRequested.Should().BeTrue();
            pending.IsCompleted.Should().BeFalse();
            var disposal = connection.DisposeAsync().AsTask();
            disposal.IsCompleted.Should().BeFalse();
            f.Lease.Disposals.Should().Be(0);
            f.HandlerDisposed.Should().BeFalse();
            release.SetResult();
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
            await disposal;
            f.Lease.Disposals.Should().Be(1);
            f.HandlerDisposed.Should().BeTrue();
            f.Acquisitions.Should().Be(1);
            f.HandlerCreations.Should().Be(1);
            f.Requests.Should().ContainSingle();
            clock.Timers.Should().HaveCount(2).And.OnlyContain(timer => timer.Disposed);
        }
        finally
        {
            release.TrySetResult();
            await connection.DisposeAsync();
            if (pending is not null)
            {
                try { await pending; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [Theory]
    [InlineData("deadline", false)]
    [InlineData("deadline", true)]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("withdrawal", false)]
    [InlineData("withdrawal", true)]
    public async Task Cancellation_during_transport_construction_disposes_handler_and_lease_without_publication(
        string reason, bool duringConnectionConstruction)
    {
        var clock = new StartupClock();
        using var f = new McpTransportFixture(clock);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.OnCreateHandler = () =>
        {
            if (duringConnectionConstruction) { f.Lease.OnAdmissionRead = Cancel; }
            else { Cancel(); }
        };
        void Cancel()
        {
            if (reason is "deadline") { clock.Advance(TimeSpan.FromSeconds(30)); }
            else if (reason is "caller") { caller.Cancel(); }
            else { f.Lease.Withdrawn.Cancel(); }
        }
        await FluentActions.Awaiting(async () =>
        {
            await using var unexpected = await f.Broker.ConnectAsync(f.Account, caller.Token);
        }).Should().ThrowAsync<OperationCanceledException>();
        f.HandlerCreations.Should().Be(1);
        f.HandlerDisposed.Should().BeTrue();
        f.Lease.Disposals.Should().Be(1);
        f.Lease.CredentialReads.Should().Be(0);
        f.Requests.Should().BeEmpty();
        f.Acquisitions.Should().Be(1);
    }

    [Fact]
    public async Task Already_cancelled_caller_never_acquires_or_starts_deadline()
    {
        var clock = new StartupClock();
        using var f = new McpTransportFixture(clock);
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await FluentActions.Awaiting(() => f.Broker.ConnectAsync(f.Account, caller.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        clock.Timers.Should().BeEmpty();
        f.Acquisitions.Should().Be(0);
        f.Resolutions.Should().Be(0);
        f.HandlerCreations.Should().Be(0);
        f.Lease.Disposals.Should().Be(0);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("initial")]
    [InlineData("dns")]
    [InlineData("final")]
    public async Task Startup_stage_failure_propagates_unchanged_without_retry_or_transport(string stage)
    {
        var clock = new StartupClock();
        using var f = new McpTransportFixture(clock);
        var failure = new InvalidDataException("Fake startup stage failed.");
        f.Acquire = _ => stage is "account" ? Task.FromException<McpAccountLease>(failure)
            : Task.FromResult<McpAccountLease>(f.Lease);
        f.Lease.Validate = _ => f.Lease.Validations == (stage is "initial" ? 1 : 2)
            && stage is "initial" or "final" ? Task.FromException<bool>(failure) : Task.FromResult(true);
        f.Resolve = (_, _) => stage is "dns" ? Task.FromException<IPAddress[]>(failure)
            : Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") });
        var thrown = await FluentActions.Awaiting(() => f.Broker.ConnectAsync(f.Account, Token))
            .Should().ThrowAsync<InvalidDataException>();
        thrown.Which.Should().BeSameAs(failure);
        f.Acquisitions.Should().Be(1);
        f.Resolutions.Should().Be(stage is "dns" or "final" ? 1 : 0);
        f.Lease.Disposals.Should().Be(stage is "account" ? 0 : 1);
        f.HandlerCreations.Should().Be(0);
        f.Lease.CredentialReads.Should().Be(0);
        f.Requests.Should().BeEmpty();
        clock.Timers.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    private sealed class StartupClock : TimeProvider
    {
        private TimeSpan elapsed;
        internal List<StartupTimer> Timers { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new StartupTimer(this, callback, state);
            timer.Change(dueTime, period);
            Timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan amount)
        {
            elapsed += amount;
            foreach (var timer in Timers.ToArray()) { timer.FireIfDue(); }
        }

        internal sealed class StartupTimer(StartupClock clock, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan? due;
            internal bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                period.Should().Be(Timeout.InfiniteTimeSpan);
                if (Disposed) { return false; }
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.elapsed + dueTime;
                return true;
            }

            internal void FireIfDue()
            {
                if (!Disposed && due is { } deadline && deadline <= clock.elapsed)
                {
                    due = null;
                    callback(state);
                }
            }

            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
