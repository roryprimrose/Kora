using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Actions = Kora.Application.UnitTests.Memory.MemoryBatchDisableWorkflowTests.Actions;

namespace Kora.Application.UnitTests.Memory;

public sealed class MemoryBatchForgetWorkflowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actions Create() => new() { Operation = MemoryBatchOperation.Forget };
    private static Task<MemoryBatchReview> Review(MemoryBatchWorkflow workflow, Actions actions,
        Func<bool>? admitted = null, CancellationToken token = default) =>
        workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, actions.Selection.Take(2).ToArray(),
            admitted ?? (() => true), token);

    [Fact]
    public async Task ExplicitEnabledAndDisabledSubsetUsesOneShotForgetAndReturnsOnlyTombstones()
    {
        var actions = Create();
        var second = actions.Rows.Values.ElementAt(1);
        actions.Rows[second.Id.Value] = second with { Retention = MemoryRetentionState.Disabled };
        using var workflow = new MemoryBatchWorkflow(actions);
        var selection = actions.Selection.Take(2).Reverse().ToArray();
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, selection, () => true, Token);
        review.Operation.Should().Be(MemoryBatchOperation.Forget);
        review.Records.Select(row => row.Id.Value).Should().Equal(selection.Select(row => row.Id));
        actions.Disabled.Should().BeEmpty();
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.StoppedBy.Should().BeNull();
        result.Items.Should().OnlyContain(item => item.State == MemoryBatchState.Succeeded
            && item.ActionResult!.Record!.Retention == MemoryRetentionState.Forgotten
            && item.ActionResult.Record.Candidate == null && item.ActionResult.Record.Receipt == null
            && item.ActionResult.Use == null);
        actions.Disabled.Should().Equal(selection.Select(row => row.Id));
        actions.Rows.Should().HaveCount(3);
        actions.Rows.Values.Count(row => row.Candidate != null).Should().Be(1);
        review.Records.Should().BeEmpty();
        var repeat = () => workflow.ConfirmAsync(review, () => true, Token);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
        MemoryBatchPolicy.Disclosure(MemoryBatchOperation.Forget).Should().Contain("Irreversible")
            .And.Contain("not atomic-all").And.Contain("non-reusable").And.Contain("physical or forensic")
            .And.Contain("cannot be rolled back");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("forgotten")]
    [InlineData("pending")]
    [InlineData("review")]
    [InlineData("capacity")]
    [InlineData("identity")]
    [InlineData("scope")]
    [InlineData("revision")]
    [InlineData("retention")]
    [InlineData("time")]
    public async Task EntireSelectionIsValidatedBeforeAnyInspectionOrMutation(string fault)
    {
        var actions = Create();
        var first = actions.Selection[0];
        var later = actions.Selection[1];
        IReadOnlyList<MemorySummary> selected = fault switch
        {
            "empty" => [],
            "duplicate" => [first, first],
            "foreign" => [first, later with { Scope = MemoryScope.Session(new(Guid.NewGuid())) }],
            "forgotten" => [first, later with { Retention = MemoryRetentionState.Forgotten }],
            "pending" => [first, later with { Retention = MemoryRetentionState.Pending }],
            "review" => [first, later with { Review = MemoryReviewState.Reviewed }],
            "capacity" => Enumerable.Repeat(first, MemoryPolicy.MaximumEntries + 1).ToArray(),
            "identity" => [first, later with { Id = Guid.Empty }],
            "scope" => [first, later with { Scope = null! }],
            "revision" => [first, later with { Revision = 0 }],
            "retention" => [first, later with { Retention = (MemoryRetentionState)99 }],
            "time" => [first, later with { CreatedAt = later.CreatedAt.ToOffset(TimeSpan.FromHours(1)) }],
            _ => throw new InvalidOperationException(),
        };
        using var workflow = new MemoryBatchWorkflow(actions);
        var act = () => workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, selected, () => true, Token);
        await act.Should().ThrowAsync<InvalidDataException>();
        actions.Inspections.Should().BeEmpty();
        actions.Disabled.Should().BeEmpty();
    }

    [Theory]
    [InlineData("content")]
    [InlineData("classification")]
    [InlineData("receipt")]
    [InlineData("lineage")]
    [InlineData("request")]
    [InlineData("session")]
    [InlineData("boundary")]
    [InlineData("summary")]
    [InlineData("exhausted")]
    [InlineData("null")]
    public async Task CorruptLaterCompleteRecordNeverAuthorizesAnEarlyForget(string fault)
    {
        var actions = Create();
        var selection = actions.Selection;
        var row = actions.Rows[selection[1].Id];
        actions.Rows[row.Id.Value] = fault switch
        {
            "content" => row with { Candidate = null },
            "classification" => row with { Candidate = new((MemoryContentClass)99, "invalid") },
            "receipt" => row with { Receipt = null },
            "lineage" => row with { Lineage = null! },
            "request" => row with { Lineage = row.Lineage with { Request = null! } },
            "session" => row with { Lineage = row.Lineage with { Request = HostRequest.Create(RequestOrigin.LocalUi) } },
            "boundary" => row with { Receipt = row.Receipt! with { Boundary = null! } },
            "summary" => row with { CreatedAt = row.CreatedAt.AddTicks(1) },
            "exhausted" => row with { Revision = new(long.MaxValue) },
            "null" => row,
            _ => throw new InvalidOperationException(),
        };
        actions.ReturnNull = fault is "null";
        using var workflow = new MemoryBatchWorkflow(actions);
        var act = () => workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, selection, () => true, Token);
        await act.Should().ThrowAsync<InvalidDataException>();
        actions.Attempts.Should().Be(0);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("classification")]
    [InlineData("revision")]
    [InlineData("scope")]
    [InlineData("lineage")]
    [InlineData("forgotten")]
    public async Task FullPreflightDetectsLaterExactRecordAbaBeforeAnyMutation(string change)
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        var review = await Review(workflow, actions, token: Token);
        var row = review.Records[1];
        actions.Rows[row.Id.Value] = change switch
        {
            "content" => row with { Candidate = row.Candidate! with { Value = "changed body" } },
            "classification" => row with { Candidate = row.Candidate! with { ContentClass = MemoryContentClass.ExplicitFact } },
            "revision" => row with { Revision = new(row.Revision.Value + 1) },
            "scope" => row with { Scope = MemoryScope.Session(new(Guid.NewGuid())) },
            "lineage" => row with { Lineage = row.Lineage with { SessionGeneration = new(2) } },
            "forgotten" => row with { Candidate = null, Receipt = null, Retention = MemoryRetentionState.Forgotten },
            _ => throw new InvalidOperationException(),
        };
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Select(item => item.State).Should().Equal(MemoryBatchState.NotAttempted, MemoryBatchState.Conflict);
        result.StoppedBy.Should().Be(MemoryBatchState.Conflict);
        actions.Attempts.Should().Be(0);
        review.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemoryOutcome.RevisionConflict, "Conflict")]
    [InlineData(MemoryOutcome.InvalidTransition, "Conflict")]
    [InlineData(MemoryOutcome.NotFound, "Conflict")]
    [InlineData(MemoryOutcome.Denied, "Unavailable")]
    [InlineData(MemoryOutcome.CapacityExceeded, "Unavailable")]
    [InlineData(MemoryOutcome.Cancelled, "Cancelled")]
    public async Task FirstNonSuccessPreservesPriorCommitAndScrubsEvenHostileReturnedOldBody(
        MemoryOutcome failure, string expected)
    {
        var actions = Create();
        actions.FailAt = 1;
        var old = actions.Rows.Values.ElementAt(1);
        actions.Failure = new(failure, MemoryReason.StateMismatch, old,
            new(old.Id, old.Revision, old.Scope, old.Lineage, old.Candidate!, old.Receipt!, old.Lineage.Request));
        using var workflow = new MemoryBatchWorkflow(actions);
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, actions.Selection, () => true, Token);
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Select(item => item.State.ToString()).Should().Equal("Succeeded", expected, "NotAttempted");
        result.Items[1].ActionResult!.Record.Should().BeNull();
        result.Items[1].ActionResult!.Use.Should().BeNull();
        actions.Disabled.Should().ContainSingle();
    }

    [Theory]
    [InlineData("read")]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("unknown-commit")]
    [InlineData("missing-readback")]
    [InlineData("wrong-readback")]
    [InlineData("unexpected")]
    [InlineData("cancel-unconfirmed")]
    [InlineData("cancel-unknown-commit")]
    public async Task UnknownOrUnconfirmedOutcomesAreUnavailableAndNeverExposeRemovedContent(string fault)
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, actions.Selection, () => true, Token);
        if (fault is "read") { actions.OnInspect = () => throw new IOException("unavailable"); }
        else { actions.FailAt = 1; actions.Fault = fault; }
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        var index = fault is "read" ? 0 : 1;
        result.Items[index].State.Should().Be(MemoryBatchState.Unavailable);
        result.Items.Skip(index + 1).Should().OnlyContain(item => item.State == MemoryBatchState.NotAttempted);
        result.Items.Should().OnlyContain(item => item.ActionResult == null || item.ActionResult.Record == null
            || item.ActionResult.Record.Candidate == null);
        review.Records.Should().BeEmpty();
        if (fault is "unknown-commit" or "cancel-unknown-commit") { actions.Disabled.Should().HaveCount(2); }
        actions.Attempts.Should().Be(fault is "read" ? 0 : 2);
    }

    [Theory]
    [InlineData("cancel-before")]
    [InlineData("cancel-after")]
    [InlineData("cancel-returned")]
    [InlineData("retire")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    public async Task CancellationAndPrivacyRetirementNeverRollBackEarlierTombstones(string fault)
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var admitted = true;
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, actions.Selection,
            () => admitted, Token);
        if (fault is "cancel-before") { await cancellation.CancelAsync(); }
        else
        {
            actions.AfterCompleted = () =>
            {
                switch (fault)
                {
                    case "cancel-after": cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token);
                    case "cancel-returned": cancellation.Cancel(); break;
                    case "retire": workflow.Retire(); break;
                    case "dispose": workflow.Dispose(); break;
                    case "privacy": admitted = false; break;
                }
            };
        }
        var result = await workflow.ConfirmAsync(review, () => true, cancellation.Token);
        result.Items[0].State.Should().Be(fault is "cancel-before" ? MemoryBatchState.Cancelled : MemoryBatchState.Succeeded);
        actions.Attempts.Should().Be(fault is "cancel-before" ? 0 : 1);
        result.Items[2].State.Should().Be(MemoryBatchState.NotAttempted);
        result.Items.Should().OnlyContain(item => item.ActionResult == null || item.ActionResult.Record == null
            || item.ActionResult.Record.Candidate == null);
        review.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("retire")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task RetiredReviewDropsPrivateBodiesWithoutMutationAndCannotReacquireAdmission(string fault)
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var admitted = true;
        var review = await Review(workflow, actions, () => admitted, cancellation.Token);
        switch (fault)
        {
            case "retire": workflow.Retire(); break;
            case "dispose": workflow.Dispose(); break;
            case "privacy": admitted = false; break;
            case "cancel": await cancellation.CancelAsync(); break;
        }
        review.Records.Should().BeEmpty();
        actions.Attempts.Should().Be(0);
        var act = () => workflow.ConfirmAsync(review, () => true, Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UndefinedOperationRetiresPreviousReviewAndCannotSelectAnotherAction()
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        var review = await Review(workflow, actions, token: Token);
        var invalid = () => workflow.ReviewAsync((MemoryBatchOperation)99, actions.Session, actions.Selection, () => true, Token);
        await invalid.Should().ThrowAsync<InvalidDataException>();
        review.Records.Should().BeEmpty();
        var repeat = () => workflow.ConfirmAsync(review, () => true, Token);
        await repeat.Should().ThrowAsync<InvalidOperationException>();
        actions.Attempts.Should().Be(0);
        var expected = () => MemoryBatchPolicy.Expected((MemoryBatchOperation)99, actions.Rows.Values.First());
        expected.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("retire")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task DelayedInspectionCannotPublishPrivateBodiesAfterReviewAdmissionChanges(string fault)
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var admitted = true;
        actions.OnInspect = () =>
        {
            switch (fault)
            {
                case "retire": workflow.Retire(); break;
                case "dispose": workflow.Dispose(); break;
                case "privacy": admitted = false; break;
                case "cancel": cancellation.Cancel(); break;
            }
        };
        var act = () => Review(workflow, actions, () => admitted, cancellation.Token);
        if (fault is "cancel") { await act.Should().ThrowAsync<OperationCanceledException>(); }
        else { await act.Should().ThrowAsync<InvalidOperationException>(); }
        actions.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task ExactRecordChangeAfterFullPreflightStopsOnlyThatStepAndCannotBroadenSubset()
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        var review = await workflow.ReviewAsync(MemoryBatchOperation.Forget, actions.Session, actions.Selection, () => true, Token);
        var later = review.Records[1];
        actions.PendingDisable = () =>
        {
            if (actions.Attempts == 2)
            {
                actions.Rows[later.Id.Value] = later with
                {
                    Candidate = later.Candidate! with { ContentClass = MemoryContentClass.WorkflowPreference },
                };
            }
            return Task.CompletedTask;
        };
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Select(item => item.State).Should().Equal(
            MemoryBatchState.Succeeded, MemoryBatchState.Conflict, MemoryBatchState.NotAttempted);
        actions.Rows[later.Id.Value].Retention.Should().Be(MemoryRetentionState.Enabled);
        actions.Disabled.Should().ContainSingle();
    }

    [Fact]
    public async Task ApplyingForgetRejectsReentrantReviewAndRepeatedConfirmationWithoutAnotherMutation()
    {
        var actions = Create();
        using var workflow = new MemoryBatchWorkflow(actions);
        var review = await Review(workflow, actions, token: Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.PendingDisable = () =>
        {
            entered.SetResult();
            return released.Task;
        };
        var applying = workflow.ConfirmAsync(review, () => true, Token);
        await entered.Task.WaitAsync(Token);
        try
        {
            var anotherReview = () => Review(workflow, actions, token: Token);
            var anotherConfirm = () => workflow.ConfirmAsync(review, () => true, Token);
            await anotherReview.Should().ThrowAsync<InvalidOperationException>();
            await anotherConfirm.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            actions.PendingDisable = null;
            released.SetResult();
        }
        (await applying).Items.Should().OnlyContain(item => item.State == MemoryBatchState.Succeeded);
        actions.Attempts.Should().Be(2);
    }

    [Fact]
    public void SharedPolicyRejectsDisabledRowsForDisableAndPreservesExistingDisableDisclosure()
    {
        var actions = Create();
        var record = actions.Rows.Values.First() with { Retention = MemoryRetentionState.Disabled };
        var summary = MemoryBatchPolicy.Summary(record);
        var validate = () => MemoryBatchPolicy.ValidateRecord(MemoryBatchOperation.Disable, actions.Session, summary, record);
        validate.Should().Throw<InvalidDataException>();
        MemoryBatchPolicy.Disclosure(MemoryBatchOperation.Disable).Should().Be(MemoryBatchOutcome.Disclosure);
        actions.Attempts.Should().Be(0);
    }
}
