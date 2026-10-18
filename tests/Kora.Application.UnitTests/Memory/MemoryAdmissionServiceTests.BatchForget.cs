using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Memory;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForgetExactRecordFenceUsesTheExistingVolatileAuthoritativeTransition(bool changed)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var record = await fixture.Admitted();
        var exact = changed ? record with { Candidate = new(MemoryContentClass.Decision, "different reviewed body") } : record;
        var result = await fixture.Service.ForgetAsync(record.Id, record.Revision, Token, exact);
        result.Outcome.Should().Be(changed ? MemoryOutcome.RevisionConflict : MemoryOutcome.Succeeded);
        if (!changed)
        {
            result.Record!.Candidate.Should().BeNull();
            result.Record.Receipt.Should().BeNull();
            result.Record.Retention.Should().Be(MemoryRetentionState.Forgotten);
        }
    }

    [Fact]
    public async Task SelectedForgetUsesSeparateAuditedExactSessionActionsAndLeavesUnselectedContentUntouched()
    {
        using var fixture = new Fixture(durable: true);
        Guid first;
        Guid secondId;
        using (var root = fixture.Root())
        {
            first = (await fixture.Admitted()).Id.Value;
            var second = await fixture.Admitted();
            secondId = second.Id.Value;
            await fixture.Service.DisableAsync(second.Id, second.Revision, Token);
            await fixture.Admitted();
        }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories
            .Where(row => row.Id == first || row.Id == secondId).ToArray();
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, fixture.Session.SessionId, selection, () => true, Token);
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Should().OnlyContain(item => item.State == MemoryBatchState.Succeeded
            && item.ActionResult!.Record!.Candidate == null && item.ActionResult.Record.Receipt == null);
        fixture.Stored.Should().HaveCount(3);
        fixture.Stored.Values.Count(row => row.Retention == MemoryRetentionState.Forgotten).Should().Be(2);
        fixture.Stored.Values.Count(row => row.Candidate != null).Should().Be(1);
        fixture.Audits.Count(audit => audit.ActionId == "memory.forget" && audit.Outcome == SecurityAuditOutcome.Succeeded)
            .Should().Be(2);
        fixture.ControlIntents.Should().OnlyContain(intent => intent.Request.SessionId == fixture.Session.SessionId);
        fixture.ControlIntents.Select(intent => intent.Request.RequestId).Should().OnlyHaveUniqueItems();
        fixture.LogRequests.Should().OnlyContain(request => request.SessionId == fixture.Session.SessionId);
        fixture.Messages.Should().NotContain(message => message.Contains(Candidate.Value!, StringComparison.Ordinal));
        review.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("unknown")]
    [InlineData("cancel")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("generation")]
    [InlineData("host")]
    public async Task AuthoritativeForgetStopsAtFirstFailureAndNeverRepublishesEarlierRemovedContent(string fault)
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root())
        {
            await fixture.Admitted();
            await fixture.Admitted();
            await fixture.Admitted();
        }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories;
        var control = fixture.ControlRevision;
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, fixture.Session.SessionId, selection,
            () => fixture.ControlRevision == control, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        fixture.OnAfterStoreCommit = () =>
        {
            if (fixture.Stored.Values.Count(row => row.Retention == MemoryRetentionState.Forgotten) != 1) { return; }
            switch (fault)
            {
                case "audit": fixture.FailTerminalAudit = true; break;
                case "commit": fixture.FailStoreCommit = true; break;
                case "unknown": throw new IOException("commit result unavailable");
                case "cancel": cancellation.Cancel(); break;
                case "control": fixture.ControlRevision++; break;
                case "source": fixture.Boundary = fixture.Boundary! with { SourceRevision = new(2) }; break;
                case "privacy": fixture.CanControl = false; break;
                case "generation":
                    fixture.Session = fixture.Session with { Generation = new(2) };
                    fixture.Boundary = fixture.Boundary! with { Generation = new(2) };
                    break;
                case "host": fixture.Current = false; break;
            }
        };
        var result = await workflow.ConfirmAsync(review, () => fixture.ControlRevision == control, cancellation.Token);
        fixture.Stored.Values.Count(row => row.Retention == MemoryRetentionState.Forgotten).Should().Be(1);
        result.Items[0].State.Should().Be(fault is "unknown" ? MemoryBatchState.Unavailable : MemoryBatchState.Succeeded);
        result.Items[2].State.Should().Be(MemoryBatchState.NotAttempted);
        result.Items.Should().OnlyContain(item => item.ActionResult == null || item.ActionResult.Record == null
            || item.ActionResult.Record.Candidate == null);
        result.StoppedBy.Should().NotBeNull();
        review.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("body")]
    [InlineData("classification")]
    [InlineData("revision")]
    [InlineData("scope")]
    [InlineData("missing")]
    public async Task ForgetFencesTheCompleteExactRowInsideItsOwnAuthoritativeStorageTransaction(string change)
    {
        using var fixture = new Fixture(durable: true);
        MemoryRecord record;
        using (var root = fixture.Root()) { record = await fixture.Admitted(); }
        await using var service = fixture.Management();
        var actions = (IMemoryBatchActions)service;
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        await actions.InspectAsync(fixture.Session.SessionId, selection, () => true, Token);
        var transactions = 0;
        fixture.OnStoreTransition = () =>
        {
            if (++transactions != 2) { return; }
            if (change is "missing") { fixture.Stored.Remove(record.Id); return; }
            fixture.Stored[record.Id] = change switch
            {
                "body" => record with { Candidate = new(MemoryContentClass.Decision, "same revision other body") },
                "classification" => record with { Candidate = record.Candidate! with { ContentClass = MemoryContentClass.Decision } },
                "revision" => record with { Revision = new(record.Revision.Value + 1) },
                "scope" => record with { Scope = MemoryScope.Session(new(Guid.NewGuid())) },
                _ => throw new InvalidOperationException(),
            };
        };
        MemoryResult? result = null;
        await actions.ApplyAsync(MemoryBatchOperation.Forget, fixture.Session.SessionId, record, () => true,
            value => result = value, Token);
        result!.Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        fixture.Stored.Values.Should().NotContain(row => row.Retention == MemoryRetentionState.Forgotten);
        fixture.Audits.Should().NotContain(audit => audit.ActionId == "memory.forget"
            && audit.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForgetReviewCannotFollowAForeignSessionOrNewGeneration(bool generationOnly)
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories;
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, fixture.Session.SessionId, selection, () => true, Token);
        if (generationOnly)
        {
            fixture.Session = fixture.Session with { Generation = new(2) };
            fixture.Boundary = fixture.Boundary! with { Generation = new(2) };
        }
        else { fixture.SwitchSession(); }
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Should().NotContain(item => item.State == MemoryBatchState.Succeeded);
        fixture.Stored.Values.Should().NotContain(row => row.Retention == MemoryRetentionState.Forgotten);
    }

    [Fact]
    public async Task MissingLaterDurableRowCannotBeServedFromInspectionCacheOrCauseAnEarlierForget()
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root())
        {
            await fixture.Admitted();
            await fixture.Admitted();
        }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories;
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, fixture.Session.SessionId, selection, () => true, Token);
        fixture.Stored.Remove(new(selection[1].Id));
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Select(item => item.State).Should().Equal(MemoryBatchState.NotAttempted, MemoryBatchState.Conflict);
        fixture.Stored.Values.Should().OnlyContain(row => row.Retention == MemoryRetentionState.Enabled);
        fixture.Audits.Should().NotContain(audit => audit.ActionId == "memory.forget");
    }
}
