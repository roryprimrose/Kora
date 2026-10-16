using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task CorrelationEditsArePassiveComposedClearedAndDisposedWithoutAuthorityOrQueryDisclosure()
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        ExactGrantsViewModel.HistoryApprovalFilters.Should().Equal(ExactGrantHistoryQuery.ApprovalOptions);
        await f.InspectAsync();
        var target = f.Preview;
        f.Store.HistoryResult = History(f, "cursor");
        await f.State.ReadHistoryAsync(f.Token);
        var admission = f.Store.HistoryAdmissionId;
        var original = f.Store.HistoryResult.Records[0];
        var id = Guid.NewGuid();
        var row = original with { Audit = new SecurityAuditEvent(original.Audit.CorrelationId, original.Audit.Category,
            original.Audit.ActionId, original.Audit.Outcome, original.Audit.Initiator, original.Audit.TargetId, id) };
        Action[] edits =
        [
            () => f.State.HistoryAuditCorrelationId = row.Audit.CorrelationId.ToString("D"),
            () => f.State.HistoryApprovalFilter = "Exact",
            () => f.State.HistoryApprovalId = id.ToString("D"),
        ];
        foreach (var edit in edits)
        {
            edit();
            edit();
            AssertHistoryFilterRetired(f);
            f.State.CanReadHistory.Should().BeTrue();
            f.Store.HistoryReads.Should().Be(1);
        }
        f.Store.HistoryResult = f.Store.HistoryResult with { Records = [row] };
        await f.State.ReadHistoryAsync(f.Token);
        f.Store.HistoryQuery.Should().Be(new ExactGrantHistoryQuery(auditCorrelationId: row.Audit.CorrelationId,
            approval: ExactGrantHistoryApproval.Exact, approvalId: id));
        f.Store.HistoryExact.Should().BeSameAs(target);
        f.Store.HistoryAdmissionId.Should().NotBe(admission);
        f.State.CanRevoke.Should().BeFalse();
        logger.Last.Should().BeNull();
        f.Store.Audit.Should().BeNull();
        f.Store.Revokes.Should().Be(0);
        f.Intents.Tasks.Should().BeEmpty();
        await f.State.ClearHistoryFiltersCommand.ExecuteAsync();
        AssertEmptyCorrelation(f);
        f.Store.HistoryReads.Should().Be(2);
        foreach (var edit in edits) { edit(); }
        f.State.Dispose();
        AssertEmptyCorrelation(f);
        foreach (var edit in edits) { edit.Should().Throw<ObjectDisposedException>(); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task EachCorrelationEditCancelsAndSuppressesStalePrivateResults(int field, bool fails)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var pending = new TaskCompletionSource<ExactGrantHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingHistory = pending.Task;
        var read = f.State.ReadHistoryCommand.ExecuteAsync();
        var admitted = f.Store.HistoryAdmission!;
        EditCorrelation(f, field, "private-old-query");
        f.Store.HistoryToken.IsCancellationRequested.Should().BeTrue();
        admitted().Should().BeFalse();
        if (fails) { pending.SetException(new IOException("private-old-query")); }
        else { pending.SetResult(History(f, "old")); }
        await read;
        AssertHistoryFilterRetired(f);
        f.State.CanReadHistory.Should().BeTrue();
        f.State.Message.Should().NotContain("private-old-query");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidCorrelationAdmissionNeverReadsAndForeignCommittedMetadataFailsClosed(int field)
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        await f.InspectAsync();
        EditCorrelation(f, field, "private-invalid");
        f.State.ReadHistoryCommand.Execute(null);
        f.Store.HistoryReads.Should().Be(0);
        f.State.Message.Should().Contain("unavailable").And.NotContain("private-invalid");
        logger.Last.Should().NotContain("private-invalid");
        f.State.CanReadHistory.Should().BeFalse();
        f.State.ClearHistoryFilters();
        await f.InspectAsync();
        if (field == 0) { f.State.HistoryAuditCorrelationId = Guid.NewGuid().ToString("D"); }
        if (field == 1) { f.State.HistoryApprovalFilter = "Absent"; }
        if (field == 2)
        {
            f.State.HistoryApprovalFilter = "Exact";
            f.State.HistoryApprovalId = Guid.NewGuid().ToString("D");
        }
        var page = History(f);
        var original = page.Records[0];
        var row = original with { Audit = new SecurityAuditEvent(original.Audit.CorrelationId, original.Audit.Category,
            original.Audit.ActionId, original.Audit.Outcome, original.Audit.Initiator, original.Audit.TargetId, Guid.NewGuid()) };
        f.Store.HistoryResult = page with { Records = [row] };
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanRevoke.Should().BeFalse();
    }

    private static void EditCorrelation(Fixture f, int field, string value)
    {
        if (field == 0) { f.State.HistoryAuditCorrelationId = value; }
        if (field == 1) { f.State.HistoryApprovalFilter = value; }
        if (field == 2) { f.State.HistoryApprovalId = value; }
    }

    [Fact]
    public async Task CorrelationMetadataCannotKeepPrivateRowsOrInertTargetAfterAuthorityLoss()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.HistoryAuditCorrelationId = Guid.NewGuid().ToString("D");
        f.State.HistoryApprovalFilter = "Exact";
        f.State.HistoryApprovalId = Guid.NewGuid().ToString("D");
        f.Store.HistoryResult = History(f, "private-next") with { Records = [] };
        await f.State.ReadHistoryAsync(f.Token);
        f.Access.CanInspect = false;
        f.State.SynchronizeAuthority();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanReadHistory.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        f.Access.CanInspect = true;
        f.State.ClearHistoryFilters();
        f.State.CanReadHistory.Should().BeFalse();
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.Store.HistoryReads.Should().Be(1);
        f.Store.Revokes.Should().Be(0);
    }

    private static void AssertEmptyCorrelation(Fixture f)
    {
        f.State.HistoryAuditCorrelationId.Should().BeEmpty();
        f.State.HistoryApprovalFilter.Should().Be("Any");
        f.State.HistoryApprovalId.Should().BeEmpty();
    }
}
