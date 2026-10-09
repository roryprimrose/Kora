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
    [InlineData("subject")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("privacy")]
    public async Task Native_search_suppresses_cancelled_or_late_content_and_has_explicit_recovery(string mode)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var held = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(held, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        model.HistorySessionId = f.Request.SessionId.Value.ToString("D");
        model.HistoryQuery = "needle";
        var search = model.SearchHistoryAsync();
        model.CanCancelHistorySearch.Should().BeTrue();
        model.CanSearchHistory.Should().BeFalse();
        if (mode is "query") { model.HistoryQuery = "another"; }
        if (mode is "subject") { model.HistorySessionId = Guid.NewGuid().ToString("D"); }
        if (mode is "cancel") { model.CancelHistorySearch(); }
        if (mode is "close") { model.Close(); }
        if (mode is "privacy") { access.CanInspect = false; }
        var record = new SessionHistoryEvent(Guid.NewGuid(), f.Request.SessionId, 1, new(1),
            SessionHistoryKind.Question, SessionHistoryAvailability.Available, null, null, null, 1,
            new('a', 64), null, false, "needle", [], null, [], null, null, null, null);
        held.HistoryCompletion.SetResult(new(f.Request.SessionId, new(1), false, 1, [record], null));
        await search;
        model.HistoryRecords.Should().BeEmpty();
        model.Detail.Should().BeEmpty();
        model.CanCancelHistorySearch.Should().BeFalse();
        model.SelectedHistoryRecord.Should().BeNull();
        held.ControlCalls.Should().Be(0);
        model.Close();
    }
}
