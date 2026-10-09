using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Memory;

public sealed partial class MemoryAdmissionServiceTests
{
    [Fact]
    public async Task Management_shares_delivered_transitions_and_requires_exact_inspect_review_then_separate_admission()
    {
        using var fixture = new Fixture(durable: true);
        MemoryRecord admitted;
        using (var root = fixture.Root()) { admitted = await fixture.Admitted(); }
        await using var service = fixture.Management();
        var listed = await Run(service, fixture, MemoryCommandOperation.List);
        listed.Memories.Single().Id.Should().Be(admitted.Id.Value);
        listed.Inspected.Should().BeNull();
        var inspected = await Run(service, fixture, MemoryCommandOperation.Inspect, listed.Memories.Single());
        inspected.Inspected.Should().Be(admitted);
        var disabled = await Run(service, fixture, MemoryCommandOperation.Disable, inspected.Memories.Single());
        disabled.Memories.Single().Retention.Should().Be(MemoryRetentionState.Disabled);
        (await Run(service, fixture, MemoryCommandOperation.Admit, disabled.Memories.Single())).Outcome.Should().Be("InvalidTransition");
        var edited = await Run(service, fixture, MemoryCommandOperation.Edit, disabled.Memories.Single(), Candidate);
        edited.Memories.Single().Review.Should().Be(MemoryReviewState.Proposed);
        edited.Memories.Single().Retention.Should().Be(MemoryRetentionState.Pending);
        fixture.Stored.Single().Value.Candidate.Should().BeNull();
        var act = () => Run(service, fixture, MemoryCommandOperation.Review, edited.Memories.Single());
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Inspect this exact*");
        (await Run(service, fixture, MemoryCommandOperation.Admit, edited.Memories.Single())).Outcome.Should().Be("InvalidTransition");
        inspected = await Run(service, fixture, MemoryCommandOperation.Inspect, edited.Memories.Single());
        inspected.Inspected!.Candidate.Should().Be(Candidate);
        var reviewed = await Run(service, fixture, MemoryCommandOperation.Review, inspected.Memories.Single());
        reviewed.Inspected.Should().BeNull();
        reviewed.Memories.Single().Review.Should().Be(MemoryReviewState.Reviewed);
        fixture.Stored.Single().Value.Candidate.Should().BeNull();
        var readmitted = await Run(service, fixture, MemoryCommandOperation.Admit, reviewed.Memories.Single());
        readmitted.Memories.Single().Retention.Should().Be(MemoryRetentionState.Enabled);
        var forgotten = await Run(service, fixture, MemoryCommandOperation.Forget, readmitted.Memories.Single());
        forgotten.Memories.Single().Retention.Should().Be(MemoryRetentionState.Forgotten);
        fixture.Stored.Single().Value.Candidate.Should().BeNull();
        foreach (var operation in new[] { MemoryCommandOperation.Edit, MemoryCommandOperation.Review,
            MemoryCommandOperation.Admit, MemoryCommandOperation.Disable, MemoryCommandOperation.Forget })
        {
            if (operation == MemoryCommandOperation.Review)
            {
                await Run(service, fixture, MemoryCommandOperation.Inspect, forgotten.Memories.Single());
            }
            (await Run(service, fixture, operation, forgotten.Memories.Single(), Candidate)).Outcome.Should().Be("InvalidTransition");
        }
        fixture.ControlIntents.Select(record => record.Request.RequestId).Should().OnlyHaveUniqueItems();
        fixture.ControlIntents.Select(record => record.Request.TaskId).Should().OnlyHaveUniqueItems();
        fixture.ControlIntents.Should().OnlyContain(record => record.Request.SessionId == fixture.Session.SessionId);
        fixture.ControlOutcomes.Should().Contain(record => record.State == HostTaskState.Denied);
    }

    [Fact]
    public async Task Rejection_edit_stale_revision_missing_identity_and_restart_shell_have_explicit_outcomes()
    {
        using var fixture = new Fixture(durable: true);
        MemoryRecord admitted;
        using (var root = fixture.Root()) { admitted = await fixture.Admitted(); }
        await using var service = fixture.Management();
        var original = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        var edited = await Run(service, fixture, MemoryCommandOperation.Edit, original, Candidate);
        (await Run(service, fixture, MemoryCommandOperation.Disable, original)).Outcome.Should().Be("RevisionConflict");
        await Run(service, fixture, MemoryCommandOperation.Inspect, edited.Memories.Single());
        var rejected = await service.ExecuteAsync(new(MemoryCommandOperation.Review, fixture.Session.SessionId.Value,
            admitted.Id.Value, edited.Memories.Single().Revision, Accept: false), RequestOrigin.LocalUi, () => true, Token);
        rejected.Memories.Single().Review.Should().Be(MemoryReviewState.Rejected);
        (await Run(service, fixture, MemoryCommandOperation.Admit, rejected.Memories.Single())).Outcome.Should().Be("InvalidTransition");
        var missing = original with { Id = Guid.NewGuid() };
        (await Run(service, fixture, MemoryCommandOperation.Inspect, missing)).Outcome.Should().Be("NotFound");
        await using var restart = fixture.Management();
        var shell = (await Run(restart, fixture, MemoryCommandOperation.List)).Memories.Single();
        (await Run(restart, fixture, MemoryCommandOperation.Inspect, shell)).Inspected!.Candidate.Should().BeNull();
        (await Run(restart, fixture, MemoryCommandOperation.Review, shell)).Outcome.Should().Be("InvalidTransition");
        (await Run(restart, fixture, MemoryCommandOperation.Admit, shell)).Outcome.Should().Be("InvalidTransition");
    }

