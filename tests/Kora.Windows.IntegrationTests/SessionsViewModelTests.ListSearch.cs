using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("kind")]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("revision")]
    public async Task MetadataSearchSuppressesLateResultsAfterQuerySelectionSourcePrivacyOrClosureChanges(string mode)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access { CanControl = false };
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        model.ListQuery = "needle";
        model.CanSearchList.Should().BeTrue();
        var search = model.SearchListAsync();
        model.CanCancelListSearch.Should().BeTrue();
        model.CanSearchList.Should().BeFalse();
        model.CanEditListSearch.Should().BeTrue();
        if (mode is "query") { model.ListQuery = "another"; }
        if (mode is "kind") { model.ListSearchKind = SessionListSearchKind.ExactId; }
        if (mode is "selection") { await model.SelectAsync(new(new(new(Guid.NewGuid()), new(1), true), null)); }
        if (mode is "source") { model.RevokeSessionList(); }
        if (mode is "cancel") { model.CancelListSearch(); }
        if (mode is "close") { model.Close(); }
        if (mode is "privacy") { access.CanInspect = false; }
        if (mode is "revision") { access.ControlRevision++; }
        var entry = new SessionWorkspaceEntry(new(f.Request.SessionId, new(1), true),
            new(f.Request.SessionId, new(1), new("needle")));
        held.Completion.SetResult(new([entry], null));
        await search;
        model.Sessions.Should().BeEmpty();
        model.Detail.Should().BeEmpty();
        model.SelectedSessionRecord.Should().BeNull();
        model.CanCancelListSearch.Should().BeFalse();
        model.CanNextListSearch.Should().BeFalse();
        model.WorkRecords.Should().BeEmpty();
        model.PendingQuestions.Should().BeEmpty();
        model.MemoryRecords.Should().BeEmpty();
        held.ControlCalls.Should().Be(0);
        held.Reads.Should().Be(1);
        model.Close();
        model.ListQuery.Should().BeEmpty();
    }
}
