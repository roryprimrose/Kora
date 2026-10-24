using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsViewModelTests
{
    [Fact]
    public async Task PageOrderingCannotResurrectRowsAfterAnExactDispositionRetiresTheRawPage()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        var created = await service.CreateAsync(new("owned"), RequestOrigin.LocalUi, f.Token);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.ListSearchKind = SessionListSearchKind.Browse;
            await model.SearchListAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority == created.Authority));
            await model.PreviewDispositionAsync();
            model.CanConfirmDisposition.Should().BeTrue();
            var eligibility = model.CaptureNavigationEligibility();
            model.ListPageOrder = SessionListPageOrder.NameDescending;
            model.CanConfirmDisposition.Should().BeTrue("presentation order retains the exact review, not a row index");
            eligibility().Should().BeTrue();
            await model.ConfirmDispositionAsync();
            model.Status.Should().Contain("Committed logical disposition");
            model.Sessions.Should().BeEmpty();
            model.SelectedSessionRecord.Should().BeNull();
            model.CanOrderListPage.Should().BeFalse();
            model.ClearListPageOrder();
            model.Sessions.Should().BeEmpty();
            eligibility().Should().BeFalse();
        }
        finally { model.Close(); }
    }

    [Fact]
    public async Task NextUsesOriginalSemanticCursorAndEachNewPageUsesTheVolatileOrderUntilOrdinaryRefresh()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance);
        for (var index = 0; index < 27; index++)
        { await service.CreateAsync(new("same"), RequestOrigin.LocalUi, f.Token); }
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service,
            new(new WindowsSqliteEvidenceReader(sink), access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
            access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            model.ListSearchKind = SessionListSearchKind.Browse;
            using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
            var first = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.All, "", null, 25, f.Token);
            var second = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.All, "", first.Next, 25, f.Token);
            var originalBytes = SessionListSearchPage.Serialize(first);
            var originalNext = first.Next;
            await model.SearchListAsync();
            model.Sessions.Should().Equal(first.Records);
            model.ListPageOrder = SessionListPageOrder.NameDescending;
            model.Sessions.Should().Equal(SessionListPageOrdering.Apply(first.Records, model.ListPageOrder));
            model.CanNextListSearch.Should().BeTrue();
            await model.SearchListAsync(next: true);
            model.Sessions.Should().Equal(SessionListPageOrdering.Apply(second.Records, model.ListPageOrder));
            model.CanNextListSearch.Should().BeFalse();
            first.Next.Should().BeSameAs(originalNext);
            SessionListSearchPage.Serialize(first).Should().Equal(originalBytes);
            await model.SearchListAsync();
            model.Sessions.Should().Equal(SessionListPageOrdering.Apply(first.Records, model.ListPageOrder));
            await model.RefreshAsync();
            model.Sessions.Should().Equal(first.Records);
            model.CanOrderListPage.Should().BeFalse();
            model.ListPageOrder.Should().Be(SessionListPageOrder.NameDescending, "retiring search does not reset the volatile choice");
            model.ClearListPageOrder();
            model.ListPageOrder.Should().Be(SessionListPageOrder.NameDescending, "the order control is explicitly limited to paged search");
        }
        finally { model.Close(); }
    }

    [Theory]
    [InlineData("query")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("clear-search")]
    public async Task PageOrderingAndClearAreReadFreeAndNeverRepublishRetiredSource(string retirement)
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
        try
        {
            model.ListPageOrder.Should().Be(SessionListPageOrder.Source);
            model.CanOrderListPage.Should().BeFalse();
            model.ListPageOrder = SessionListPageOrder.NameDescending;
            model.ClearListPageOrder();
            held.Reads.Should().Be(0);
            model.ListSearchKind = SessionListSearchKind.Browse;
            var first = new SessionWorkspaceEntry(new(new(Guid.Parse("00000000-0000-0000-0000-000000000001")), new(1), true),
                null);
            var second = new SessionWorkspaceEntry(new(new(Guid.Parse("00000000-0000-0000-0000-000000000002")), new(2), true),
                null);
            ImmutableArray<SessionWorkspaceEntry> raw = [first, second];
            var search = model.SearchListAsync();
            model.ListPageOrder = SessionListPageOrder.NameDescending;
            model.ListPageOrder.Should().Be(SessionListPageOrder.Source, "pending reads cannot be ordered");
            held.Completion.SetResult(new(raw, second.Authority.SessionId.Value));
            await search;
            model.Sessions.Should().Equal(raw);
            model.CanNextListSearch.Should().BeTrue();
            foreach (var order in Enum.GetValues<SessionListPageOrder>())
            {
                model.ListPageOrder = order;
                model.ListPageOrder.Should().Be(order);
                model.Sessions.Should().Equal(SessionListPageOrdering.Apply(raw, order));
                model.CanNextListSearch.Should().BeTrue();
                held.Reads.Should().Be(1);
                held.ControlCalls.Should().Be(0);
            }
            model.ClearListPageOrder();
            model.Sessions.Should().Equal(raw);
            model.SelectedSessionRecord.Should().BeNull();
            if (retirement is "query") { model.ListQuery = "changed query"; }
            if (retirement is "source") { model.RevokeSessionList(); }
            if (retirement is "privacy") { access.CanInspect = false; }
            if (retirement is "control") { access.ControlRevision++; }
            if (retirement is "close") { model.Close(); }
            if (retirement is "clear-search") { model.ClearListSearch(); }
            model.CanOrderListPage.Should().BeFalse();
            model.ListPageOrder = SessionListPageOrder.NameAscending;
            model.ListPageOrder.Should().Be(SessionListPageOrder.Source);
            held.Reads.Should().Be(1);
            held.ControlCalls.Should().Be(0);
        }
        finally { model.Close(); }
    }
}
