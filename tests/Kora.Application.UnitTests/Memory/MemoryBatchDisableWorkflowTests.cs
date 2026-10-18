using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using MemoryBatchDisableWorkflow = Kora.Application.Memory.MemoryBatchWorkflow;
using MemoryBatchDisableState = Kora.Application.Memory.MemoryBatchState;
using MemoryBatchDisableOutcome = Kora.Application.Memory.MemoryBatchOutcome;
using IMemoryBatchDisableActions = Kora.Application.Memory.IMemoryBatchActions;

namespace Kora.Application.UnitTests.Memory;

public sealed class MemoryBatchDisableWorkflowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReviewShowsEveryExactRecordThenConfirmationDisablesOnlyTheSelectedSubsetOnce()
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var selection = actions.Selection.Take(2).Reverse().ToArray();
        var review = await workflow.ReviewAsync(actions.Session, selection, () => true, Token);
        review.Records.Select(record => record.Id.Value).Should().Equal(selection.Select(row => row.Id));
        review.Records.Should().OnlyContain(record => record.Candidate != null);
        actions.Disabled.Should().BeEmpty();
        var outcome = await workflow.ConfirmAsync(review, () => true, Token);
        outcome.Items.Select(item => item.Id).Should().Equal(selection.Select(row => row.Id));
        outcome.Items.Should().OnlyContain(item => item.State == MemoryBatchDisableState.Succeeded);
        outcome.StoppedBy.Should().BeNull();
        outcome.Items.Should().OnlyContain(item => item.ActionResult!.Record!.Retention == MemoryRetentionState.Disabled);
        actions.Disabled.Should().Equal(selection.Select(row => row.Id));
        actions.Inspections.Should().HaveCount(4);
        actions.Rows.Values.Count(record => record.Retention == MemoryRetentionState.Enabled).Should().Be(1);
        var retry = () => workflow.ConfirmAsync(review, () => true, Token);
        await retry.Should().ThrowAsync<InvalidOperationException>();
        actions.Disabled.Should().HaveCount(2);
        MemoryBatchDisableOutcome.Disclosure.Should().Contain("not atomic-all").And.Contain("cannot be rolled back");
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("id")]
    [InlineData("scope")]
    [InlineData("retention")]
    [InlineData("review")]
    [InlineData("time")]
    [InlineData("capacity")]
    public async Task ReviewRejectsTheEntireMalformedSelectionBeforeInspection(string fault)
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var row = actions.Selection[1];
        IReadOnlyList<MemorySummary> selection = fault switch
        {
            "empty" => [],
            "duplicate" => [row, row],
            "foreign" => [actions.Selection[0], row with { Scope = MemoryScope.Session(new(Guid.NewGuid())) }],
            "id" => [actions.Selection[0], row with { Id = Guid.Empty }],
            "scope" => [row with { Scope = null! }],
            "retention" => [row with { Retention = MemoryRetentionState.Disabled }],
            "review" => [row with { Review = MemoryReviewState.Proposed }],
            "time" => [row with { CreatedAt = row.CreatedAt.ToOffset(TimeSpan.FromHours(1)) }],
            "capacity" => Enumerable.Repeat(row, MemoryPolicy.MaximumEntries + 1).ToArray(),
            _ => throw new InvalidOperationException(),
        };
        var act = () => workflow.ReviewAsync(actions.Session, selection, () => true, Token);
        await act.Should().ThrowAsync<InvalidDataException>();
        actions.Inspections.Should().BeEmpty();
        actions.Disabled.Should().BeEmpty();
    }

    [Theory]
    [InlineData("content")]
    [InlineData("receipt")]
    [InlineData("lineage")]
    [InlineData("session")]
    [InlineData("summary")]
    [InlineData("scope")]
    [InlineData("missing-lineage")]
    [InlineData("missing-request")]
    [InlineData("missing-boundary")]
    public async Task InvalidLaterInspectedRowCannotAuthorizeAnyMutation(string fault)
    {
        var actions = new Actions();
        var selection = actions.Selection;
        var row = actions.Rows[selection[1].Id];
        actions.Rows[row.Id.Value] = fault switch
        {
            "content" => row with { Candidate = new((MemoryContentClass)99, "invalid") },
            "receipt" => row with { Receipt = null },
            "lineage" => row with { Lineage = row.Lineage with { SessionGeneration = default } },
            "session" => row with { Lineage = row.Lineage with { Request = HostRequest.Create(RequestOrigin.LocalUi) } },
            "summary" => row with { CreatedAt = row.CreatedAt.AddSeconds(1) },
            "scope" => row with { Scope = null! },
            "missing-lineage" => row with { Lineage = null! },
            "missing-request" => row with { Lineage = row.Lineage with { Request = null! } },
            "missing-boundary" => row with { Receipt = row.Receipt! with { Boundary = null! } },
            _ => throw new InvalidOperationException(),
        };
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var act = () => workflow.ReviewAsync(actions.Session, selection, () => true, Token);
        await act.Should().ThrowAsync<InvalidDataException>();
        actions.Disabled.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullPreflightRejectsLaterRevisionOrSameRevisionContentAbaBeforeAnyDisable(bool sameRevision)
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        var row = review.Records[1];
        actions.Rows[row.Id.Value] = row with
        {
            Revision = sameRevision ? row.Revision : new(row.Revision.Value + 1),
            Candidate = new(MemoryContentClass.Decision, "changed after review"),
        };
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Select(item => item.State).Should().Equal(MemoryBatchDisableState.NotAttempted,
            MemoryBatchDisableState.Conflict, MemoryBatchDisableState.NotAttempted);
        actions.Disabled.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemoryOutcome.RevisionConflict, "Conflict")]
    [InlineData(MemoryOutcome.InvalidTransition, "Conflict")]
    [InlineData(MemoryOutcome.NotFound, "Conflict")]
    [InlineData(MemoryOutcome.Denied, "Unavailable")]
    [InlineData(MemoryOutcome.CapacityExceeded, "Unavailable")]
    [InlineData(MemoryOutcome.Cancelled, "Cancelled")]
    public async Task SequenceStopsAtFirstNonSuccessAndPreservesEarlierCommittedResult(
        MemoryOutcome outcome, string expected)
    {
        var actions = new Actions { Failure = new(outcome, MemoryReason.StateMismatch), FailAt = 1 };
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items.Select(item => item.State.ToString()).Should().Equal("Succeeded", expected, "NotAttempted");
        actions.Attempts.Should().Be(2);
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
    public async Task FailuresNeverBecomeSuccessOrRetryAndUnknownCommitRemainsUnavailable(string fault)
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        if (fault is "read") { actions.OnInspect = () => throw new IOException("read unavailable"); }
        else
        {
            actions.FailAt = 1;
            actions.Fault = fault;
        }
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        var index = fault is "read" ? 0 : 1;
        result.Items[index].State.Should().Be(MemoryBatchDisableState.Unavailable);
        result.Items.Skip(index + 1).Should().OnlyContain(item => item.State == MemoryBatchDisableState.NotAttempted);
        actions.Attempts.Should().Be(fault is "read" ? 0 : 2);
        if (fault is "unknown-commit" or "cancel-unknown-commit") { actions.Disabled.Should().HaveCount(2); }
        else if (fault is not "read") { result.Items[0].State.Should().Be(MemoryBatchDisableState.Succeeded); }
    }

    [Theory]
    [InlineData("cancel-before")]
    [InlineData("cancel-after")]
    [InlineData("cancel-returned")]
    [InlineData("retire-after")]
    [InlineData("revoke-after")]
    [InlineData("dispose-after")]
    public async Task CancellationAndRetirementDoNotEraseCommittedOutcomes(string fault)
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var admitted = true;
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => admitted, Token);
        if (fault is "cancel-before") { await cancellation.CancelAsync(); }
        else
        {
            actions.AfterCompleted = () =>
            {
                switch (fault)
                {
                    case "cancel-after": cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token);
                    case "cancel-returned": cancellation.Cancel(); break;
                    case "retire-after": workflow.Retire(); break;
                    case "revoke-after": admitted = false; break;
                    case "dispose-after": workflow.Dispose(); break;
                }
            };
        }
        var result = await workflow.ConfirmAsync(review, () => true, cancellation.Token);
        if (fault is "cancel-before")
        {
            result.Items.Select(item => item.State).Should().Equal(MemoryBatchDisableState.Cancelled,
                MemoryBatchDisableState.NotAttempted, MemoryBatchDisableState.NotAttempted);
            actions.Attempts.Should().Be(0);
        }
        else
        {
            result.Items[0].State.Should().Be(MemoryBatchDisableState.Succeeded);
            result.Items[1].State.Should().Be(fault is "cancel-after" ? MemoryBatchDisableState.NotAttempted
                : fault is "cancel-returned" ? MemoryBatchDisableState.Cancelled : MemoryBatchDisableState.Unavailable);
            actions.Disabled.Should().ContainSingle();
            actions.Attempts.Should().Be(1);
        }
    }

    [Theory]
    [InlineData("retire")]
    [InlineData("reselect")]
    [InlineData("dispose")]
    [InlineData("other-owner")]
    public async Task RetiredOrForeignReceiptsCannotBeConfirmed(string fault)
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        using var other = new MemoryBatchDisableWorkflow(actions);
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        switch (fault)
        {
            case "retire": workflow.Retire(); break;
            case "reselect": await workflow.ReviewAsync(actions.Session, actions.Selection.Take(1).ToArray(), () => true, Token); break;
            case "dispose": workflow.Dispose(); break;
        }
        var act = () => (fault is "other-owner" ? other : workflow).ConfirmAsync(review, () => true, Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        actions.Disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task OriginalReviewAdmissionIsRequiredEvenIfConfirmationSuppliesNewAdmission()
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var admitted = true;
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => admitted, Token);
        admitted = false;
        var result = await workflow.ConfirmAsync(review, () => true, Token);
        result.Items[0].State.Should().Be(MemoryBatchDisableState.Unavailable);
        result.StoppedBy.Should().Be(MemoryBatchDisableState.Unavailable);
        actions.Disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellingTheOriginalReviewTokenCannotBeReplacedByAFreshConfirmationToken()
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => true, cancellation.Token);
        await cancellation.CancelAsync();
        var outcome = await workflow.ConfirmAsync(review, () => true, Token);
        outcome.Items[0].State.Should().Be(MemoryBatchDisableState.Unavailable);
        actions.Disabled.Should().BeEmpty();
    }

    [Theory]
    [InlineData("retire")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task ReviewCannotPublishAfterInspectionRetiresItsAdmission(string fault)
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
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
        var act = () => workflow.ReviewAsync(actions.Session, actions.Selection, () => admitted, cancellation.Token);
        if (fault is "cancel") { await act.Should().ThrowAsync<OperationCanceledException>(); }
        else { await act.Should().ThrowAsync<InvalidOperationException>(); }
        actions.Inspections.Should().ContainSingle();
        actions.Disabled.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrExhaustedInspectedRecordIsRejectedWithoutMutation(bool exhausted)
    {
        var actions = new Actions();
        var row = actions.Rows.Values.First();
        if (exhausted) { actions.Rows[row.Id.Value] = row with { Revision = new(long.MaxValue) }; }
        else { actions.ReturnNull = true; }
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var act = () => workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        await act.Should().ThrowAsync<InvalidDataException>();
        actions.Disabled.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentReviewAndConfirmationCannotOverlapAnApplyingSequence()
    {
        var actions = new Actions();
        using var workflow = new MemoryBatchDisableWorkflow(actions);
        var review = await workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.PendingDisable = () =>
        {
            entered.SetResult();
            return released.Task;
        };
        var applying = workflow.ConfirmAsync(review, () => true, Token);
        await entered.Task.WaitAsync(Token);
        var anotherReview = () => workflow.ReviewAsync(actions.Session, actions.Selection, () => true, Token);
        var anotherConfirm = () => workflow.ConfirmAsync(review, () => true, Token);
        try
        {
            await anotherReview.Should().ThrowAsync<InvalidOperationException>();
            await anotherConfirm.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            actions.PendingDisable = null;
            released.SetResult();
        }
        (await applying).Items.Should().OnlyContain(item => item.State == MemoryBatchDisableState.Succeeded);
        actions.Attempts.Should().Be(3);
    }

    internal sealed class Actions : IMemoryBatchDisableActions
    {
        internal MemoryBatchOperation Operation { get; set; }
        internal HostId<SessionIdentity> Session { get; } = new(Guid.NewGuid());
        internal Dictionary<Guid, MemoryRecord> Rows { get; } = [];
        internal List<Guid> Inspections { get; } = [];
        internal List<Guid> Disabled { get; } = [];
        internal int Attempts { get; private set; }
        internal int FailAt { get; set; } = -1;
        internal string? Fault { get; set; }
        internal MemoryResult? Failure { get; set; }
        internal Action? OnInspect { get; set; }
        internal Action? AfterCompleted { get; set; }
        internal bool ReturnNull { get; set; }
        internal Func<Task>? PendingDisable { get; set; }
        internal IReadOnlyList<MemorySummary> Selection => Rows.Values.Select(row => new MemorySummary(
            row.Id.Value, row.Revision.Value, row.Scope, row.Review, row.Retention, row.CreatedAt)).ToArray();

        internal Actions()
        {
            var profile = new HostId<DeviceProfileIdentity>(Guid.NewGuid());
            var request = new HostRequest(new(Guid.NewGuid()), Session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            var boundary = new MemoryBoundary(profile, Session, new(1), null, null, null, 1, true, true, true);
            var time = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 3; index++)
            {
                var id = Guid.NewGuid();
                Rows[id] = new(new(id), new(3), MemoryScope.Session(Session),
                    new(request, new(1), profile, MemoryProposalOrigin.User, null, null),
                    new(MemoryContentClass.Decision, "private exact " + index), time,
                    MemoryReviewState.Admitted, MemoryRetentionState.Enabled,
                    new(request.RequestId, new(2), time, boundary));
            }
        }

        public Task<MemoryRecord> InspectAsync(HostId<SessionIdentity> session, MemorySummary selection,
            Func<bool> admitted, CancellationToken token)
        {
            session.Should().Be(Session);
            token.ThrowIfCancellationRequested();
            admitted().Should().BeTrue();
            OnInspect?.Invoke();
            Inspections.Add(selection.Id);
            return Task.FromResult(ReturnNull ? null! : Rows[selection.Id]);
        }

        public async Task ApplyAsync(MemoryBatchOperation operation, HostId<SessionIdentity> session, MemoryRecord record, Func<bool> admitted,
            Action<MemoryResult> completed, CancellationToken token)
        {
            operation.Should().Be(Operation);
            session.Should().Be(Session);
            token.ThrowIfCancellationRequested();
            admitted().Should().BeTrue();
            var fail = Attempts++ == FailAt;
            if (PendingDisable is { } pending) { await pending(); }
            if (fail && Fault is "audit" or "commit") { throw new IOException("required write unavailable"); }
            if (fail && Fault is "unexpected") { throw new NotSupportedException("required transition unavailable"); }
            if (fail && Fault is "cancel-unconfirmed") { throw new OperationCanceledException(token); }
            if (fail && Failure is { } result) { completed(result); return; }
            if (fail && Fault is "missing-readback") { return; }
            if (Rows[record.Id.Value] != record)
            {
                completed(new(MemoryOutcome.RevisionConflict, MemoryReason.RevisionMismatch));
                return;
            }
            var updated = operation == MemoryBatchOperation.Disable
                ? record with { Revision = new(record.Revision.Value + 1), Retention = MemoryRetentionState.Disabled }
                : record with
                {
                    Revision = new(record.Revision.Value + 1), Retention = MemoryRetentionState.Forgotten,
                    Candidate = null, Receipt = null,
                };
            Rows[record.Id.Value] = updated;
            Disabled.Add(record.Id.Value);
            if (fail && Fault is "unknown-commit") { throw new IOException("commit result unknown"); }
            if (fail && Fault is "cancel-unknown-commit") { throw new OperationCanceledException(token); }
            completed(new(MemoryOutcome.Succeeded, MemoryReason.None,
                fail && Fault is "wrong-readback" ? record : updated));
            AfterCompleted?.Invoke();
        }
    }
}
