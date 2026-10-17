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

    private sealed class GatedAudit : ISecurityAuditLog, IDisposable
    {
        private readonly CancellationToken token = TestContext.Current.CancellationToken;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public ConcurrentQueue<SecurityAuditEvent> Events { get; } = new();
        public HostRequest? Request { get; private set; }
        public bool FailTerminal { get; init; }

        public void Write(SecurityAuditEvent auditEvent)
        {
            Request = HostActivity.Current?.Request;
            Events.Enqueue(auditEvent);
            if (auditEvent.Outcome == SecurityAuditOutcome.Requested)
            {
                Entered.SetResult();
                Release.Wait(token);
            }
            else if (FailTerminal) { throw new IOException("Fixture required terminal audit failure."); }
        }

        public void Dispose() => Release.Dispose();
    }
}
