using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task NativeFiltersSendOneExactQueryAndDefaultClearNeverCreateIntentOrAuthority()
    {
        await using var f = new Fixture();
        ExactGrantsViewModel.ScopeFilters.Should().Equal("Any", "Once", "Session", "Perpetual");
        ExactGrantsViewModel.StatusFilters.Should().Equal("Any", "Active", "Consumed", "Revoked");
        await f.State.RefreshAsync(f.Token);
        f.Store.LastQuery.Should().Be(ExactGrantQuery.Unfiltered);
        f.State.ScopeFilter = "Perpetual";
        f.State.StatusFilter = "Active";
        f.State.ActionIdFilter = "bounded.read";
        f.State.OriginatingSessionFilter = f.Preview.Grant.ApprovedProposal.Request.SessionId.Value.ToString("D");
        f.State.Records.Should().BeEmpty();
        f.State.NextCommand.CanExecute(null).Should().BeFalse();
        await f.State.RefreshCommand.ExecuteAsync();
        f.Store.LastQuery.Should().Be(new ExactGrantQuery(OperationGrantScope.Perpetual, OperationGrantStatus.Active,
            "bounded.read", f.Preview.Grant.ApprovedProposal.Request.SessionId));
        f.Store.LastCursor.Should().BeNull();
        f.State.InventorySummary.Should().Contain("validated stored rows").And.Contain("correlation only")
            .And.Contain("51").And.Contain("64 KiB");
        await f.State.ClearFiltersCommand.ExecuteAsync();
        f.State.ScopeFilter.Should().Be("Any");
        f.State.StatusFilter.Should().Be("Any");
        f.State.ActionIdFilter.Should().BeEmpty();
        f.State.OriginatingSessionFilter.Should().BeEmpty();
        f.State.ClearFilters();
        await f.State.RefreshAsync(f.Token);
        f.Store.LastQuery.Should().Be(ExactGrantQuery.Unfiltered);
        f.Store.Revokes.Should().Be(0);
        f.Store.RevokeRequest.Should().BeNull();
        HostActivity.HasScope.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AnyFilterChangeImmediatelyRetiresSelectionHistorySingleBatchAndConfirmation(int change)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.State.CanReadHistory.Should().BeTrue();
        ChangeFilter(f, change);
        AssertRetired(f);
        await f.State.RefreshAsync(f.Token);
        f.State.SetBatchSelection([f.Preview]);
        await f.State.PreviewBatchAsync(f.Token);
        f.State.BatchReviewed = true;
        f.State.CanRevokeBatch.Should().BeTrue();
        f.State.ClearFilters();
        AssertRetired(f);
        f.State.ActionIdFilter = f.State.ActionIdFilter;
        f.State.ScopeFilter = f.State.ScopeFilter;
        f.State.StatusFilter = f.State.StatusFilter;
        f.State.OriginatingSessionFilter = f.State.OriginatingSessionFilter;
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InvalidFiltersNeverCallStorageOrFallBackToUnfilteredInventory(int invalid)
    {
        await using var f = new Fixture();
        if (invalid == 0) { f.State.ScopeFilter = "unknown"; }
        else if (invalid == 1) { f.State.StatusFilter = "99"; }
        else if (invalid == 2) { f.State.ActionIdFilter = "BOUNDED.read"; }
        else { f.State.OriginatingSessionFilter = "00000000-0000-0000-0000-000000000000"; }
        f.State.RefreshCommand.Execute(null);
        f.Store.Pages.Should().Be(0);
        f.State.Message.Should().Contain("unavailable").And.NotContain("No more records");
        AssertRetired(f);
        f.State.Dispose();
        f.State.ClearFiltersCommand.CanExecute(null).Should().BeFalse();
        var clear = f.State.ClearFilters;
        clear.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilterChangeCancelsPendingPageAndSuppressesOldResultsOrFailure(bool fails)
    {
        await using var f = new Fixture();
        var pending = new TaskCompletionSource<ExactGrantPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingPage = pending.Task;
        var read = f.State.RefreshCommand.ExecuteAsync();
        f.State.ActionIdFilter = "bounded.read";
        f.Store.PageToken.IsCancellationRequested.Should().BeTrue();
        AssertRetired(f);
        if (fails) { pending.SetException(new IOException("old private query failure")); }
        else { pending.SetResult(new([f.Preview], new("store", 1, new string('a', 64), f.Preview.Grant.Id.Value))); }
        await read;
        AssertRetired(f);
        f.State.Message.Should().Contain("Filters changed");
        f.Store.PendingPage = null;
        await f.State.RefreshAsync(f.Token);
        f.State.Records.Should().ContainSingle();
        f.Store.LastQuery!.ActionId.Should().Be("bounded.read");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FilterChangeDuringSingleBatchInspectOrHistorySuppressesLateReviewAndMutation(int operation)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        if (operation == 0)
        {
            f.Store.BeforeRead = f.State.ClearFilters;
            await f.State.InspectAsync(f.Token);
        }
        else if (operation == 1)
        {
            f.State.SetBatchSelection([f.Preview]);
            f.Store.BeforePreview = f.State.ClearFilters;
            await f.State.PreviewBatchAsync(f.Token);
        }
        else if (operation == 2)
        {
            f.State.Reviewed = true;
            f.Store.BeforeRevoke = f.State.ClearFilters;
            await f.State.RevokeAsync(f.Token);
        }
        else if (operation == 3)
        {
            f.State.SetBatchSelection([f.Preview]);
            await f.State.PreviewBatchAsync(f.Token);
            f.State.BatchReviewed = true;
            f.Store.BeforeRevoke = f.State.ClearFilters;
            await f.State.RevokeBatchAsync(f.Token);
        }
        else
        {
            f.Store.BeforeHistory = f.State.ClearFilters;
            await f.State.ReadHistoryAsync(f.Token);
            f.Store.HistoryAdmission!().Should().BeFalse();
        }
        AssertRetired(f);
        f.Store.Revokes.Should().Be(0);
        f.State.Message.Should().Contain("Filters changed");
    }

    [Fact]
    public async Task EmptyMatchingPageStillAllowsNextUnderSameQueryAndFilterEditCannotReuseIt()
    {
        await using var f = new Fixture();
        f.State.ActionIdFilter = "other.read";
        f.Store.Current = null;
        f.Store.Next = new("store", 1, new string('a', 64), f.Preview.Grant.Id.Value);
        await f.State.RefreshAsync(f.Token);
        f.State.Records.Should().BeEmpty();
        f.State.NextCommand.CanExecute(null).Should().BeTrue();
        f.State.InventorySummary.Should().Contain("more stored rows, not necessarily matches");
        var query = f.Store.LastQuery;
        await f.State.NextAsync(f.Token);
        f.Store.LastCursor.Should().Be(f.Store.Next);
        f.Store.LastQuery.Should().Be(query);
        f.State.ActionIdFilter = "bounded.read";
        var next = () => f.State.NextAsync(f.Token);
        await next.Should().ThrowAsync<InvalidOperationException>();
        f.State.Dispose();
        f.State.InventorySummary.Should().Contain("Closed");
        f.State.ActionIdFilter.Should().BeEmpty();
        var lateScope = () => f.State.ScopeFilter = "Once";
        var lateStatus = () => f.State.StatusFilter = "Active";
        var lateAction = () => f.State.ActionIdFilter = "late.private.id";
        var lateOrigin = () => f.State.OriginatingSessionFilter = Guid.NewGuid().ToString("D");
        lateScope.Should().Throw<ObjectDisposedException>();
        lateStatus.Should().Throw<ObjectDisposedException>();
        lateAction.Should().Throw<ObjectDisposedException>();
        lateOrigin.Should().Throw<ObjectDisposedException>();
        f.State.ScopeFilter.Should().Be("Any");
        f.State.StatusFilter.Should().Be("Any");
        f.State.ActionIdFilter.Should().BeEmpty();
        f.State.OriginatingSessionFilter.Should().BeEmpty();
    }

    private static void ChangeFilter(Fixture f, int change)
    {
        if (change == 0) { f.State.ScopeFilter = "Once"; }
        else if (change == 1) { f.State.StatusFilter = "Revoked"; }
        else if (change == 2) { f.State.ActionIdFilter = "bounded.read"; }
        else if (change == 3) { f.State.OriginatingSessionFilter = Guid.NewGuid().ToString("D"); }
        else { f.State.ClearFilters(); }
    }

    private static void AssertRetired(Fixture f)
    {
        f.State.Records.Should().BeEmpty();
        f.State.Selected.Should().BeNull();
        f.State.Reviewed.Should().BeFalse();
        f.State.BatchReviewed.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        f.State.CanRevokeBatch.Should().BeFalse();
        f.State.CanReadHistory.Should().BeFalse();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.Inspection.Should().NotContain(f.Preview.Grant.Id.Value.ToString("D"));
        f.State.PreviewBatchCommand.CanExecute(null).Should().BeFalse();
        f.State.NextCommand.CanExecute(null).Should().BeFalse();
    }
}
