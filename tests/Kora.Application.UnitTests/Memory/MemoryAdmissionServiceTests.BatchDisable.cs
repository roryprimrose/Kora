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
    public async Task VolatileExactRecordFenceSharesTheSameDisableTransition(bool changed)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var record = await fixture.Admitted();
        var expected = changed ? record with { Candidate = new(MemoryContentClass.Decision, "different reviewed content") } : record;
        var result = await fixture.Service.DisableAsync(record.Id, record.Revision, Token, expected);
        result.Outcome.Should().Be(changed ? MemoryOutcome.RevisionConflict : MemoryOutcome.Succeeded);
    }

    [Fact]
    public async Task BatchUsesIndividualAuthoritativeTransitionsAuditsAndExactSessionControlIntents()
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root())
        {
            await fixture.Admitted();
            await fixture.Admitted();
            await fixture.Admitted();
        }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchDisableWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Take(2).ToArray();
        var review = await workflow.ReviewAsync(fixture.Session.SessionId, selection, () => true, Token);
        fixture.Stored.Values.Should().OnlyContain(record => record.Retention == MemoryRetentionState.Enabled);
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Should().OnlyContain(item => item.State == MemoryBatchDisableState.Succeeded);
        fixture.Stored.Values.Count(record => record.Retention == MemoryRetentionState.Disabled).Should().Be(2);
        fixture.Stored.Values.Count(record => record.Retention == MemoryRetentionState.Enabled).Should().Be(1);
        fixture.Audits.Where(audit => string.Equals(audit.ActionId, "memory.disable", StringComparison.Ordinal)
                && audit.Outcome == SecurityAuditOutcome.Succeeded)
            .Should().HaveCount(2);
        fixture.ControlIntents.Should().OnlyContain(intent => intent.Request.SessionId == fixture.Session.SessionId);
        fixture.ControlIntents.Select(intent => intent.Request.RequestId).Should().OnlyHaveUniqueItems();
        fixture.Messages.Should().NotContain(message => message.Contains(Candidate.Value!, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("unknown")]
    [InlineData("cancel")]
    [InlineData("control")]
    public async Task BatchPreservesCommittedEarlierActionsAndStopsAfterAuthoritativeFailure(string fault)
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root())
        {
            await fixture.Admitted();
            await fixture.Admitted();
            await fixture.Admitted();
        }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchDisableWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories;
        var control = fixture.ControlRevision;
        var review = await workflow.ReviewAsync(fixture.Session.SessionId, selection, () => fixture.ControlRevision == control, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        fixture.OnAfterStoreCommit = () =>
        {
            var disabled = fixture.Stored.Values.Count(record => record.Retention == MemoryRetentionState.Disabled);
            if (disabled != 1) { return; }
            switch (fault)
            {
                case "audit": fixture.FailTerminalAudit = true; break;
                case "commit": fixture.FailStoreCommit = true; break;
                case "unknown": throw new IOException("commit outcome unavailable");
                case "cancel": cancellation.Cancel(); break;
                case "control": fixture.ControlRevision++; break;
            }
        };
        var result = await workflow.ConfirmAsync(review, () => fixture.ControlRevision == control, cancellation.Token);
        fixture.Stored.Values.Count(record => record.Retention == MemoryRetentionState.Disabled).Should().Be(1);
        if (fault is "unknown")
        {
            result.Items[0].State.Should().Be(MemoryBatchDisableState.Unavailable);
            result.Items.Skip(1).Should().OnlyContain(item => item.State == MemoryBatchDisableState.NotAttempted);
        }
        else
        {
            result.Items[0].State.Should().Be(MemoryBatchDisableState.Succeeded);
            if (fault is "audit" or "commit") { result.Items[1].State.Should().Be(MemoryBatchDisableState.Unavailable); }
            result.Items[2].State.Should().Be(MemoryBatchDisableState.NotAttempted);
        }
        fixture.OnAfterStoreCommit = null;
        fixture.FailTerminalAudit = false;
        fixture.FailStoreCommit = false;
        fixture.ControlRevision = control;
        var retry = () => workflow.ConfirmAsync(review, () => true, Token);
        await retry.Should().ThrowAsync<InvalidOperationException>();
        fixture.Stored.Values.Count(record => record.Retention == MemoryRetentionState.Disabled).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRecordFenceRejectsRevisionAndContentAbaAtTheIndividualStorageTransition(bool missing)
    {
        using var fixture = new Fixture(durable: true);
        MemoryRecord record;
        using (var root = fixture.Root()) { record = await fixture.Admitted(); }
        await using var service = fixture.Management();
        var actions = (IMemoryBatchDisableActions)service;
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        await actions.InspectAsync(fixture.Session.SessionId, selection, () => true, Token);
        var transactions = 0;
        fixture.OnStoreTransition = () =>
        {
            // ObserveSession transaction first, then the actual authoritative disable transition.
            if (++transactions != 2) { return; }
            if (missing) { fixture.Stored.Remove(record.Id); }
            else { fixture.Stored[record.Id] = record with { Candidate = new(MemoryContentClass.Decision, "same revision different body") }; }
        };
        MemoryResult? result = null;
        await actions.DisableAsync(fixture.Session.SessionId, record, () => true, value => result = value, Token);
        result!.Outcome.Should().Be(MemoryOutcome.RevisionConflict);
        fixture.Stored.Values.Should().NotContain(row => row.Retention == MemoryRetentionState.Disabled);
        fixture.Audits.Should().NotContain(audit => audit.ActionId == "memory.disable" && audit.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchNeverRetargetsAnotherSessionOrANewSessionGeneration(bool generationOnly)
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        using var workflow = new MemoryBatchDisableWorkflow(service);
        var selection = (await Run(service, fixture, MemoryCommandOperation.List)).Memories;
        var originalSession = fixture.Session.SessionId;
        var review = await workflow.ReviewAsync(originalSession, selection, () => true, Token);
        if (generationOnly)
        {
            fixture.Session = fixture.Session with { Generation = new(fixture.Session.Generation.Value + 1) };
            fixture.Boundary = fixture.Boundary! with { Generation = fixture.Session.Generation };
        }
        else { fixture.SwitchSession(); }
        var outcome = await workflow.ConfirmAsync(review, () => true, Token);
        outcome.Items[0].State.Should().Be(generationOnly
            ? MemoryBatchDisableState.Conflict : MemoryBatchDisableState.Unavailable);
        fixture.Stored.Values.Should().OnlyContain(record => record.Retention == MemoryRetentionState.Enabled);
        fixture.ControlIntents.Should().OnlyContain(intent => intent.Request.SessionId == originalSession);
    }
}
