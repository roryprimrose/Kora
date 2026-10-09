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
    public async Task NativeNewMemoryIsUsableWithoutExistingIdentityAndPersistsOnlyAfterInspectReviewAndAdmit()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
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
            viewer.CanProposeMemory.Should().BeTrue();
            viewer.CanInspectMemory.Should().BeFalse();
            viewer.MemoryClass = Candidate.ContentClass;
            viewer.MemoryDraft = Candidate.Value!;
            await viewer.ProposeMemoryAsync();
            viewer.Status.Should().Contain("Succeeded");
            viewer.MemoryDraft.Should().BeEmpty();
            viewer.MemoryDetail.Should().NotContain("exact reviewed memory");
            viewer.SelectedMemory.Should().BeNull();
            viewer.CanReviewMemory.Should().BeFalse();
            var row = viewer.MemoryRecords.Single();
            row.Review.Should().Be(MemoryReviewState.Proposed);
            row.Retention.Should().Be(MemoryRetentionState.Pending);
            fixture.Count("reviewed_memory").Should().Be(0);
            AssertDatabaseDoesNotContainProposal(fixture);
            await viewer.ListMemoriesAsync();
            viewer.MemoryRecords.Should().Equal(row);
            viewer.SelectMemory(row);
            await viewer.InspectMemoryAsync();
            viewer.MemoryDraft.Should().Be(Candidate.Value);
            viewer.CanReviewMemory.Should().BeTrue();
            await viewer.ReviewMemoryAsync(true);
            viewer.MemoryRecords.Single().Review.Should().Be(MemoryReviewState.Reviewed);
            fixture.Count("reviewed_memory").Should().Be(0);
            AssertDatabaseDoesNotContainProposal(fixture);
            await viewer.AdmitMemoryAsync();
            viewer.MemoryRecords.Single().Retention.Should().Be(MemoryRetentionState.Enabled);
            Payload(fixture).Should().Contain("exact reviewed memory");
            AssertContentFreeAudit(fixture);
        }
        finally { viewer.Close(); }
    }

    [Theory]
    [InlineData("close")]
    [InlineData("selection")]
    [InlineData("privacy")]
    [InlineData("restart")]
    public async Task NativeDraftAndUnadmittedProposalAreDiscardedAtPresentationBoundaries(string boundary)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(sessions,
            new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
                NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance, memories: management);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            viewer.MemoryDraft = Candidate.Value!;
            await viewer.ProposeMemoryAsync();
            viewer.MemoryRecords.Should().HaveCount(1);
            viewer.MemoryDraft = "discard this unsent draft";
            switch (boundary)
            {
                case "close": viewer.Close(); break;
                case "selection": await viewer.SelectAsync(null); break;
                case "privacy": access.CanControl = false; await viewer.ListMemoriesAsync(); access.CanControl = true; break;
                case "restart": viewer.Close(); fixture.Reopen(); await fixture.Store.InitializeAsync(fixture.Token); break;
            }
            viewer.MemoryDraft.Should().BeEmpty();
            viewer.MemoryRecords.Should().BeEmpty();
            viewer.MemoryDraft = "late callback must not restore body";
            viewer.MemoryDraft.Should().BeEmpty();
            await using var restarted = new MemoryManagementService(sessions, fixture.Store, access, access,
                fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
            var target = boundary is "restart" ? restarted : management;
            var result = await target.ExecuteAsync(new(MemoryCommandOperation.List, fixture.Request.SessionId.Value),
                RequestOrigin.LocalUi, () => true, fixture.Token);
            result.Memories.Should().BeEmpty();
            AssertDatabaseDoesNotContainProposal(fixture);
        }
        finally { viewer.Close(); }
    }

    [WindowsFact]
    public async Task ExactGrammarCreatesThenInspectsReviewsAndAdmitsThroughTheSamePrivateStore()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var access = new Access();
        var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance);
        await using var management = new MemoryManagementService(sessions, fixture.Store, access, access,
            fixture.Store, new Audit(), NullLogger<MemoryAdmissionService>.Instance, fixture.Time);
        var session = fixture.Request.SessionId.Value.ToString("D");
        async Task<MemoryCommandResult> Execute(string text) => await management.ExecuteAsync(
            MemoryCommand.Parse("Kora, memory " + text, "Kora")!, RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        var row = (await Execute("propose " + session + " ResponsePreference \"exact reviewed memory café\"")).Memories.Single();
        AssertDatabaseDoesNotContainProposal(fixture);
        string Selector() => session + " " + row.Id.ToString("D") + " " + row.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        (await Execute("inspect " + Selector())).Inspected!.Candidate.Should().Be(Candidate);
        row = (await Execute("review " + Selector() + " accept")).Memories.Single();
        AssertDatabaseDoesNotContainProposal(fixture);
        row = (await Execute("admit " + Selector())).Memories.Single();
        row.Retention.Should().Be(MemoryRetentionState.Enabled);
        Payload(fixture).Should().Contain("exact reviewed memory");
        AssertContentFreeAudit(fixture);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("terminal")]
    [InlineData("foreign")]
    public async Task ActualStoreRejectsProposalWithoutFreshExactOriginalIntent(string failure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await FinishIntent(fixture);
        if (failure is "missing")
        {
            fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        }
        if (failure is "foreign")
        {
            var access = new Access();
            var sessions = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
                NullLogger<SessionWorkspaceService>.Instance);
            var other = await sessions.CreateAsync(new("Foreign exact session"), RequestOrigin.LocalUi, fixture.Token);
            fixture.Request = new(fixture.Request.RequestId, other.Authority.SessionId, fixture.Request.TaskId, fixture.Request.Origin);
        }
        using var service = Service(fixture);
        var act = () => fixture.RunAsync(() => new ValueTask<MemoryResult>(service.ProposeAsync(Candidate,
            MemoryScope.Session(fixture.Request.SessionId), MemoryProposalOrigin.User, fixture.Token)));
        await act.Should().ThrowAsync<InvalidDataException>();
        fixture.Count("reviewed_memory").Should().Be(0);
        AssertDatabaseDoesNotContainProposal(fixture);
    }

    private static void AssertDatabaseDoesNotContainProposal(InteractionStorageFixture fixture)
    {
        fixture.Count("reviewed_memory").Should().Be(0);
        foreach (var path in new[] { fixture.DatabasePath, fixture.DatabasePath + "-journal" })
        {
            if (File.Exists(path))
            {
                Encoding.UTF8.GetString(File.ReadAllBytes(path)).Should().NotContain("exact reviewed memory")
                    .And.NotContain("discard this unsent draft");
            }
        }
    }
}
