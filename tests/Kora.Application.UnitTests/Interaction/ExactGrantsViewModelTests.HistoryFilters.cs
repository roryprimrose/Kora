using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task TypedHistoryFiltersArePassiveAndExplicitReadCarriesCurrentExactAdmissionWithoutMutation()
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        ExactGrantsViewModel.HistoryOutcomeFilters.Should().Equal("Any", "Requested", "Succeeded", "Failed", "Denied", "Cancelled", "Unknown");
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.Store.HistoryResult = History(f, "next");
        await f.State.ReadHistoryAsync(f.Token);
        var oldAdmission = f.Store.HistoryAdmissionId;
        f.State.HistoryOutcomeFilter = "Succeeded";
        f.State.HistoryReasonCodeFilter = "exact-private-reason";
        AssertHistoryFilterRetired(f);
        f.Store.HistoryReads.Should().Be(1);
        f.State.CanReadHistory.Should().BeTrue();
        f.Store.HistoryResult = History(f) with { Records = [] };
        await f.State.ReadHistoryCommand.ExecuteAsync();
        f.Store.HistoryQuery.Should().Be(new ExactGrantHistoryQuery(SecurityAuditOutcome.Succeeded, "exact-private-reason"));
        f.Store.HistoryControlRevision.Should().Be(f.Access.ControlRevision);
        f.Store.HistoryAdmissionId.Should().NotBe(oldAdmission).And.NotBe(Guid.Empty);
        f.Store.HistoryCursor.Should().BeNull();
        f.State.HistorySummary.Should().Contain("nonmatching scanned rows");
        logger.Last.Should().BeNull();
        f.Store.Audit.Should().BeNull();
        f.Store.RevokeRequest.Should().BeNull();
        f.Intents.Tasks.Should().BeEmpty();
        HostActivity.Current.Should().BeNull();
        f.State.HistoryOutcomeFilter = f.State.HistoryOutcomeFilter;
        f.State.HistoryReasonCodeFilter = f.State.HistoryReasonCodeFilter;
        await f.State.ClearHistoryFiltersCommand.ExecuteAsync();
        f.State.HistoryOutcomeFilter.Should().Be("Any");
        f.State.HistoryReasonCodeFilter.Should().BeEmpty();
        await f.State.ReadHistoryAsync(f.Token);
        f.Store.HistoryQuery.Should().Be(ExactGrantHistoryQuery.Unfiltered);
        f.Store.Revokes.Should().Be(0);
        f.State.Dispose();
        f.State.HistoryOutcomeFilter.Should().Be("Any");
        f.State.HistoryReasonCodeFilter.Should().BeEmpty();
        f.State.ClearHistoryFiltersCommand.CanExecute(null).Should().BeFalse();
        var outcome = () => f.State.HistoryOutcomeFilter = "Failed";
        var reason = () => f.State.HistoryReasonCodeFilter = "late";
        var clear = f.State.ClearHistoryFilters;
        outcome.Should().Throw<ObjectDisposedException>();
        reason.Should().Throw<ObjectDisposedException>();
        clear.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ChangingHistoryFiltersWithoutInspectionCannotReadOrReconstructARecord()
    {
        await using var f = new Fixture();
        f.State.ClearHistoryFilters();
        f.State.CanReadHistory.Should().BeFalse();
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.Store.HistoryReads.Should().Be(0);
    }

    [Theory]
    [InlineData("invalid", "")]
    [InlineData("Any", "Private Query")]
    [InlineData("1", "")]
    public async Task InvalidHistoryInputHasExplicitPrivateFreeFailureWithoutStorageFallback(string outcome, string reason)
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        await f.InspectAsync();
        f.State.HistoryOutcomeFilter = outcome;
        f.State.HistoryReasonCodeFilter = reason;
        f.State.ReadHistoryCommand.Execute(null);
        f.Store.HistoryReads.Should().Be(0);
        f.State.Message.Should().Contain("unavailable").And.NotContain("Private Query");
        logger.Last.Should().NotBeNull().And.NotContain("Private Query");
        f.State.CanReadHistory.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryFilterEditCancelsHeldReadAndSuppressesLateRowsOrPrivateFailures(bool fails)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var pending = new TaskCompletionSource<ExactGrantHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingHistory = pending.Task;
        var read = f.State.ReadHistoryCommand.ExecuteAsync();
        var admission = f.Store.HistoryAdmission!;
        f.State.HistoryReasonCodeFilter = "policy-denied";
        f.Store.HistoryToken.IsCancellationRequested.Should().BeTrue();
        admission().Should().BeFalse();
        AssertHistoryFilterRetired(f);
        if (fails) { pending.SetException(new IOException("private old query")); }
        else { pending.SetResult(History(f, "old")); }
        await read;
        AssertHistoryFilterRetired(f);
        f.Store.PendingHistory = null;
        f.Store.HistoryResult = History(f, "new") with { Records = [], NonmatchingRows = 4096 };
        await f.State.ReadHistoryAsync(f.Token);
        f.State.NextHistoryCommand.CanExecute(null).Should().BeTrue();
        f.State.HistorySummary.Should().Contain("nonmatching scanned rows: 4096").And.Contain("zero-match");
        var viewer = f.Store.HistoryViewer;
        var id = f.Store.HistoryAdmissionId;
        await f.State.NextHistoryAsync(f.Token);
        f.Store.HistoryViewer.Should().BeSameAs(viewer);
        f.Store.HistoryAdmissionId.Should().Be(id);
        f.Store.HistoryCursor.Should().Be("new");
        f.State.ClearHistoryFilters();
        var next = () => f.State.NextHistoryAsync(f.Token);
        await next.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FilterChangesRetireCompleteBatchAndSingleReviewAndCannotGrantNewAuthority()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.State.HistoryOutcomeFilter = "Denied";
        AssertHistoryFilterRetired(f);
        await f.InspectAsync();
        f.State.SetBatchSelection([f.Preview]);
        await f.State.PreviewBatchAsync(f.Token);
        f.State.BatchReviewed = true;
        f.State.HistoryReasonCodeFilter = "reason";
        AssertHistoryFilterRetired(f);
        var batch = () => f.State.PreviewBatchAsync(f.Token);
        await batch.Should().ThrowAsync<InvalidDataException>();
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ReaderCannotReturnNonmatchesOrInvalidScanCountsUnderTypedQuery(int invalid)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.HistoryOutcomeFilter = "Denied";
        var page = History(f);
        f.Store.HistoryResult = invalid switch
        {
            0 => page,
            1 => page with { Records = [], ScannedRows = -1 },
            2 => page with { Records = [], ScannedRows = 4097 },
            3 => page with { Records = [], NonmatchingRows = -1 },
            _ => page with { Records = [], NonmatchingRows = 4097 },
        };
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanRevoke.Should().BeFalse();
    }

    private static void AssertHistoryFilterRetired(Fixture f)
    {
        f.State.HistoryRows.Should().BeEmpty();
        f.State.NextHistoryCommand.CanExecute(null).Should().BeFalse();
        f.State.Selected.Should().BeNull();
        f.State.Records.Should().BeEmpty();
        f.State.Reviewed.Should().BeFalse();
        f.State.BatchReviewed.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        f.State.CanRevokeBatch.Should().BeFalse();
        f.State.Message.Should().Contain("History filters changed");
    }
}
