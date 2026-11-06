using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Application.UnitTests.Interaction;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Presentation;

public sealed class DetailsOfferServiceTests
{
    [Theory]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    public async Task OneUseExactAnswerHasIndependentControlAndNoImplicitReveal(bool yes, RequestOrigin channel)
    {
        using var f = new DetailsOfferTestSource(named: yes);
        var service = new DetailsOfferService(f.Store, f.Time);
        HostQuestionRecord? displayed = null;
        var reveals = 0;
        var withdrawn = 0;
        f.Request.Withdrawn += () => withdrawn++;
        var running = service.RunAsync(f.Request, () => f.Live, (question, live) =>
        {
            live().Should().BeTrue();
            displayed = question;
            return Task.CompletedTask;
        }, live => { live().Should().BeTrue(); reveals++; return Task.FromResult(true); }, f.Token);
        service.Revalidate().Should().BeTrue();
        displayed!.Key.Request.Should().NotBe(f.Request.Receipt.Message.Request);
        displayed.DetailsOffer!.OriginalRequest.Should().Be(f.Request.Receipt.Message.Request);
        reveals.Should().Be(0);
        await service.Invoking(value => value.RunAsync(f.Request, () => true,
            (_, _) => Task.CompletedTask, _ => Task.FromResult(true), f.Token)).Should().ThrowAsync<InvalidOperationException>();
        await service.AnswerAsync(displayed.Key, yes, channel, f.Token);
        await running;
        f.Store.Completed.Should().Equal(yes);
        f.Store.Serial!.Snapshot.Questions.Single().AnswerChannel.Should().Be(channel);
        reveals.Should().Be(yes ? 1 : 0);
        f.Request.IsRevealConfirmed.Should().Be(yes);
        withdrawn.Should().Be(yes ? 0 : 1);
        f.Request.IsCurrent().Should().Be(yes);
        service.Revalidate().Should().BeFalse();
        await service.Invoking(value => value.AnswerAsync(displayed.Key, true, channel, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(value => value.RunAsync(f.Request, () => true,
            (_, _) => Task.CompletedTask, _ => Task.FromResult(true), f.Token)).Should().ThrowAsync<InvalidOperationException>();
        await service.DisposeAsync();
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("elapsed")]
    [InlineData("rollback")]
    [InlineData("source")]
    [InlineData("host")]
    [InlineData("withdraw")]
    [InlineData("caller")]
    [InlineData("dispose")]
    [InlineData("queued-present")]
    [InlineData("admitted")]
    [InlineData("after-answer")]
    public async Task EveryRetirementClosesOnlyTheOfferAndCannotRevealOrReplay(string cause)
    {
        using var f = new DetailsOfferTestSource();
        var service = new DetailsOfferService(f.Store, f.Time);
        using var caller = new CancellationTokenSource();
        var hostLive = true;
        HostQuestionRecord? displayed = null;
        var reveals = 0;
        if (cause is "admitted") { f.Store.AfterAdmission = () => f.Live = false; }
        var running = service.RunAsync(f.Request, () => hostLive, (question, live) =>
        {
            displayed = question;
            if (cause is "queued-present") { f.Live = false; live().Should().BeFalse(); }
            return Task.CompletedTask;
        }, _ => { reveals++; return Task.FromResult(true); }, caller.Token);
        if (cause is "after-answer")
        {
            f.Store.Serial!.BeforeCommit = () => f.Live = false;
            await service.AnswerAsync(f.Store.Question!.Key, true, RequestOrigin.LocalUi, f.Token);
        }
        if (cause is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
        if (cause is "elapsed") { f.Time.Stamp += TimeSpan.FromSeconds(60).Ticks; }
        if (cause is "rollback") { f.Time.Now = f.Time.Now.AddTicks(-1); }
        if (cause is "source") { f.Live = false; }
        if (cause is "host") { hostLive = false; }
        if (cause is "withdraw") { service.Withdraw(); }
        if (cause is "caller") { caller.Cancel(); }
        if (cause is "dispose") { await service.DisposeAsync(); }
        if (cause is not "after-answer") { service.Revalidate(); }
        await running;
        reveals.Should().Be(0);
        f.Store.Completed.Should().Equal(false);
        f.Live = hostLive = true;
        await service.Invoking(value => value.AnswerAsync(f.Store.Question!.Key, true, RequestOrigin.LocalUi, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.DisposeAsync();
        await service.Invoking(value => value.RunAsync(f.Request, () => true, (_, _) => Task.CompletedTask,
            _ => Task.FromResult(true), f.Token)).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("host")]
    [InlineData("nonterminal")]
    [InlineData("short")]
    [InlineData("answer")]
    [InlineData("admission")]
    public async Task MissingSourceOrAdmissionNeverPresentsAnotherQuestion(string fault)
    {
        using var f = new DetailsOfferTestSource();
        var request = f.Request;
        if (fault is "source") { f.Live = false; }
        if (fault is "nonterminal") { request = new(request.Receipt with { Task = new(f.Original, new(1), HostTaskState.IntentRecorded) }, request.Target, request.OptIn, () => f.Live); }
        if (fault is "short" or "answer")
        {
            request = new(request.Receipt with
            {
                Message = request.Receipt.Message with
                { AssistantResponse = fault is "answer" ? null : new(request.Receipt.Message.Execution!, "Short answer.") }
            },
                request.Target, request.OptIn, () => f.Live);
        }
        f.Store.FailAdmission = fault is "admission";
        var service = new DetailsOfferService(f.Store, f.Time);
        var displayed = 0;
        await service.Invoking(value => value.RunAsync(request, () => fault is not "host",
            (_, _) => { displayed++; return Task.CompletedTask; }, _ => Task.FromResult(true), f.Token)).Should().ThrowAsync<InvalidOperationException>();
        displayed.Should().Be(0);
        f.Store.Completed.Should().BeEmpty();
        await service.DisposeAsync();
    }

    [Theory]
    [InlineData("present")]
    [InlineData("reveal")]
    [InlineData("unconfirmed")]
    [InlineData("complete")]
    public async Task NativeOrAuditFailureNeverClaimsARevealedSuccess(string fault)
    {
        using var f = new DetailsOfferTestSource();
        var service = new DetailsOfferService(f.Store, f.Time);
        var running = service.RunAsync(f.Request, () => true, (_, _) =>
            fault is "present" ? Task.FromException(new IOException("Synthetic native failure.")) : Task.CompletedTask,
            _ => fault is "reveal" ? Task.FromException<bool>(new IOException("Synthetic reveal failure."))
                : Task.FromResult(fault is not "unconfirmed"), f.Token);
        if (fault is not "present")
        {
            f.Store.FailCompletion = fault is "complete";
            await service.AnswerAsync(f.Store.Question!.Key, true, RequestOrigin.LocalUi, f.Token);
        }
        await running.Invoking(value => value).Should().ThrowAsync<Exception>();
        f.Store.Completed.Should().NotContain(true);
        f.Store.FailCompletion = false;
        await service.DisposeAsync();
    }

    [Theory]
    [InlineData("invalid-channel")]
    [InlineData("refused")]
    [InlineData("storage")]
    [InlineData("foreign")]
    public async Task InvalidOrRefusedAnswersCannotFallThroughOrReveal(string fault)
    {
        using var f = new DetailsOfferTestSource();
        await using var service = new DetailsOfferService(f.Store, f.Time);
        var running = service.RunAsync(f.Request, () => true, (_, _) => Task.CompletedTask, _ => Task.FromResult(true), f.Token);
        var key = f.Store.Question!.Key;
        if (fault is "refused") { f.Store.Serial!.RefusedDecision = new(HostInteractionOutcome.Conflict, "changed"); }
        if (fault is "storage") { f.Store.Serial!.FailAudit = true; }
        if (fault is "foreign") { key = new(key.Request, new(Guid.NewGuid()), key.Revision); }
        await service.Invoking(value => value.AnswerAsync(key, true,
            fault is "invalid-channel" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, f.Token)).Should().ThrowAsync<Exception>();
        service.Withdraw();
        await running;
        f.Store.Completed.Should().Equal(false);
    }

    [Fact]
    public async Task DisposalKeepsOwnershipUntilAnAlreadyStartedNativeRevealActuallyJoins()
    {
        using var f = new DetailsOfferTestSource();
        var service = new DetailsOfferService(f.Store, f.Time);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = service.RunAsync(f.Request, () => f.Live, (_, _) => Task.CompletedTask, async live =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(f.Token);
            return live();
        }, f.Token);
        await service.AnswerAsync(f.Store.Question!.Key, true, RequestOrigin.LocalUi, f.Token);
        await entered.Task.WaitAsync(f.Token);
        var disposal = service.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse("cancellation is not native resource quiescence");
        release.SetResult();
        await running.Invoking(value => value).Should().ThrowAsync<InvalidOperationException>();
        await disposal;
        f.Request.IsRevealConfirmed.Should().BeFalse();
        f.Store.Completed.Should().Equal(false);
    }

    [Fact]
    public async Task LateCommittedAnswerCannotRestoreAWithdrawnPresenterOrReleaseItsJoinEarly()
    {
        using var f = new DetailsOfferTestSource();
        var service = new DetailsOfferService(f.Store, f.Time);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.AfterAnswer = async () => { entered.TrySetResult(); await release.Task.WaitAsync(f.Token); };
        var running = service.RunAsync(f.Request, () => true, (_, _) => Task.CompletedTask, _ => Task.FromResult(true), f.Token);
        var answering = service.AnswerAsync(f.Store.Question!.Key, true, RequestOrigin.LocalUi, f.Token);
        await entered.Task.WaitAsync(f.Token);
        await service.Invoking(value => value.AnswerAsync(f.Store.Question.Key, true, RequestOrigin.LocalUi, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        service.Withdraw();
        await running;
        using var other = new DetailsOfferTestSource();
        await service.Invoking(value => value.RunAsync(other.Request, () => true, (_, _) => Task.CompletedTask,
            _ => Task.FromResult(true), f.Token)).Should().ThrowAsync<InvalidOperationException>();
        var disposal = service.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        release.SetResult();
        await answering.Invoking(value => value).Should().ThrowAsync<InvalidOperationException>();
        await disposal;
        f.Request.IsRevealConfirmed.Should().BeFalse();
        f.Store.Completed.Should().Equal(false);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("request")]
    [InlineData("revision")]
    [InlineData("status")]
    [InlineData("draft")]
    [InlineData("channel")]
    [InlineData("later")]
    [InlineData("source")]
    public async Task UnconfirmedAdmissionNeverPublishesOrRetiresAForeignQuestion(string fault)
    {
        using var f = new DetailsOfferTestSource();
        f.Store.ReturnQuestion = question => fault switch
        {
            "binding" => question with { DetailsOffer = null },
            "request" => question with
            {
                Key = new(new(new(Guid.NewGuid()), question.Key.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.HostSystem),
                    question.Key.QuestionId, question.Key.Revision)
            },
            "revision" => question with { Key = question.Key.Next() },
            "status" => question with { Status = QuestionStatus.Answered },
            "draft" => question with { Draft = new(["yes"]) },
            "channel" => question with { AnswerChannel = RequestOrigin.LocalUi },
            "later" => question with
            {
                ExpiresAt = question.ExpiresAt.AddTicks(1),
                DetailsOffer = question.DetailsOffer! with
                { CreatedAt = question.DetailsOffer!.CreatedAt.AddTicks(1), ExpiresAt = question.ExpiresAt.AddTicks(1) }
            },
            _ => question with { DetailsOffer = question.DetailsOffer! with { OptIn = Guid.NewGuid() } },
        };
        await using var service = new DetailsOfferService(f.Store, f.Time);
        var presentations = 0;
        await service.Invoking(value => value.RunAsync(f.Request, () => true,
            (_, _) => { presentations++; return Task.CompletedTask; }, _ => Task.FromResult(true), f.Token))
            .Should().ThrowAsync<InvalidDataException>();
        presentations.Should().Be(0);
        f.Store.Completed.Should().BeEmpty("a malformed receipt cannot select a foreign retirement target");
    }
    internal sealed class DetailsOfferTestSource : IDisposable
    {
        private readonly InteractionFixture tracing = new();
        internal DetailsOfferTestSource(bool named = false)
        {
            Original = HostRequest.Create(RequestOrigin.ActivatedVoice);
            var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), Original, ModelProviderSelection.OllamaCandidate);
            var message = new ConversationMessage(Original, new(1), 1, 3, Time.Now, "Original question",
                ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
            {
                Version = 2,
                Execution = provenance,
                ExecutionMetadataRevision = named ? 1 : 0,
                AssistantResponse = new(provenance, "Brief response:\nBrief.\n\nDetailed response:\n# Exact answer\n\nFull original answer."),
            };
            message.Validate();
            var target = new SessionWorkspaceEntry(new(Original.SessionId, new(1), true),
                named ? new(Original.SessionId, new(1), new("Exact source")) : null);
            Request = new(new(message, new(Original, new(3), HostTaskState.Succeeded)), target, Guid.NewGuid(), () => Live);
        }
        internal HostRequest Original { get; }
        internal Clock Time { get; } = new();
        internal OfferStore Store { get; } = new();
        internal DetailsOfferRequest Request { get; }
        internal bool Live { get; set; } = true;
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        public void Dispose() => tracing.Dispose();
        internal sealed class Clock : TimeProvider
        {
            internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
            internal long Stamp { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
            public override long GetTimestamp() => Stamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        }
        internal sealed class OfferStore : IDetailsOfferStore
        {
            internal InteractionFixture.TransactionalStore? Serial { get; private set; }
            internal HostQuestionRecord? Question { get; private set; }
            internal List<bool> Completed { get; } = [];
            internal bool FailAdmission { get; set; }
            internal bool FailCompletion { get; set; }
            internal Action? AfterAdmission { get; set; }
            internal Func<Task>? AfterAnswer { get; set; }
            internal Func<Task>? BeforeCompletion { get; set; }
            internal Func<HostQuestionRecord, HostQuestionRecord>? ReturnQuestion { get; set; }
            public ValueTask<HostQuestionRecord> AdmitDetailsOfferAsync(DetailsOfferBinding source, Func<bool> admitted, CancellationToken token)
            {
                if (FailAdmission) { throw new InvalidOperationException("Synthetic unavailable admission."); }
                admitted().Should().BeTrue();
                var request = HostActivity.RequireCurrent().Request;
                Question = new(new(request, new(Guid.NewGuid()), new(1)), DetailsOfferBinding.CreateSpec(),
                    source.Generation, source.ExpiresAt)
                { DetailsOffer = source with { RunId = Guid.NewGuid() } };
                Question.DetailsOffer.Validate(Question);
                Serial = new(new(new(request, new(1), HostTaskState.IntentRecorded),
                    new(request.SessionId, source.Generation, true), new(true, false, false, true), null, [Question], []));
                AfterAdmission?.Invoke();
                return ValueTask.FromResult(ReturnQuestion?.Invoke(Question) ?? Question);
            }
            public async ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
                Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken)
            {
                var result = await Serial!.TransactAsync(request, transition, cancellationToken);
                if (AfterAnswer is { } after) { await after(); }
                return result;
            }
            public async ValueTask CompleteDetailsOfferAsync(HostQuestionKey key, bool revealed, Func<bool> admitted, CancellationToken token)
            {
                if (FailCompletion) { throw new IOException("Synthetic audit failure."); }
                if (BeforeCompletion is { } before) { await before(); }
                if (revealed) { admitted().Should().BeTrue(); }
                key.Request.Should().Be(Question!.Key.Request);
                Completed.Add(revealed);
            }
        }
    }
}