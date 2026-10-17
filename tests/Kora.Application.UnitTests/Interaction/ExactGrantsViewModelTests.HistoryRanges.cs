using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task RetentionAndTimeInputsArePassiveComposedClearedAndDisposedWithoutQueryDisclosure()
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        ExactGrantsViewModel.HistoryRetentionFilters.Should().Equal(ExactGrantHistoryQuery.RetentionOptions);
        await f.InspectAsync();
        var target = f.Preview;
        f.State.Reviewed = true;
        var page = History(f, "cursor");
        f.Store.HistoryResult = page;
        await f.State.ReadHistoryAsync(f.Token);
        var admission = f.Store.HistoryAdmissionId;
        var expected = new ExactGrantHistoryQuery(retention: EvidenceSegmentStatus.ExpiredButPresent,
            committedFromUtc: DateTimeOffset.UnixEpoch, committedUntilUtc: DateTimeOffset.UnixEpoch,
            dueFromUtc: DateTimeOffset.UnixEpoch.AddDays(1), dueUntilUtc: DateTimeOffset.UnixEpoch.AddDays(1));
        var edits = RangeEdits(f);
        foreach (var edit in edits)
        {
            edit();
            edit();
            AssertHistoryFilterRetired(f);
            f.State.CanReadHistory.Should().BeTrue();
            f.Store.HistoryReads.Should().Be(1);
        }
        await f.State.ReadHistoryAsync(f.Token);
        f.Store.HistoryQuery.Should().Be(expected);
        f.Store.HistoryExact.Should().BeSameAs(target);
        f.Store.HistoryAdmissionId.Should().NotBe(admission);
        f.State.CanRevoke.Should().BeFalse();
        f.State.HistoryRows.Should().HaveCount(2);
        logger.Last.Should().BeNull();
        f.Store.Audit.Should().BeNull();
        f.Store.Revokes.Should().Be(0);
        f.Intents.Tasks.Should().BeEmpty();
        await f.State.ClearHistoryFiltersCommand.ExecuteAsync();
        AssertEmptyRangeInputs(f);
        f.Store.HistoryReads.Should().Be(2);
        await f.State.ReadHistoryAsync(f.Token);
        f.Store.HistoryQuery.Should().Be(ExactGrantHistoryQuery.Unfiltered);
        foreach (var edit in edits) { edit(); }
        f.State.Dispose();
        AssertEmptyRangeInputs(f);
        foreach (var edit in edits) { edit.Should().Throw<ObjectDisposedException>(); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public async Task EachRangeOrRetentionEditCancelsAndSuppressesStaleRowsAndPrivateFailures(int field, bool fails)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var pending = new TaskCompletionSource<ExactGrantHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingHistory = pending.Task;
        var read = f.State.ReadHistoryCommand.ExecuteAsync();
        var admission = f.Store.HistoryAdmission!;
        RangeEdits(f)[field]();
        f.Store.HistoryToken.IsCancellationRequested.Should().BeTrue();
        admission().Should().BeFalse();
        if (fails) { pending.SetException(new IOException("private old range query")); }
        else { pending.SetResult(History(f, "old")); }
        await read;
        AssertHistoryFilterRetired(f);
        f.State.CanReadHistory.Should().BeTrue();
        f.State.Message.Should().NotContain("private old range query");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task InvalidInputsFailBeforeStorageAndNonmatchingReaderPagesAreRejected(int field)
    {
        await using var f = new Fixture(new EnabledLogger());
        await f.InspectAsync();
        if (field == 0) { f.State.HistoryRetentionFilter = "Unavailable"; }
        if (field == 1) { f.State.HistoryCommittedFromUtc = "private-invalid"; }
        if (field == 2) { f.State.HistoryCommittedUntilUtc = "private-invalid"; }
        if (field == 3) { f.State.HistoryDueFromUtc = "private-invalid"; }
        if (field == 4) { f.State.HistoryDueUntilUtc = "private-invalid"; }
        f.State.ReadHistoryCommand.Execute(null);
        f.Store.HistoryReads.Should().Be(0);
        f.State.Message.Should().Contain("unavailable").And.NotContain("private-invalid");
        f.State.CanReadHistory.Should().BeFalse();
        f.State.ClearHistoryFilters();
        await f.InspectAsync();
        if (field == 0) { f.State.HistoryRetentionFilter = "Present"; }
        if (field == 1) { f.State.HistoryCommittedFromUtc = "1970-01-01T00:00:00.0000001+00:00"; }
        if (field == 2) { f.State.HistoryCommittedUntilUtc = "1969-12-31T23:59:59.9999999+00:00"; }
        if (field == 3) { f.State.HistoryDueFromUtc = "1970-01-02T00:00:00.0000001+00:00"; }
        if (field == 4) { f.State.HistoryDueUntilUtc = "1970-01-01T23:59:59.9999999+00:00"; }
        f.Store.HistoryResult = History(f);
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanRevoke.Should().BeFalse();
    }

    private static Action[] RangeEdits(Fixture f) =>
    [
        () => f.State.HistoryRetentionFilter = "ExpiredButPresent",
        () => f.State.HistoryCommittedFromUtc = "1970-01-01T00:00:00.0000000+00:00",
        () => f.State.HistoryCommittedUntilUtc = "1970-01-01T00:00:00.0000000+00:00",
        () => f.State.HistoryDueFromUtc = "1970-01-02T00:00:00.0000000+00:00",
        () => f.State.HistoryDueUntilUtc = "1970-01-02T00:00:00.0000000+00:00",
    ];

    private static void AssertEmptyRangeInputs(Fixture f)
    {
        f.State.HistoryRetentionFilter.Should().Be("Any");
        f.State.HistoryCommittedFromUtc.Should().BeEmpty();
        f.State.HistoryCommittedUntilUtc.Should().BeEmpty();
        f.State.HistoryDueFromUtc.Should().BeEmpty();
        f.State.HistoryDueUntilUtc.Should().BeEmpty();
    }
}
