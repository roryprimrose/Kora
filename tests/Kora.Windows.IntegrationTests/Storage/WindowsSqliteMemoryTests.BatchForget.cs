using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteMemoryTests
{
    [WindowsFact]
    public async Task ReviewedSelectedForgetReopensOnlyContentFreeNonReusableTombstonesAndPreservesUnselectedMemory()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        MemoryRecord first;
        MemoryRecord second;
        MemoryRecord unselected;
        using (var admission = Service(fixture))
        {
            first = await Admit(fixture, admission);
            second = await Admit(fixture, admission);
            await Run(fixture, () => admission.DisableAsync(second.Id, second.Revision, fixture.Token));
            unselected = await Admit(fixture, admission);
        }
        await FinishIntent(fixture);
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using (var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time))
        {
            using var workflow = new MemoryBatchWorkflow(management);
            var list = await management.ExecuteAsync(new(MemoryCommandOperation.List, fixture.Request.SessionId.Value),
                RequestOrigin.LocalUi, () => true, fixture.Token);
            var selected = list.Memories.Where(row => row.Id == first.Id.Value || row.Id == second.Id.Value).ToArray();
            var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, fixture.Request.SessionId, selected,
                () => true, fixture.Token);
            review.Records.Should().HaveCount(2);
            var result = await workflow.ConfirmAsync(review, () => true, fixture.Token);
            result.Items.Should().OnlyContain(item => item.State == MemoryBatchState.Succeeded
                && item.ActionResult!.Record!.Candidate == null && item.ActionResult.Record.Receipt == null);
            review.Records.Should().BeEmpty();
        }
        fixture.Count("reviewed_memory").Should().Be(3);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        using var restarted = Service(fixture);
        await NewIntent(fixture);
        var rows = await fixture.RunAsync(async () => await restarted.ObserveSessionAsync(fixture.Token));
        await FinishIntent(fixture);
        foreach (var original in new[] { first, second })
        {
            var tombstone = rows.Single(row => row.Id == original.Id);
            tombstone.Retention.Should().Be(MemoryRetentionState.Forgotten);
            tombstone.Candidate.Should().BeNull();
            tombstone.Receipt.Should().BeNull();
            (await Run(fixture, () => restarted.EditAsync(tombstone.Id, tombstone.Revision, Candidate, fixture.Token)))
                .Outcome.Should().Be(MemoryOutcome.InvalidTransition);
            (await Run(fixture, () => restarted.UseAsync(tombstone.Id, tombstone.Revision, MemoryDestination.Local, fixture.Token)))
                .Use.Should().BeNull();
        }
        (await Run(fixture, () => restarted.UseAsync(unselected.Id, unselected.Revision, MemoryDestination.Local, fixture.Token)))
            .Use!.Candidate.Should().Be(Candidate);
        AssertContentFreeAudit(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedForgetActualSqliteAuditOrCommitFailureNeverPublishesADeletedBody(bool commitFailure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using (var admission = Service(fixture)) { await Admit(fixture, admission); }
        await FinishIntent(fixture);
        var checkpoint = new InteractionTransactionCheckpoint();
        var access = new Access();
        fixture.Reopen(checkpoint);
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using (var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time))
        {
            using var workflow = new MemoryBatchWorkflow(management);
            var list = await management.ExecuteAsync(new(MemoryCommandOperation.List, fixture.Request.SessionId.Value),
                RequestOrigin.LocalUi, () => true, fixture.Token);
            var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, fixture.Request.SessionId, list.Memories,
                () => true, fixture.Token);
            if (commitFailure) { checkpoint.Commit = (_, _) => throw new IOException("required commit unavailable"); }
            else { checkpoint.Audit = (_, _) => throw new IOException("required audit unavailable"); }
            var result = await workflow.ConfirmAsync(review, () => true, fixture.Token);
            result.Items.Single().State.Should().Be(MemoryBatchState.Unavailable);
            result.Items.Single().ActionResult.Should().BeNull();
            review.Records.Should().BeEmpty();
        }
        fixture.Reopen();
        Payload(fixture).Should().NotContain("\"Candidate\":null");
        fixture.Count("reviewed_memory").Should().Be(1);
    }
}
