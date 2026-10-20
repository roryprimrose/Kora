using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactListPreparationKeepsEveryIndependentFilterIncludingInvalidDraftsWithoutAnyRead(bool invalid)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var store = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.SessionFilter = SessionListFilter.Done;
            model.ListNameAvailability = SessionNameAvailability.Unnamed;
            model.ListMinimumGeneration = invalid ? "invalid untouched" : "2";
            model.ListMaximumGeneration = "9";
            model.ListMinimumMetadataRevision = "3";
            model.ListMaximumMetadataRevision = "7";
            model.ListMinimumNameLength = "4";
            model.ListMaximumNameLength = "8";
            model.NameDraft = "independent unsaved name";
            var focus = 0;
            (await model.PrepareListQueryAsync(f.Request.SessionId, () => true, () => { focus++; return true; })).Should().BeTrue();
            focus.Should().Be(1);
            model.ListQuery.Should().Be(f.Request.SessionId.Value.ToString("D"));
            model.ListSearchKind.Should().Be(SessionListSearchKind.ExactId);
            model.SessionFilter.Should().Be(SessionListFilter.Done);
            model.ListNameAvailability.Should().Be(SessionNameAvailability.Unnamed);
            model.ListMinimumGeneration.Should().Be(invalid ? "invalid untouched" : "2");
            model.ListMaximumGeneration.Should().Be("9");
            model.ListMinimumMetadataRevision.Should().Be("3");
            model.ListMaximumMetadataRevision.Should().Be("7");
            model.ListMinimumNameLength.Should().Be("4");
            model.ListMaximumNameLength.Should().Be("8");
            model.NameDraft.Should().Be("independent unsaved name");
            model.CanSearchList.Should().Be(!invalid);
            model.CanNextListSearch.Should().BeFalse();
            model.CanNext.Should().BeFalse();
            model.SelectedSessionRecord.Should().BeNull();
            model.Status.Should().Contain("No Search has run").And.Contain("AND");
            store.Reads.Should().Be(0);
            store.HistoryReads.Should().Be(0);
            store.ControlCalls.Should().Be(0);
        }
        finally { model.Close(); }
    }

    [Theory]
    [InlineData("query")]
    [InlineData("name")]
    [InlineData("buffer")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("closed")]
    public async Task OriginalNativeListPreparationSourceRejectsInterveningEditsEvenWhenRestored(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(WindowsSqliteSessionWorkspaceTests.Service(f, access),
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            var source = model.CaptureListPreparationEligibility();
            switch (change)
            {
                case "query": model.ListQuery = "edit"; model.ListQuery = string.Empty; break;
                case "name": model.NameDraft = "edit"; model.NameDraft = string.Empty; break;
                case "buffer": model.ComposerBody = "edit"; model.ComposerBody = string.Empty; break;
                case "private": access.CanInspect = false; break;
                case "control": access.ControlRevision++; break;
                case "source": model.RevokeSessionList(); break;
                default: model.Close(); break;
            }
            source().Should().BeFalse();
            (await model.PrepareListQueryAsync(f.Request.SessionId, source, () => throw new InvalidOperationException("No focus"))).Should().BeFalse();
        }
        finally { model.Close(); }
    }

    [Fact]
    public async Task ListPreparationUsesTheSameNoReadInvalidationToRetireOldPageCursorAndLateCallbacks()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var store = new HeldStore();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.ListSearchKind = SessionListSearchKind.Browse;
            var pending = model.SearchListAsync();
            (await model.PrepareListQueryAsync(f.Request.SessionId, () => true, () => true)).Should().BeTrue();
            store.Completion.SetResult(new([new(new(f.Request.SessionId, new(1), true), null)], Guid.NewGuid()));
            await pending;
            model.Sessions.Should().BeEmpty();
            model.ListQuery.Should().Be(f.Request.SessionId.Value.ToString("D"));
            model.CanNextListSearch.Should().BeFalse();
            model.CanNext.Should().BeFalse();
            model.SelectedSessionRecord.Should().BeNull();
            model.Detail.Should().BeEmpty();
            store.Reads.Should().Be(1);
            store.ControlCalls.Should().Be(0);
        }
        finally { model.Close(); }
    }
}
