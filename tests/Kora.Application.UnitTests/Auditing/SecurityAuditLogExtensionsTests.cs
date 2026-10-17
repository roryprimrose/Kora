using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Auditing;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Auditing;

[Collection("Host tracing")]
public sealed class SecurityAuditLogExtensionsTests
{
    [Fact]
    public async Task WriteAsyncYieldsUntilDurableAcknowledgmentAndPreservesTheLiveHost()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Policy);
        using var audit = new GatedAuditLog();
        var write = audit.WriteAsync(Event(), TestContext.Current.CancellationToken);
        try
        {
            await audit.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            write.IsCompleted.Should().BeFalse();
            audit.Context.Should().BeSameAs(host);
            audit.Committed.Should().BeFalse();
        }
        finally { audit.Release.Set(); }
        await write;
        audit.Committed.Should().BeTrue();
        HostActivity.RequireCurrent().Should().BeSameAs(host);
    }

    [Fact]
    public async Task WriteAsyncPropagatesFailureWithoutAcknowledgingACommit()
    {
        using var audit = new GatedAuditLog { Failure = new IOException("Fixture audit commit failed.") };
        audit.Release.Set();
        var write = () => audit.WriteAsync(Event(), TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<IOException>();
        audit.Committed.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsyncRejectsCancellationBeforeStartingAnyAudit()
    {
        using var audit = new GatedAuditLog();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var write = () => audit.WriteAsync(Event(), cancellation.Token);
        await write.Should().ThrowAsync<OperationCanceledException>();
        audit.Entered.Task.IsCompleted.Should().BeFalse();
    }

    private static SecurityAuditEvent Event() => new(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
        "fixture.save", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture.target");

    private sealed class GatedAuditLog : ISecurityAuditLog, IDisposable
    {
        private readonly CancellationToken token = TestContext.Current.CancellationToken;
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HostActivity? Context { get; private set; }
        public bool Committed { get; private set; }
        public IOException? Failure { get; init; }

        public void Write(SecurityAuditEvent auditEvent)
        {
            Context = HostActivity.Current;
            Entered.SetResult();
            Release.Wait(token);
            if (Failure is not null) { throw Failure; }
            Committed = true;
        }

        public void Dispose() => Release.Dispose();
    }
}
