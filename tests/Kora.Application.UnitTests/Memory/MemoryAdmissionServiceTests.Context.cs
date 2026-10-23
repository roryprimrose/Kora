using System.Collections.Immutable;
using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Fact]
    public async Task ContextReadReturnsOnlyExplicitExactEnabledRecordsWithoutMutationAndCorrelatesAudit()
    {
        using var f = new Fixture(durable: true);
        using var root = f.Root();
        var first = await f.Admitted();
        var unselected = await f.Admitted();
        var rows = f.Stored.Values.ToArray();
        var source = f.ContextSource();
        var audits = f.Audits.Count;
        var snapshot = await source.ReadAsync(new(f.Session, null), [Summary(first)], static () => true, Token);
        snapshot.Records.Should().Equal(first);
        snapshot.Boundary.Should().Be(f.Boundary);
        f.ControlOutcomes.Should().HaveCount(1).And.OnlyContain(outcome =>
            outcome.State == HostTaskState.Succeeded && outcome.Request == f.ControlIntents.Single().Request);
        f.Stored.Values.Should().Equal(rows);
        f.Stored.Should().ContainKey(unselected.Id);
        f.Audits.Skip(audits).Select(row => row.Outcome).Should()
            .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        f.LogRequests.Should().OnlyContain(request => request.SessionId.Value == first.Scope.Identity);
        var value = first.Candidate!.Value!;
        f.Messages.Should().NotContain(text => text.Contains(value, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("foreign")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("disabled")]
    [InlineData("forgotten")]
    [InlineData("class")]
    [InlineData("receipt")]
    [InlineData("receipt-boundary")]
    [InlineData("lineage")]
    [InlineData("retention-unknown")]
    public async Task ContextReadRejectsIneligibleCurrentRecordBeforeReturningAnyBody(string fault)
    {
        using var f = new Fixture(durable: true);
        using var root = f.Root();
        var record = await f.Admitted();
        var selection = Summary(record);
        if (fault is "missing") { f.Stored.Clear(); }
        else
        {
            f.Stored[record.Id] = fault switch
            {
                "changed" => record with { Revision = new(record.Revision.Value + 1) },
                "foreign" => record with { Scope = MemoryScope.Session(new(Guid.NewGuid())) },
                "pending" => record with { Review = MemoryReviewState.Proposed, Retention = MemoryRetentionState.Pending },
                "rejected" => record with { Review = MemoryReviewState.Rejected },
                "disabled" => record with { Retention = MemoryRetentionState.Disabled },
                "forgotten" => record with { Retention = MemoryRetentionState.Forgotten, Candidate = null, Receipt = null },
                "class" => record with { Candidate = new(MemoryContentClass.Credential, "not-for-disclosure") },
                "receipt" => record with { Receipt = null },
                "receipt-boundary" => record with
                    { Receipt = record.Receipt! with { Boundary = record.Receipt.Boundary with { Session = new(Guid.NewGuid()) } } },
                "lineage" => record with { Lineage = null! },
                _ => record with { Retention = (MemoryRetentionState)99 },
            };
        }
        var source = f.ContextSource();
        await source.Invoking(value => value.ReadAsync(new(f.Session, null), [selection], static () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("host")]
    [InlineData("owner")]
    [InlineData("boundary")]
    [InlineData("metadata")]
    [InlineData("generation")]
    [InlineData("origin")]
    [InlineData("activity-session")]
    [InlineData("commit")]
    [InlineData("receipt")]
    [InlineData("unobserved")]
    [InlineData("audit")]
    [InlineData("late-audit")]
    [InlineData("cancellation")]
    public async Task ContextReadPreservesAuthorityAndRejectsUnconfirmedReadOrAudit(string fault)
    {
        using var f = new Fixture(durable: true);
        using var original = f.Root();
        var record = await f.Admitted();
        var target = new SessionWorkspaceEntry(f.Session, null);
        using var root = HostActivity.BeginRoot(fault is "activity-session" ? HostRequest.Create(RequestOrigin.LocalUi)
            : new(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()),
                fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        switch (fault)
        {
            case "privacy": f.CanControl = false; break;
            case "host": f.Current = false; break;
            case "owner": f.Boundary = f.Boundary! with { IsOwner = false }; break;
            case "boundary": f.Boundary = f.Boundary! with { LineageKnown = false }; break;
            case "metadata": f.Session = f.Session with { IsActive = false }; break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; break;
            case "commit": f.OnStoreCommit = () => f.ControlRevision++; break;
            case "receipt": f.StoreResult = new(MemoryOutcome.Denied, MemoryReason.AuthorityClosed); break;
            case "unobserved": f.SkipStoreTransition = true; break;
            case "audit": f.FailRequestedAudit = true; break;
            case "late-audit": f.FailTerminalAudit = true; break;
            case "cancellation": cancellation.Cancel(); break;
        }
        var source = f.ContextSource();
        await source.Invoking(value => value.ReadAsync(target, [Summary(record)], static () => true, cancellation.Token))
            .Should().ThrowAsync<Exception>();
    }

    private static MemorySummary Summary(MemoryRecord record) =>
        new(record.Id.Value, record.Revision.Value, record.Scope, record.Review, record.Retention, record.CreatedAt);

    [Theory]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("receipt")]
    public async Task ContextReadCompletionRevalidatesPrivateAccessAndExactTerminalReceiptBeforeDisclosure(string fault)
    {
        using var f = new Fixture(durable: true);
        using var root = f.Root();
        var record = await f.Admitted();
        var source = f.ContextSource();
        f.AfterControlOutcome = () =>
        {
            if (fault is "privacy") { f.ControlRevision++; }
            else if (fault is "source") { source.Invalidate(); }
            else
            {
                var outcome = f.ControlOutcomes.Single();
                f.ControlOutcomes[0] = new(outcome.Request, new(2), HostTaskState.Unknown);
            }
        };
        await source.Invoking(value => value.ReadAsync(new(f.Session, null), [Summary(record)], static () => true, Token))
            .Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("forget")]
    [InlineData("privacy")]
    [InlineData("expire")]
    [InlineData("dispose")]
    public async Task NativeAndTypedManagementRevokePrivateMemoryContextBeforeMutationOrAccessLoss(string fault)
    {
        using var f = new Fixture(durable: true);
        MemoryRecord record;
        using (var root = f.Root()) { record = await f.Admitted(); }
        await using var management = f.Management(new ContextAudit(f));
        using var workflow = management.CreateContextWorkflow();
        var review = await workflow.ReviewAsync(new(f.Session, null), [Summary(record)], "question",
            static () => true, Token);
        (await Run(management, f, Kora.Core.Commands.MemoryCommandOperation.List)).Memories.Should().HaveCount(1);
        review.Records.Should().HaveCount(1, "passive metadata listing does not authorize or invalidate use");
        switch (fault)
        {
            case "disable": await Run(management, f, Kora.Core.Commands.MemoryCommandOperation.Disable, Summary(record)); break;
            case "forget": await Run(management, f, Kora.Core.Commands.MemoryCommandOperation.Forget, Summary(record)); break;
            case "privacy": management.ClearVolatile(); break;
            case "expire": management.ExpireSession(f.Session.SessionId); break;
            case "dispose": await management.DisposeAsync(); break;
        }
        review.Revocation.IsCancellationRequested.Should().BeTrue();
        review.Records.Should().BeEmpty();
        review.UserRequest.Should().BeEmpty();
    }

    private sealed class ContextAudit(Fixture fixture) : Kora.Core.Auditing.ISecurityAuditLog
    {
        public void Write(Kora.Core.Auditing.SecurityAuditEvent auditEvent)
        {
            if (!string.Equals(auditEvent.ActionId, "memory.context.read", StringComparison.Ordinal))
            { fixture.Write(auditEvent); return; }
            var current = HostActivity.RequireCurrent();
            current.Request.SessionId.Should().Be(fixture.Session.SessionId);
            current.CorrelationId.Should().Be(auditEvent.CorrelationId);
            current.Activity!.GetTagItem("kora.session.id").Should().Be(fixture.Session.SessionId.Value);
            current.Activity.TagObjects.Where(tag => tag.Value is string text
                && text.Contains("user-content-not-for-logs", StringComparison.Ordinal)).Should().BeEmpty();
            fixture.Audits.Add(auditEvent);
        }

    }

    private sealed partial class Fixture
    {
        internal SessionMemoryContextSource ContextSource()
        {
            MemoryControlEnabled = true;
            var sessions = new Kora.Application.Hosting.SessionWorkspaceService(this, new(this), this,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Kora.Application.Hosting.SessionWorkspaceService>.Instance);
            return new(sessions, this, this, this, this, this);
        }
    }
}
