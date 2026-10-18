using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task CollateralInputsAreReadFreePreserveOnlyInertTargetAndClearOnDisposal()
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        await f.InspectAsync();
        var target = f.Preview;
        f.Store.HistoryResult = History(f, "next");
        await f.State.ReadHistoryAsync(f.Token);
        var admission = f.Store.HistoryAdmissionId;
        var notifications = new List<string?>();
        f.State.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        foreach (var edit in CollateralEdits(f))
        {
            edit();
            edit();
            AssertHistoryFilterRetired(f);
            f.State.CanReadHistory.Should().BeTrue();
            f.Store.HistoryReads.Should().Be(1);
        }
        await f.State.ReadHistoryAsync(f.Token);
        f.Store.HistoryExact.Should().BeSameAs(target);
        f.Store.HistoryAdmissionId.Should().NotBe(admission);
        f.Store.HistoryQuery.Should().Be(new ExactGrantHistoryQuery(minimumOtherChanges: 0, maximumOtherChanges: int.MaxValue,
            minimumOtherRequestedGrants: 0, maximumOtherRequestedGrants: int.MaxValue));
        f.State.CanRevoke.Should().BeFalse();
        f.Store.Audit.Should().BeNull();
        f.Store.Revokes.Should().Be(0);
        f.Intents.Tasks.Should().BeEmpty();
        logger.Last.Should().BeNull();
        await f.State.ClearHistoryFiltersCommand.ExecuteAsync();
        AssertEmptyCollateral(f);
        AssertHistoryFilterRetired(f);
        f.Store.HistoryReads.Should().Be(2);
        await f.State.ReadHistoryAsync(f.Token);
        f.Store.HistoryQuery.Should().Be(ExactGrantHistoryQuery.Unfiltered);
        foreach (var edit in CollateralEdits(f)) { edit(); }
        f.State.Dispose();
        AssertEmptyCollateral(f);
        notifications.Should().Contain(nameof(ExactGrantsViewModel.HistoryMinimumOtherChanges))
            .And.Contain(nameof(ExactGrantsViewModel.HistoryMaximumOtherChanges))
            .And.Contain(nameof(ExactGrantsViewModel.HistoryMinimumOtherRequestedGrants))
            .And.Contain(nameof(ExactGrantsViewModel.HistoryMaximumOtherRequestedGrants));
        foreach (var edit in CollateralEdits(f)) { edit.Should().Throw<ObjectDisposedException>(); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task EachCollateralEditCancelsAndSuppressesLateRowsAndPrivateErrors(int field, bool fails)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var pending = new TaskCompletionSource<ExactGrantHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingHistory = pending.Task;
        var read = f.State.ReadHistoryCommand.ExecuteAsync();
        var admitted = f.Store.HistoryAdmission!;
        CollateralEdits(f)[field]();
        f.Store.HistoryToken.IsCancellationRequested.Should().BeTrue();
        admitted().Should().BeFalse();
        if (fails) { pending.SetException(new IOException("private collateral query")); }
        else { pending.SetResult(History(f, "stale")); }
        await read;
        AssertHistoryFilterRetired(f);
        f.State.Message.Should().NotContain("private collateral query");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InvalidCollateralInputNeverReadsAndHostileOrNonmatchingPageNeverPublishes(int field)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        EditCollateral(f.State, field, "2147483648");
        f.State.ReadHistoryCommand.Execute(null);
        f.Store.HistoryReads.Should().Be(0);
        f.State.Message.Should().NotContain("2147483648");
        f.State.ClearHistoryFilters();
        await f.InspectAsync();
        var page = History(f);
        f.Store.HistoryResult = page with { Records = [page.Records[0] with { OtherChanges = -1 }] };
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        f.State.HistoryRows.Should().BeEmpty();
        await f.InspectAsync();
        EditCollateral(f.State, field, field % 2 == 0 ? "1" : "0");
        var row = page.Records[0] with { OtherChanges = field == 1 ? 1 : 0, OtherRequestedGrants = field == 3 ? 1 : 0 };
        f.Store.HistoryResult = page with { Records = [row] };
        await read.Should().ThrowAsync<InvalidDataException>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanRevoke.Should().BeFalse();
    }

    private static Action[] CollateralEdits(Fixture f) =>
    [
        () => EditCollateral(f.State, 0, "0"),
        () => EditCollateral(f.State, 1, "2147483647"),
        () => EditCollateral(f.State, 2, "0"),
        () => EditCollateral(f.State, 3, "2147483647"),
    ];

    private static void EditCollateral(ExactGrantsViewModel state, int field, string value)
    {
        switch (field)
        {
            case 0: state.HistoryMinimumOtherChanges = value; break;
            case 1: state.HistoryMaximumOtherChanges = value; break;
            case 2: state.HistoryMinimumOtherRequestedGrants = value; break;
            case 3: state.HistoryMaximumOtherRequestedGrants = value; break;
        }
    }

    private static void AssertEmptyCollateral(Fixture f)
    {
        f.State.HistoryMinimumOtherChanges.Should().BeEmpty();
        f.State.HistoryMaximumOtherChanges.Should().BeEmpty();
        f.State.HistoryMinimumOtherRequestedGrants.Should().BeEmpty();
        f.State.HistoryMaximumOtherRequestedGrants.Should().BeEmpty();
    }
}
