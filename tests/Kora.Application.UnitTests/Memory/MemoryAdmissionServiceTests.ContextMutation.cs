using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Theory]
    [InlineData(MemoryCommandOperation.Edit, false, false)]
    [InlineData(MemoryCommandOperation.Disable, false, false)]
    [InlineData(MemoryCommandOperation.Forget, false, false)]
    [InlineData(MemoryCommandOperation.Edit, true, false)]
    [InlineData(MemoryCommandOperation.Edit, false, true)]
    public async Task PendingMemoryMutationRefusesReplacementReviewUntilCompletionAndRequiresFreshConsent(
        MemoryCommandOperation operation, bool failCommit, bool cancel)
    {
        using var f = new Fixture(durable: true);
        MemoryRecord record;
        using (var root = f.Root()) { record = await f.Admitted(); }
        await using var management = f.Management(new ConcurrentMemoryAudit(f));
        using var workflow = management.CreateContextWorkflow();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var selection = new[] { Summary(record) };
        var original = await workflow.ReviewAsync(target, selection, "question", static () => true, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.BeforeStoreTransaction = token =>
        {
            f.BeforeStoreTransaction = null;
            entered.SetResult();
            return release.Task.WaitAsync(token);
        };
        var mutation = management.ExecuteAsync(new(operation, f.Session.SessionId.Value, record.Id.Value,
            record.Revision.Value, Candidate: Candidate), Kora.Core.Hosting.RequestOrigin.LocalUi,
            static () => true, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        try
        {
            original.Revocation.IsCancellationRequested.Should().BeTrue();
            original.Records.Should().BeEmpty();
            original.UserRequest.Should().BeEmpty();
            var intents = f.ControlIntents.Count;
            var audits = f.Audits.Count;
            await workflow.Invoking(value => value.ReviewAsync(target, selection, "replacement request",
                static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            f.ControlIntents.Should().HaveCount(intents, "a pending mutation cannot admit a private source read");
            f.Audits.Should().HaveCount(audits);
            f.Stored[record.Id].Should().Be(record, "the blocked review cannot mutate retained memory");
            workflow.Invoking(value => value.Confirm(original, static () => true, Token))
                .Should().Throw<InvalidOperationException>();
        }
        finally
        {
            f.FailStoreCommit = failCommit;
            if (cancel) { cancellation.Cancel(); }
            release.TrySetResult();
            if (cancel) { await ((Func<Task>)(async () => await mutation)).Should().ThrowAsync<OperationCanceledException>(); }
            else if (failCommit) { await ((Func<Task>)(async () => await mutation)).Should().ThrowAsync<IOException>(); }
            else { (await mutation).Outcome.Should().Be("Succeeded"); }
        }
        f.FailStoreCommit = false;
        original.Current.Should().BeFalse("completion or failure must not restore previous consent");
        if (failCommit || cancel)
        {
            f.Stored[record.Id].Should().Be(record);
            var fresh = await workflow.ReviewAsync(target, selection, "fresh request", static () => true, Token);
            fresh.Records.Should().Equal(record);
            using var use = workflow.Confirm(fresh, static () => true, Token);
            use.Current().Should().BeTrue("a new complete review and separate confirmation recover after failure or cancellation");
        }
        else
        {
            await workflow.Invoking(value => value.ReviewAsync(target, selection, "stale request",
                static () => true, Token)).Should().ThrowAsync<InvalidDataException>();
            var changed = (await Run(management, f, MemoryCommandOperation.List)).Memories.Single();
            changed.Revision.Should().Be(record.Revision.Value + 1);
            changed.Retention.Should().Be(operation switch
            {
                MemoryCommandOperation.Edit => MemoryRetentionState.Pending,
                MemoryCommandOperation.Disable => MemoryRetentionState.Disabled,
                _ => MemoryRetentionState.Forgotten,
            });
        }
    }

    private sealed class ConcurrentMemoryAudit(Fixture fixture) : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent)
        {
            var current = HostActivity.RequireCurrent();
            current.Request.SessionId.Should().Be(fixture.Session.SessionId);
            if (string.Equals(auditEvent.ActionId, "memory.context.read", StringComparison.Ordinal))
            { current.CorrelationId.Should().Be(auditEvent.CorrelationId); }
            fixture.ControlIntents.Should().Contain(row => ReferenceEquals(row.Request, current.Request));
            fixture.Audits.Add(auditEvent);
        }
    }
}