    [Theory]
    [InlineData(MemoryCommandOperation.List)]
    [InlineData(MemoryCommandOperation.Inspect)]
    [InlineData(MemoryCommandOperation.Review)]
    [InlineData(MemoryCommandOperation.Admit)]
    [InlineData(MemoryCommandOperation.Edit)]
    [InlineData(MemoryCommandOperation.Disable)]
    [InlineData(MemoryCommandOperation.Forget)]
    public async Task Host_system_model_callback_cannot_relabel_itself_as_original_user_input(MemoryCommandOperation operation)
    {
        using var fixture = new Fixture(durable: true);
        await using var service = fixture.Management();
        using var model = fixture.Root(RequestOrigin.HostSystem);
        var command = new MemoryCommand(operation, fixture.Session.SessionId.Value, Guid.NewGuid(), 1, Candidate: Candidate);
        var act = () => service.ExecuteAsync(command, RequestOrigin.LocalUi, () => true, Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*original local user*");
        fixture.ControlIntents.Should().BeEmpty();
        fixture.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("lifecycle")]
    [InlineData("input")]
    [InlineData("cancel")]
    [InlineData("corrupt")]
    [InlineData("foreign-row")]
    public async Task Authoritative_reads_revalidate_every_boundary_and_never_publish_content_on_failure(string change)
    {
        using var fixture = new Fixture(durable: true);
        MemoryRecord admitted;
        using (var root = fixture.Root()) { admitted = await fixture.Admitted(); }
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        var eligible = true;
        using var cancel = new CancellationTokenSource();
        fixture.OnStoreTransition = () =>
        {
            switch (change)
            {
                case "owner": fixture.Current = false; break;
                case "privacy": fixture.CanControl = false; break;
                case "revision": fixture.ControlRevision++; break;
                case "generation": fixture.Session = fixture.Session with { Generation = new(2) }; break;
                case "session": fixture.Session = fixture.Session with { SessionId = new(Guid.NewGuid()) }; break;
                case "lifecycle": service.ExpireSession(fixture.Request.SessionId); break;
                case "input": eligible = false; break;
                case "cancel": cancel.Cancel(); break;
                case "corrupt": throw new InvalidDataException("Saved memory is corrupt.");
                case "foreign-row":
                    fixture.Stored[admitted.Id] = admitted with { Scope = MemoryScope.Session(new(Guid.NewGuid())) };
                    break;
            }
        };
        // A generation change is observed in the final store boundary as well as in workspace metadata.
        fixture.OnStoreCommit = () =>
        {
            if (change is "generation" or "session")
            {
                fixture.Boundary = fixture.Boundary! with { Generation = fixture.Session.Generation, Session = fixture.Session.SessionId };
            }
        };
        var act = () => service.ExecuteAsync(new(MemoryCommandOperation.Inspect, fixture.Request.SessionId.Value, row.Id, row.Revision),
            RequestOrigin.LocalUi, () => eligible, cancel.Token);
        if (change is "corrupt" or "foreign-row") { await act.Should().ThrowAsync<InvalidDataException>(); }
        else { await act.Should().ThrowAsync<Exception>(); }
    }

    [Fact]
    public async Task Inspection_is_invalidated_by_control_epoch_or_presentation_closure_and_replacement_revalidates_limits()
    {
        using var fixture = new Fixture(durable: true);
        using (var root = fixture.Root()) { await fixture.Admitted(); }
        await using var service = fixture.Management();
        var row = (await Run(service, fixture, MemoryCommandOperation.List)).Memories.Single();
        var invalid = await Run(service, fixture, MemoryCommandOperation.Edit, row,
            new(MemoryContentClass.Decision, new string('x', 513)));
        invalid.Outcome.Should().Be("Denied");
        fixture.Stored.Single().Value.Retention.Should().Be(MemoryRetentionState.Enabled);
        row = (await Run(service, fixture, MemoryCommandOperation.Edit, row, Candidate)).Memories.Single();
        await Run(service, fixture, MemoryCommandOperation.Inspect, row);
        service.ClearInspection();
        var review = () => Run(service, fixture, MemoryCommandOperation.Review, row);
        await review.Should().ThrowAsync<InvalidOperationException>();
        await Run(service, fixture, MemoryCommandOperation.Inspect, row);
        fixture.ControlRevision++;
        fixture.Boundary = fixture.Boundary! with { Revision = fixture.ControlRevision };
        await review.Should().ThrowAsync<InvalidOperationException>();
    }

    private static Task<MemoryCommandResult> Run(MemoryManagementService service, Fixture fixture,
        MemoryCommandOperation operation, MemorySummary? row = null, MemoryCandidate? candidate = null) =>
        service.ExecuteAsync(new(operation, fixture.Session.SessionId.Value, row?.Id, row?.Revision ?? 0,
            Accept: operation == MemoryCommandOperation.Review, Candidate: candidate), RequestOrigin.LocalUi, () => true, Token);

    private sealed partial class Fixture : IHostTaskStore
    {
        internal bool MemoryControlEnabled { get; private set; }
        internal List<HostTaskRecord> ControlIntents { get; } = [];
        internal List<HostTaskRecord> ControlOutcomes { get; } = [];
        internal SessionWorkspaceService? ManagementSessions { get; private set; }
        internal MemoryManagementService Management()
        {
            MemoryControlEnabled = true;
            ManagementSessions = new(this, new(this), this, NullLogger<SessionWorkspaceService>.Instance);
            return new(ManagementSessions,
                this, this, this, this, this, this, new FixedTime());
        }
        public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
        {
            ControlOutcomes.Add(record);
            return ValueTask.CompletedTask;
        }
        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<HostTaskRecord>>([]);
    }
}
