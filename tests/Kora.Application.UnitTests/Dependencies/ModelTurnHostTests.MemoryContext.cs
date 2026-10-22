using System.Collections.Immutable;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Memory;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.LocalUi, false)]
    public async Task ReviewedMemoryTurnRetainsGenuineAnswerOrTruthfulUnavailableOnlyAfterSeparateConfirmation(
        RequestOrigin origin, bool qualified)
    {
        using var f = new Fixture(origin);
        if (qualified)
        {
            f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
            f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        }
        var source = new ContextMemories(f);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        var host = f.ConfiguredProfiles(production: !qualified);
        var service = f.ConversationService(host, execution: host);
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection,
            "Explain the reviewed decision.", static () => true, Token, origin);
        review.Records.Should().Equal(source.Snapshot.Records);
        review.UserRequest.Should().Be("Explain the reviewed decision.");
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        source.Reads.Should().Be(1);
        var receipt = await service.SubmitMemoryContextAsync(workflow, review, static () => true, Token);
        receipt.Message.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
        (receipt.Message.AssistantResponse?.Body).Should().Be(qualified ? "answer" : null);
        receipt.Message.Request.Origin.Should().Be(origin);
        review.Records.Should().BeEmpty();
        review.UserRequest.Should().BeEmpty();
        if (qualified)
        {
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().Single();
            evidence.GetProperty("Content").GetString().Should().Contain("reviewed memory value");
            evidence.GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
            source.Reads.Should().BeGreaterThan(4);
        }
        await service.Invoking(value => value.SubmitMemoryContextAsync(workflow, review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.RetainCalls.Should().Be(1);
        f.Messages.Should().NotContain(text => text.Contains("reviewed memory value", StringComparison.Ordinal));
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("profile")]
    [InlineData("disabled")]
    [InlineData("forgotten")]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("unknown")]
    [InlineData("limit")]
    public async Task EntireMemorySelectionIsRejectedBeforeAnyPrivateRead(string fault)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        var row = source.Selection[0];
        IReadOnlyList<MemorySummary> selection = fault switch
        {
            "empty" => [],
            "duplicate" => [row, row],
            "foreign" => [row with { Scope = MemoryScope.Session(new(Guid.NewGuid())) }],
            "profile" => [row with { Scope = MemoryScope.DeviceProfile(new(Guid.NewGuid())) }],
            "disabled" => [row with { Retention = MemoryRetentionState.Disabled }],
            "forgotten" => [row with { Retention = MemoryRetentionState.Forgotten }],
            "pending" => [row with { Review = MemoryReviewState.Proposed }],
            "rejected" => [row with { Review = MemoryReviewState.Rejected }],
            "unknown" => [row with { Review = (MemoryReviewState)99 }],
            _ => Enumerable.Repeat(row, 17).ToArray(),
        };
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        await workflow.Invoking(value => value.ReviewAsync(new(f.Session, null), selection, "question",
            static () => true, Token)).Should().ThrowAsync<InvalidDataException>();
        source.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("revoked")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("cancelled")]
    [InlineData("expired")]
    [InlineData("retired")]
    public async Task QueuedMemoryUseRevocationClearsReviewAndNeverRetainsOrExecutes(string fault)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        var time = new MemoryReviewTime();
        using var workflow = new SessionMemoryContext(source, f, time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question",
            static () => true, cancellation.Token);
        switch (fault)
        {
            case "changed": source.Snapshot = source.Snapshot with
                { Records = [source.Snapshot.Records[0] with { Candidate = new(MemoryContentClass.Decision, "changed same revision") }] }; break;
            case "revoked": source.Invalidate(); break;
            case "privacy": f.CanControl = false; break;
            case "control": f.ControlRevision++; break;
            case "cancelled": cancellation.Cancel(); break;
            case "expired": time.Expire(); break;
            case "retired": workflow.Retire(); break;
        }
        var service = f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable));
        await service.Invoking(value => value.SubmitMemoryContextAsync(workflow, review, static () => true, Token))
            .Should().ThrowAsync<Exception>();
        review.Records.Should().BeEmpty();
        review.UserRequest.Should().BeEmpty();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task RevocationDuringNonCooperativeProviderSuppressesLateAnswerAndResourcesQuiesce(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var source = new ContextMemories(f);
        var time = new MemoryReviewTime();
        using var workflow = new SessionMemoryContext(source, f, time);
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        var pending = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = pending.Task;
        var host = f.ConfiguredProfiles();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var submission = f.ConversationService(host, execution: host)
            .SubmitMemoryContextAsync(workflow, review, static () => true, cancellation.Token);
        f.Adapter.Calls.Should().Be(1);
        if (fault is "source") { source.Invalidate(); }
        else if (fault is "expiry") { time.Expire(); }
        else { cancellation.Cancel(); }
        await ((Func<Task>)(async () => { await submission; })).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        review.Records.Should().BeEmpty();
        host.IsQuiescent.Should().BeFalse();
        f.Retained!.AssistantResponse.Should().BeNull();
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late answer", null)));
        await host.DisposeAsync();
        host.IsQuiescent.Should().BeTrue();
        f.ExecutionCompletions.Should().Be(0);
    }

    [Fact]
    public async Task DefaultConversationStillMakesNoMemoryReads()
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        await f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable))
            .SubmitAsync(new(f.Session, null), "ordinary no-memory request", static () => true, Token);
        source.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("terminal")]
    [InlineData("answer")]
    public async Task UnconfirmedMemoryConversationCommitNeverBecomesAReportedSuccessOrAutomaticRetry(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
        var source = new ContextMemories(f);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        if (fault is "dispatch") { f.StartReceiptFault = "body"; }
        if (fault is "terminal") { f.TerminalReceiptFault = "revision"; }
        if (fault is "answer")
        { f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(" ", null)); }
        var host = f.ConfiguredProfiles();
        var service = f.ConversationService(host, execution: host);
        if (fault is "answer")
        {
            var receipt = await service.SubmitMemoryContextAsync(workflow, review, static () => true, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else
        {
            await service.Invoking(value => value.SubmitMemoryContextAsync(workflow, review, static () => true, Token))
                .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        }
        await service.Invoking(value => value.SubmitMemoryContextAsync(workflow, review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Records.Should().BeEmpty();
        f.RetainCalls.Should().Be(1);
        f.Adapter.Calls.Should().Be(fault is "dispatch" ? 0 : 1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task WallClockExpiryAndRollbackRetirePrivateReviewEvenBeforeTimerCallback()
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        var time = new MemoryReviewTime();
        using var workflow = new SessionMemoryContext(source, f, time);
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        time.Now = DateTimeOffset.UnixEpoch.AddMinutes(2);
        review.Current.Should().BeFalse();
        review.Records.Should().BeEmpty();
        time.TimerDisposed.Should().BeTrue();
        time.Now = DateTimeOffset.UnixEpoch;
        var next = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        time.Now = DateTimeOffset.UnixEpoch.AddTicks(-1);
        next.Current.Should().BeFalse();
        next.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteSerializedMemoryEnvelopeHonorsExactByteAndItemLimitsWithoutTruncation()
    {
        using var f = new Fixture();
        var source = new ContextMemories(f, 16);
        var request = f.Request;
        // Measure the nested JSON wire, rather than approximating escaping overhead.
        var records = source.Snapshot.Records;
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var low = 0;
            var high = 260;
            var chosen = records;
            while (low <= high)
            {
                var count = (low + high) / 2;
                var candidate = record with { Candidate = new(MemoryContentClass.Decision,
                    record.Candidate!.Value + new string('\u00e9', count)) };
                MemoryPolicy.ValidateCandidate(candidate.Candidate).Should().Be(MemoryReason.None);
                var trial = records.SetItem(index, candidate);
                var length = SessionMemoryContext.CreateEnvelope(request, "q",
                    source.Snapshot with { Records = trial }, f.Time.Now).Serialize().Length;
                if (length <= 32768) { chosen = trial; low = count + 1; }
                else { high = count - 1; }
            }
            records = chosen;
        }
        var snapshot = source.Snapshot with { Records = records };
        var bytes = SessionMemoryContext.CreateEnvelope(request, "q", snapshot, f.Time.Now).Serialize().Length;
        var padding = ModelContextEnvelope.MaximumInputUtf8Bytes - bytes;
        var exact = SessionMemoryContext.CreateEnvelope(request, "q" + new string('x', padding), snapshot, f.Time.Now);
        exact.Serialize().Length.Should().Be(32768);
        exact.Invoking(value => value.RequireCompleteInputLimit()).Should().NotThrow();
        var over = SessionMemoryContext.CreateEnvelope(request, exact.UserRequest + "x", snapshot, f.Time.Now);
        over.Invoking(value => value.RequireCompleteInputLimit()).Should().Throw<InvalidDataException>();
        source.Snapshot = snapshot;
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, exact.UserRequest, static () => true, Token);
        review.Records.Should().HaveCount(16);
        await workflow.Invoking(value => value.ReviewAsync(new(f.Session, null), source.Selection,
            exact.UserRequest + "x", static () => true, Token)).Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class ContextMemories : ISessionMemoryContextSource
    {
        internal ContextMemories(Fixture f, int count = 1)
        {
            var boundary = new MemoryBoundary(new(Guid.NewGuid()), f.Session.SessionId, f.Session.Generation,
                null, null, null, 1, true, true, true);
            Snapshot = new(boundary, [.. Enumerable.Range(0, count).Select(_ => new MemoryRecord(
                new(Guid.NewGuid()), new(3), MemoryScope.Session(f.Session.SessionId),
                new(f.Request, f.Session.Generation, boundary.Profile, MemoryProposalOrigin.User, null, null),
                new(MemoryContentClass.Decision, "reviewed memory value"), DateTimeOffset.UnixEpoch,
                MemoryReviewState.Admitted, MemoryRetentionState.Enabled,
                new(f.Request.RequestId, new(2), DateTimeOffset.UnixEpoch, boundary)))]);
        }
        internal SessionMemoryContextSnapshot Snapshot { get; set; }
        internal MemorySummary[] Selection => [.. Snapshot.Records.Select(record =>
            new MemorySummary(record.Id.Value, record.Revision.Value, record.Scope, record.Review, record.Retention, record.CreatedAt))];
        internal int Reads { get; private set; }
        public long Revision { get; private set; }
        public event Action? Invalidated;
        internal void Invalidate() { Revision++; Invalidated?.Invoke(); }
        public Task<SessionMemoryContextSnapshot> ReadAsync(SessionWorkspaceEntry target,
            ImmutableArray<MemorySummary> selection, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            admitted().Should().BeTrue();
            target.Authority.SessionId.Should().Be(Snapshot.Boundary.Session);
            selection.Select(row => row.Id).Should().Equal(Selection.Select(row => row.Id));
            Reads++;
            return System.Threading.Tasks.Task.FromResult(Snapshot);
        }
    }

    private sealed class MemoryReviewTime : TimeProvider
    {
        private TimerCallback? callback;
        private object? state;
        internal bool TimerDisposed { get; private set; }
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            this.state = state;
            dueTime.Should().Be(TimeSpan.FromMinutes(2));
            return new ReviewTimer(this);
        }
        internal void Expire() => callback!(state);
        private sealed class ReviewTimer(MemoryReviewTime owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => owner.TimerDisposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
