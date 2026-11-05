using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task ManagementAnswerReviewsBodyFreeLedgerThenRetainsGenuineAnswerInSeparateConversation()
    {
        await using var f = await ManagementAnswerFixture.Create();
        var review = await f.Review();
        f.Model.Adapter.Calls.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
        review.Disclosure.Should().Contain(f.Pending.Target.Authority.SessionId.Value.ToString("D"))
            .And.Contain(f.Destination.Authority.SessionId.Value.ToString("D"))
            .And.Contain(f.Question).And.Contain("Complete model policy").And.Contain("Local catalogue selection")
            .And.NotContain("private pending").And.NotContain("active original");
        var receipt = await f.Service.ConfirmAsync(review, () => true, Token);
        receipt.Message.Body.Should().Be(f.Question);
        receipt.Message.AssistantResponse!.Body.Should().Be("The observed conversation has running work and two pending requests.");
        receipt.Message.Request.SessionId.Should().Be(f.Destination.Authority.SessionId);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        (await f.Model.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
            receipt.Message.Request.RequestId, Token)).Should().Be(receipt.Message);
        f.Model.Adapter.Calls.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(f.Question);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("Constraints").GetProperty("DeadlineMilliseconds").GetInt32().Should().Be(15000);
        wire.RootElement.GetProperty("Constraints").GetProperty("MaximumRounds").GetInt32().Should().Be(1);
        Encoding.UTF8.GetString(f.Model.Adapter.Wire!).Should().NotContain("private pending").And.NotContain("active original");
        f.Pending.A.Completion.IsCompleted.Should().BeFalse();
        f.Pending.B.Completion.IsCompleted.Should().BeFalse();
        f.Pending.C.Completion.IsCompleted.Should().BeFalse();
        f.Pending.F.Adapter.Calls.Should().Be(1);
        f.Pending.F.CancelAudit.Events.Should().BeEmpty();
        await f.Service.Invoking(service => service.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*unused*");
        f.Model.Messages.Should().NotContain(item => item.Contains(f.Question, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("expired")]
    [InlineData("destination")]
    [InlineData("admitted")]
    [InlineData("records")]
    public async Task ManagementAnswerRejectsStaleReviewWithoutInferenceOrRetention(string boundary)
    {
        await using var f = await ManagementAnswerFixture.Create();
        var review = await f.Review();
        if (boundary is "revoked") { review.Dispose(); }
        if (boundary is "source") { f.SourceLive = false; }
        if (boundary is "control") { f.Pending.F.ControlRevision++; }
        if (boundary is "privacy") { f.Model.CanControl = false; }
        if (boundary is "expired") { f.Pending.F.Time.Now += TimeSpan.FromSeconds(11); }
        if (boundary is "destination") { f.Model.Metadata = new(f.Destination.Authority.SessionId, new(1), new("Changed")); }
        if (boundary is "records") { f.Pending.F.RemainingProjection = value => value with { Tasks = [] }; }
        await f.Service.Invoking(service => service.ConfirmAsync(review, () => boundary is not "admitted", Token))
            .Should().ThrowAsync<Exception>();
        f.Model.Adapter.Calls.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
        f.Pending.B.Completion.IsCompleted.Should().BeFalse();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.QuotaExceeded)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired)]
    [InlineData(ModelTurnOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.Cancelled)]
    public async Task ManagementAnswerProviderFailureRetainsTruthfulHostOutcomeWithoutAnswerOrFallback(ModelTurnOutcome outcome)
    {
        await using var f = await ManagementAnswerFixture.Create();
        f.Model.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate);
        var receipt = await f.Service.ConfirmAsync(await f.Review(), () => true, Token);
        receipt.Message.Outcome.Should().Be(outcome);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.Model.Adapter.Calls.Should().Be(1);
        f.Pending.F.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ManagementAnswerRequiresDifferentActiveDestinationAndExactHostReview()
    {
        await using var f = await ManagementAnswerFixture.Create();
        await f.Service.Invoking(service => service.ReviewAsync(f.Pending.Target, f.Pending.Target,
            f.Question, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        await f.Service.Invoking(service => service.ReviewAsync(f.Pending.Target,
            f.Destination with { Authority = f.Destination.Authority with { IsActive = false } },
            f.Question, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        var review = await f.Review();
        await using var foreign = await ManagementAnswerFixture.Create();
        await foreign.Service.Invoking(service => service.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Model.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("metadata")]
    public async Task ManagementAnswerLateProviderSuccessCannotSurviveSourceRevocation(string boundary)
    {
        await using var f = await ManagementAnswerFixture.Create();
        var review = await f.Review();
        f.Model.Adapter.OnRun = () =>
        {
            if (boundary is "source") { f.SourceLive = false; }
            if (boundary is "privacy") { f.Pending.F.CanControl = false; }
            if (boundary is "metadata") { f.Pending.F.Metadata = new(f.Pending.Target.Authority.SessionId, new(1), new("Changed")); }
        };
        await f.Service.Invoking(service => service.ConfirmAsync(review, () => true, Token)).Should().ThrowAsync<Exception>();
        f.Model.Retained!.AssistantResponse.Should().BeNull();
        f.Model.Adapter.Calls.Should().Be(1);
        f.Pending.F.CancelAudit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("cancel")]
    [InlineData("revoke")]
    [InlineData("dispose")]
    public async Task ManagementAnswerHoldsSingleLaneUntilPhysicalJoinWhileManualStatusRemainsAvailable(string boundary)
    {
        var clock = new ManagementAnswerClock();
        await using var f = await ManagementAnswerFixture.Create(clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Model.Adapter.Pending = provider.Task;
        f.Model.Adapter.OnRun = entered.SetResult;
        var review = await f.Review();
        var pending = f.Service.ConfirmAsync(review, () => true, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        Task closing = Task.CompletedTask;
        try
        {
            if (boundary is "deadline")
            {
                await clock.Armed.Task.WaitAsync(Token);
                clock.Fire();
                await pending.Invoking(task => task).Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*fifteen-second host deadline*");
            }
            if (boundary is "cancel")
            {
                await cancellation.CancelAsync();
                await pending.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
            }
            if (boundary is "revoke") { review.Dispose(); }
            if (boundary is "dispose") { closing = f.Service.DisposeAsync().AsTask(); }
            f.Model.PolicyHost.IsQuiescent.Should().BeFalse();
            if (boundary is not "dispose")
            {
                var second = await f.Review();
                await f.Service.Invoking(service => service.ConfirmAsync(second, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*still joining*");
                second.Dispose();
            }
            else { closing.IsCompleted.Should().BeFalse(); }
            using (var root = f.Pending.F.Root())
            {
                var status = await f.Pending.Service.ReadRemainingWorkAsync(f.Pending.Target, () => true, Token);
                status.Format().Should().Contain("running").And.Contain("FIFO position");
            }
            f.Model.Adapter.Calls.Should().Be(1);
            f.Pending.F.Adapter.Calls.Should().Be(1);
            f.Pending.A.Completion.IsCompleted.Should().BeFalse();
            provider.SetResult(f.Model.Adapter.Reply);
            await pending.Invoking(task => task).Should().ThrowAsync<Exception>();
            if (boundary is not "dispose") { closing = f.Service.DisposeAsync().AsTask(); }
            await closing;
            f.Model.PolicyHost.IsQuiescent.Should().BeTrue();
            f.Model.Retained!.AssistantResponse.Should().BeNull();
        }
        finally { provider.TrySetResult(f.Model.Adapter.Reply); }
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("questions")]
    [InlineData("queue")]
    [InlineData("omitted-queue")]
    public async Task ManagementAnswerRefusesIncompleteOrDiagnosticLedgerWithoutDispatch(string boundary)
    {
        await using var f = await ManagementAnswerFixture.Create();
        f.Pending.F.RemainingProjection = value => boundary switch
        {
            "tasks" => value with { OmittedTasks = 1 },
            "questions" => value with { OmittedQuestions = 1 },
            "omitted-queue" => value with { OmittedQueueRecords = 1 },
            _ => value with
            {
                QueueRecords = [new(new(HostRequest.Create(RequestOrigin.LocalUi),
                new(1), new(1), 1, SessionQueueState.Pending, Guid.NewGuid(), 1,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(30)), default, null)]
            },
        };
        await f.Invoking(value => value.Review()).Should().ThrowAsync<InvalidOperationException>();
        f.Model.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ManagementAnswerLinksOriginalNativeReviewAndDisclosesNamedDestinationWithoutQualification()
    {
        await using var f = await ManagementAnswerFixture.Create();
        f.Model.Metadata = new(f.Destination.Authority.SessionId, new(1), new("Exact answer destination"));
        using var root = f.Pending.F.Root();
        using var review = await f.Review();
        review.Disclosure.Should().Contain("Exact answer destination").And.Contain("metadata revision 1");
        f.Model.Adapter.Calls.Should().Be(0);
        await f.Service.DisposeAsync();
        await f.Invoking(value => value.Review()).Should().ThrowAsync<ObjectDisposedException>();
        await f.Service.Invoking(service => service.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ManagementAnswerRejectsAuthorityChangeAcrossDestinationReadAndObservesCancellationCallbackFailure()
    {
        await using var changed = await ManagementAnswerFixture.Create();
        changed.Model.BeforeRead = () => { changed.SourceLive = false; return Task.CompletedTask; };
        await changed.Invoking(value => value.Review()).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authority changed during validation*");
        await using var f = await ManagementAnswerFixture.Create();
        var review = await f.Review();
        CancellationTokenRegistration registration = default;
        var registered = false;
        f.Model.MetadataToken = ct =>
        {
            if (registered) { return; }
            registered = true;
            registration = ct.Register(() => throw new IOException("Synthetic content-free cancellation failure."));
        };
        try
        {
            await f.Service.Invoking(service => service.ConfirmAsync(review, () => true, Token))
                .Should().ThrowAsync<AggregateException>();
            f.Model.Adapter.Calls.Should().Be(1);
        }
        finally { registration.Dispose(); }
    }

    [Fact]
    public async Task ManagementAnswerCannotSubstituteAnotherQualifiedLocalCatalogueSelection()
    {
        await using var f = await ManagementAnswerFixture.Create(wrongProvider: true);
        var review = await f.Review();
        await f.Service.Invoking(service => service.ConfirmAsync(review, () => true, Token))
            .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        f.Model.Adapter.Calls.Should().Be(0);
        f.Model.Retained!.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData(4096, ModelTurnOutcome.Succeeded)]
    [InlineData(4097, ModelTurnOutcome.Unknown)]
    public async Task ManagementAnswerEnforcesCompleteSerializedOutputBoundary(int bytes, ModelTurnOutcome expected)
    {
        await using var f = await ManagementAnswerFixture.Create();
        var empty = f.Model.Adapter.Reply.Response! with { Answer = string.Empty };
        var framing = JsonSerializer.SerializeToUtf8Bytes(empty).Length;
        f.Model.Adapter.Reply = f.Model.Adapter.Reply with { Response = empty with { Answer = new string('x', bytes - framing) } };
        JsonSerializer.SerializeToUtf8Bytes(f.Model.Adapter.Reply.Response).Length.Should().Be(bytes);
        var receipt = await f.Service.ConfirmAsync(await f.Review(), () => true, Token);
        receipt.Message.Outcome.Should().Be(expected);
        (receipt.Message.AssistantResponse is not null).Should().Be(expected == ModelTurnOutcome.Succeeded);
        f.Model.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ManagementAnswerRejectsCompleteInputOver32768WithoutDroppingEvidence()
    {
        await using var f = await ManagementAnswerFixture.Create();
        var extras = Enumerable.Range(0, 10).Select(_ => new HostTaskObservation(
            new(new(new(Guid.NewGuid()), f.Pending.Target.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                new(1), HostTaskState.IntentRecorded), new(1), "host", true, null)).ToArray();
        f.Pending.F.RemainingProjection = value => value with { Tasks = [.. value.Tasks, .. extras] };
        f.Question = "x";
        using var initial = await f.Review();
        var request = new HostRequest(new(Guid.NewGuid()), f.Destination.Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var context = new ModelContextEnvelope(request, initial.ModelPolicy, initial.Question,
            [new(new(Guid.NewGuid()), request, new(1), initial.Evidence, ModelEvidenceDisclosure.LocalOnly)],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None,
            new(1, [], 0, 1, 32768, 4096, 15000));
        var remaining = 32768 - context.Serialize().Length + 1;
        f.Question = new string('<', remaining / 6) + new string('x', remaining % 6);
        f.Question.Length.Should().BeLessThanOrEqualTo(Kora.Core.Conversation.ConversationText.MaximumUtf16Characters);
        using var exact = await f.Review();
        var receipt = await f.Service.ConfirmAsync(exact, () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Model.Adapter.Wire!.Length.Should().Be(32768);
        f.Question += "x";
        await f.Invoking(value => value.Review()).Should().ThrowAsync<InvalidDataException>();
        f.Model.Adapter.Calls.Should().Be(1);
    }

    private sealed class ManagementSelectionAdmission(ModelTurnHost host, ModelProviderSelection selection) : IModelTurnAdmission
    {
        public bool IsQuiescent => host.IsQuiescent;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context, ModelHandoffReview? review,
            CancellationToken token) => host.AdmitAsync(selection, context, token);
    }

    private sealed class ManagementAnswerClock : TimeProvider
    {
        private readonly List<ManagementTimer> timers = [];
        internal TaskCompletionSource Armed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            dueTime.Should().Be(TimeSpan.FromSeconds(15));
            var timer = new ManagementTimer(callback, state);
            timers.Add(timer);
            if (timers.Count == 2) { Armed.TrySetResult(); }
            return timer;
        }
        internal void Fire()
        {
            foreach (var timer in timers.AsEnumerable().Reverse().ToArray()) { timer.Fire(); }
        }
        private sealed class ManagementTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            internal void Fire() { if (!disposed) { callback(state); } }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class ManagementAnswerFixture : IAsyncDisposable
    {
        internal PendingClearFixture Pending { get; private set; } = null!;
        internal Fixture Model { get; } = new();
        internal RemainingWorkManagement Service { get; private set; } = null!;
        internal SessionWorkspaceEntry Destination => new(Model.Session, Model.Metadata);
        internal bool SourceLive { get; set; } = true;
        internal string Question { get; set; } = "What remains in this explicitly selected source conversation?";
        internal static async Task<ManagementAnswerFixture> Create(TimeProvider? clock = null, bool wrongProvider = false)
        {
            var f = new ManagementAnswerFixture();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Pending = await PendingClearFixture.Create(providerEntered: entered.SetResult);
            await entered.Task.WaitAsync(Token);
            f.Pending.F.Time.AdditionalExpectedDeadline = TimeSpan.FromSeconds(10);
            f.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new("The observed conversation has running work and two pending requests.", null));
            var workspace = new SessionWorkspaceService(f.Model, new(f.Model), f.Model, NullLogger<SessionWorkspaceService>.Instance);
            var selection = ModelProviderSelection.OllamaCandidate with { Model = new(Guid.NewGuid()) };
            var host = wrongProvider ? f.Model.Qualified(selection) : f.Model.PolicyHost;
            IModelTurnAdmission admission = wrongProvider ? new ManagementSelectionAdmission(host, selection) : host;
            f.Service = new(f.Pending.Service, f.Model.ConversationService(admission, execution: host),
                workspace, f.Model, clock ?? f.Model.Time, NullLogger<RemainingWorkManagement>.Instance);
            return f;
        }
        internal Task<RemainingWorkManagementReview> Review() =>
            Service.ReviewAsync(Pending.Target, Destination, Question, () => SourceLive, Token);
        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Model.Dispose();
            Pending.F.Time.ExpectedDeadline = TimeSpan.FromSeconds(15);
            await Pending.DisposeAsync();
        }
    }
}