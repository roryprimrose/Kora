using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class AuditedPreferenceWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestedAuditYieldsAndEligibilityIsCheckedAgainBeforeAtomicPersistence(bool eligible)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var audit = new GatedAudit();
        var writer = new AuditedPreferenceWriter(audit);
        var persisted = false;
        var write = writer.WriteAsync(() => persisted = true, "fixture.preference",
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => eligible);
        try
        {
            await audit.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            write.IsCompleted.Should().BeFalse();
            persisted.Should().BeFalse();
            audit.Request.Should().Be(host.Request);
        }
        finally { audit.Release.Set(); }
        if (eligible) { await write; }
        else
        {
            var failed = () => write;
            await failed.Should().ThrowAsync<InvalidOperationException>();
        }
        persisted.Should().Be(eligible);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            eligible ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed);
        audit.Events.Select(item => item.CorrelationId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task FailedRequiredTerminalAuditNeverReturnsSuccessfulPersistence()
    {
        using var audit = new GatedAudit { FailTerminal = true };
        audit.Release.Set();
        var persisted = false;
        var writer = new AuditedPreferenceWriter(audit);
        var write = () => writer.WriteAsync(() => persisted = true, "fixture.preference",
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        await write.Should().ThrowAsync<IOException>();
        persisted.Should().BeTrue("a failed receipt cannot claim rollback of an atomic preference write");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuiescenceWaitsForTerminalAuditWithoutReinterpretingTheWriteOutcome(bool failTerminal)
    {
        using var audit = new GatedAudit { GateTerminal = true, FailTerminal = failTerminal };
        var writer = new AuditedPreferenceWriter(audit);
        writer.WaitForQuiescenceAsync().IsCompletedSuccessfully.Should().BeTrue();
        var persisted = false;
        var write = writer.WriteAsync(() => persisted = true, "fixture.preference",
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        try
        {
            await audit.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            persisted.Should().BeTrue();
            writer.WaitForQuiescenceAsync().IsCompleted.Should().BeFalse();
        }
        finally { audit.Release.Set(); }
        if (failTerminal) { await FluentActions.Awaiting(() => write).Should().ThrowAsync<IOException>(); }
        else { await write; }
        await writer.WaitForQuiescenceAsync();
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task QuiescenceJoinsEveryConcurrentWriteAndStartsANewCohortAfterRelease()
    {
        using var audit = new GatedAudit();
        var writer = new AuditedPreferenceWriter(audit);
        var first = writer.WriteAsync(static () => { }, "fixture.first",
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        var second = writer.WriteAsync(static () => { }, "fixture.second",
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        Task joined;
        try
        {
            await audit.BothEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            joined = writer.WaitForQuiescenceAsync();
            joined.IsCompleted.Should().BeFalse();
        }
        finally { audit.Release.Set(); }
        await Task.WhenAll(first, second, joined);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var next = writer.WriteAsync(() =>
        {
            entered.SetResult();
            release.Wait(TestContext.Current.CancellationToken);
        }, "fixture.next", SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            writer.WaitForQuiescenceAsync().Should().NotBeSameAs(joined);
            writer.WaitForQuiescenceAsync().IsCompleted.Should().BeFalse();
        }
        finally { release.Set(); }
        await Task.WhenAll(next, writer.WaitForQuiescenceAsync());
    }

    [Fact]
    public async Task RequestedAuditFailureReleasesTheWorkerButDoesNotRunPersistence()
    {
        using var audit = new GatedAudit { FailRequested = true };
        var writer = new AuditedPreferenceWriter(audit);
        var persisted = false;
        var write = writer.WriteAsync(() => persisted = true, "fixture.preference",
            SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi, () => true);
        await FluentActions.Awaiting(() => write).Should().ThrowAsync<IOException>();
        await writer.WaitForQuiescenceAsync();
        persisted.Should().BeFalse();
    }

    private sealed class GatedAudit : ISecurityAuditLog, IDisposable
    {
        private readonly CancellationToken token = TestContext.Current.CancellationToken;
        private int requested;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public ConcurrentQueue<SecurityAuditEvent> Events { get; } = new();
        public HostRequest? Request { get; private set; }
        public bool FailTerminal { get; init; }
        public bool FailRequested { get; init; }
        public bool GateTerminal { get; init; }

        public void Write(SecurityAuditEvent auditEvent)
        {
            Request = HostActivity.Current?.Request;
            Events.Enqueue(auditEvent);
            if (auditEvent.Outcome == SecurityAuditOutcome.Requested)
            {
                if (FailRequested) { throw new IOException("Fixture required requested audit failure."); }
                if (Interlocked.Increment(ref requested) == 2) { BothEntered.TrySetResult(); }
                if (!GateTerminal)
                {
                    Entered.TrySetResult();
                    Release.Wait(token);
                }
            }
            else
            {
                if (GateTerminal) { Entered.TrySetResult(); Release.Wait(token); }
                if (FailTerminal) { throw new IOException("Fixture required terminal audit failure."); }
            }
        }

        public void Dispose() => Release.Dispose();
    }
}
