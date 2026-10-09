using System.Text;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteMemoryTests
{
    [WindowsFact]
    public async Task NativeMemorySelectionIsPassiveAndPrivacyClosureClearsInspectedContent()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var admission = Service(fixture);
        await Admit(fixture, admission);
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(sessions,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
                NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance, memories: management);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            await viewer.ListMemoriesAsync();
            viewer.MemoryDetail.Should().NotContain("exact reviewed memory");
            var audits = fixture.Count("security_audit_events");
            viewer.SelectMemory(viewer.MemoryRecords.Single());
            viewer.MemoryDetail.Should().BeEmpty();
            viewer.MemoryDraft.Should().BeEmpty();
            fixture.Count("security_audit_events").Should().Be(audits);
            await viewer.InspectMemoryAsync();
            viewer.MemoryDraft.Should().Be(Candidate.Value);
            viewer.MemoryDetail.Should().Contain("exact reviewed memory");
            access.CanControl = false;
            await viewer.ListMemoriesAsync();
            viewer.MemoryRecords.Should().BeEmpty();
            viewer.MemoryDraft.Should().BeEmpty();
            viewer.MemoryDetail.Should().BeEmpty();
            viewer.CanReviewMemory.Should().BeFalse();
            viewer.Status.Should().Contain("unavailable/denied");
        }
        finally { viewer.Close(); }
    }

    [WindowsFact]
    public async Task NativeLifecycleChangeRevokesInspectionAndResumedSessionListsOnlyTombstoneMetadata()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var admission = Service(fixture);
        await Admit(fixture, admission);
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
        var session = fixture.Request.SessionId;
        var listed = await management.ExecuteAsync(new(MemoryCommandOperation.List, session.Value),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        var row = listed.Memories.Single();
        await management.ExecuteAsync(new(MemoryCommandOperation.Inspect, session.Value, row.Id, row.Revision),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        var ended = await sessions.ChangeLifecycleAsync(session, new(1), false, RequestOrigin.LocalUi, fixture.Token);
        var list = () => management.ExecuteAsync(new(MemoryCommandOperation.List, session.Value),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await list.Should().ThrowAsync<InvalidOperationException>();
        await sessions.ChangeLifecycleAsync(session, ended.Generation, true, RequestOrigin.LocalUi, fixture.Token);
        listed = await list();
        listed.Memories.Single().Retention.Should().Be(MemoryRetentionState.Forgotten);
        listed.Inspected.Should().BeNull();
        row = listed.Memories.Single();
        var inspected = await management.ExecuteAsync(new(MemoryCommandOperation.Inspect, session.Value, row.Id, row.Revision),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        inspected.Inspected!.Candidate.Should().BeNull();
        inspected.Inspected.Receipt.Should().BeNull();
    }

    [WindowsFact]
    public async Task NativeManagementRoundTripsExactReviewedTransitionsThroughTheAuthoritativeStore()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var admission = Service(fixture);
        var original = await Admit(fixture, admission);
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
        var session = fixture.Request.SessionId;
        async Task<MemoryCommandResult> Execute(MemoryCommandOperation operation, MemorySummary? row = null) =>
            await management.ExecuteAsync(new(operation, session.Value, row?.Id, row?.Revision ?? 0,
                Accept: true, Candidate: Candidate), RequestOrigin.LocalUi, () => true, fixture.Token);
        var listed = await Execute(MemoryCommandOperation.List);
        Encoding.UTF8.GetString(MemoryCommandResult.Serialize(listed)).Should().NotContain("exact reviewed memory");
        var row = listed.Memories.Single();
        row.Id.Should().Be(original.Id.Value);
        (await Execute(MemoryCommandOperation.Inspect, row)).Inspected.Should().Be(original);
        row = (await Execute(MemoryCommandOperation.Disable, row)).Memories.Single();
        row.Retention.Should().Be(MemoryRetentionState.Disabled);
        row = (await Execute(MemoryCommandOperation.Edit, row)).Memories.Single();
        row.Review.Should().Be(MemoryReviewState.Proposed);
        Payload(fixture).Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null");
        await Execute(MemoryCommandOperation.Inspect, row);
        row = (await Execute(MemoryCommandOperation.Review, row)).Memories.Single();
        row.Review.Should().Be(MemoryReviewState.Reviewed);
        Payload(fixture).Should().Contain("\"Candidate\":null");
        row = (await Execute(MemoryCommandOperation.Admit, row)).Memories.Single();
        row.Retention.Should().Be(MemoryRetentionState.Enabled);
        row = (await Execute(MemoryCommandOperation.Forget, row)).Memories.Single();
        row.Retention.Should().Be(MemoryRetentionState.Forgotten);
        (await Execute(MemoryCommandOperation.Edit, row)).Outcome.Should().Be("InvalidTransition");
        Payload(fixture).Should().Contain("\"Candidate\":null").And.Contain("\"Receipt\":null");
        AssertContentFreeAudit(fixture);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        using var restarted = Service(fixture);
        (await Run(fixture, () => restarted.UseAsync(original.Id, new(row.Revision), MemoryDestination.Local, fixture.Token)))
            .Outcome.Should().NotBe(MemoryOutcome.Succeeded);
    }

    [Theory]
    [InlineData("UPDATE reviewed_memory SET payload='malformed';")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Retention',999);")]
    [InlineData("UPDATE reviewed_memory SET payload=json_set(payload,'$.Candidate.Value','tampered');")]
    [InlineData("DELETE FROM reviewed_memory;")]
    [InlineData("DELETE FROM memory_profile;")]
    public async Task NativeManagementRejectsCorruptPersistedStateWithoutCachedContentFallback(string sql)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        using var admission = Service(fixture);
        await Admit(fixture, admission);
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
        var session = fixture.Request.SessionId;
        var listed = await management.ExecuteAsync(new(MemoryCommandOperation.List, session.Value),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        var row = listed.Memories.Single();
        fixture.Mutate(sql);
        var inspect = () => management.ExecuteAsync(new(MemoryCommandOperation.Inspect, session.Value, row.Id, row.Revision),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await inspect.Should().ThrowAsync<InvalidDataException>();
    }
}
