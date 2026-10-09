using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.HostSystem, true)]
    [InlineData((RequestOrigin)99, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    [InlineData(RequestOrigin.LocalUi, false)]
    public async Task NativeSourceIsUnavailableWithoutAnAdmittedPendingOffer(RequestOrigin origin, bool eligible)
    {
        using var f = new Fixture();
        var source = new ModelHandoffPresentation(f.Workflow);
        (await source.ReadPending(origin, () => eligible, Token)).Should().BeEmpty();
        source.RevokeAll();
    }

    [Fact]
    public async Task ActualAuditedOfferIsConsumedWithCompleteExactPreviewAndNoPreselectedApproval()
    {
        using var f = new Fixture();
        ModelHandoffOffer offer;
        ActivityContext offeredTrace;
        using (var root = f.Root())
        {
            var policy = await Configure(f);
            var context = new ModelContextEnvelope(f.Request, "system \"exact\"\n", "original user input",
                [new(new(Guid.NewGuid()), f.Request, new(3), "inert https://example.invalid/secret", ModelEvidenceDisclosure.HostedEligible)],
                f.Time.Now, f.Time.Now.AddMinutes(1));
            offer = await Offer(f, policy, context);
            offeredTrace = root.Activity!.Context;
        }
        var stopped = new List<Activity>();
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(observer);
        using var foreignRoot = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Core, HostOperation.Request);
        var source = new ModelHandoffPresentation(f.Workflow);
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().ContainSingle().Which.Should().BeSameAs(offer);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        review.Outcome.Should().Be(ModelHandoffOutcome.Unavailable);
        review.HasReviewed.Should().BeFalse();
        await review.Decide(ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        f.Questions.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        await review.Refresh(Token);
        review.HasReviewed.Should().BeTrue();
        review.Preview.Should().Be(Encoding.UTF8.GetString(offer.Context.Serialize()));
        review.Preview.Should().Contain("Tools").And.Contain("MaximumOutputUtf8Bytes").And.Contain("Revision").And.Contain("\"Disclosure\":2");
        review.Offer!.Context.Evidence.Single().Disclosure.Should().Be(ModelEvidenceDisclosure.HostedEligible);
        offer.Id.Should().NotBeEmpty();
        offer.Revision.Value.Should().Be(1);
        offer.Generation.Should().Be(f.Session.Generation);
        offer.TaskRevision.Should().Be(f.Task!.Task.Revision);
        offer.ControlRevision.Should().Be(f.ControlRevision);
        await review.Decide(ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        review.Outcome.Should().Be(ModelHandoffOutcome.Approved);
        review.Reason.Should().Be(ModelTurnReason.None);
        review.Offer.Should().BeNull();
        review.Preview.Should().BeEmpty();
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
        f.Questions.Snapshot.Grants.Should().BeEmpty();
        f.Messages.Should().NotContain(text => text.Contains("original user input", StringComparison.Ordinal));
        stopped.Where(activity => string.Equals(activity.OperationName, "presentation.update", StringComparison.Ordinal)).Should()
            .OnlyContain(activity => activity.ParentId == null && activity.TraceId != foreignRoot.Activity!.TraceId
                && activity.Links.Any(link => link.Context.TraceId == offeredTrace.TraceId));
    }

    [Fact]
    public async Task EvidenceRemovalRetiresExactQuestionAndRequiresFreshReducedReview()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var first = new HostId<EvidenceIdentity>(Guid.NewGuid());
        var kept = new HostId<EvidenceIdentity>(Guid.NewGuid());
        var context = new ModelContextEnvelope(f.Request, "system", "user",
            [new(first, f.Request, new(2), "removed", ModelEvidenceDisclosure.HostedEligible),
            new(kept, f.Request, new(7), "retained", ModelEvidenceDisclosure.HostedEligible)],
            f.Time.Now, f.Time.Now.AddMinutes(1));
        var offer = await Offer(f, policy, context);
        var unrelated = await new HostQuestionService(f.Questions, f.Time).CreateAsync(f.Request,
            new("Unrelated pending question", QuestionKind.SingleChoice, [new("a", "A")]), f.Time.Now.AddMinutes(1), Token);
        var source = new ModelHandoffPresentation(f.Workflow);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        await review.Remove([first], Token);
        review.Outcome.Should().Be(ModelHandoffOutcome.Removed);
        var reduced = review.Offer!;
        reduced.Should().NotBeSameAs(offer);
        reduced.Id.Should().NotBe(offer.Id);
        reduced.Question.Key.QuestionId.Should().NotBe(offer.Question.Key.QuestionId);
        reduced.Context.Evidence.Should().ContainSingle().Which.Should().BeSameAs(context.Evidence[1]);
        reduced.Policy.Should().BeSameAs(policy);
        reduced.Context.UserRequest.Should().Be(context.UserRequest);
        review.HasReviewed.Should().BeFalse();
        await review.Validate(Token);
        review.HasReviewed.Should().BeFalse();
        await review.Decide(ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        review.Offer.Should().BeSameAs(reduced);
        f.Questions.Snapshot.Questions.Single(item => item.Key.QuestionId == offer.Question.Key.QuestionId).Status.Should().Be(QuestionStatus.Cancelled);
        f.Questions.Snapshot.Questions.Single(item => item.Key.QuestionId == unrelated.Question!.Key.QuestionId).Should().Be(unrelated.Question);
        source.Open(offer, RequestOrigin.LocalUi, static () => true).Should().BeNull();
        await review.Refresh(Token);
        await review.Decide(ModelHandoffDecision.Decline, RequestOrigin.LocalUi, Token);
        review.Outcome.Should().Be(ModelHandoffOutcome.Declined);
        f.Questions.Snapshot.Grants.Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("destination")]
    [InlineData("control")]
    [InlineData("generation")]
    [InlineData("task")]
    [InlineData("session")]
    [InlineData("inactive")]
    [InlineData("terminal")]
    [InlineData("expired")]
    [InlineData("question")]
    [InlineData("question-expired")]
    [InlineData("retired")]
    [InlineData("used")]
    [InlineData("owner")]
    public async Task NativeRefreshSuppressesChangedAuthorityAndClearsExactContent(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var source = new ModelHandoffPresentation(f.Workflow);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        switch (change)
        {
            case "policy":
            case "destination":
                await f.PolicyHost.SetPolicyAsync(policy with { Revision = new(2), Hosted = string.Equals(change, "destination", StringComparison.Ordinal)
                    ? policy.Hosted with { Revision = new(2) } : policy.Hosted }, Token);
                break;
            case "control": f.ControlRevision++; break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; break;
            case "task": f.Task = f.Task! with { Task = new(f.Request, new(2), HostTaskState.IntentRecorded) }; break;
            case "session": f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; break;
            case "inactive": f.Session = f.Session with { IsActive = false }; break;
            case "terminal": f.Task = f.Task! with { Task = new(f.Request, new(2), HostTaskState.Cancelled) }; break;
            case "expired": f.Time.Now = offer.Context.ExpiresAt; break;
            case "question": f.Questions.Change(snapshot => snapshot with { Questions = snapshot.Questions.SetItem(0, offer.Question with { Key = offer.Question.Key.Next() }) }); break;
            case "question-expired": f.Questions.Change(snapshot => snapshot with { Questions = snapshot.Questions.SetItem(0, offer.Question with { ExpiresAt = f.Time.Now }) }); break;
            case "retired": Interlocked.Exchange(ref offer.Retired, 1); break;
            case "used": Interlocked.Exchange(ref offer.Used, 1); break;
            case "owner": f.Current = false; break;
        }
        await review.Validate(Token);
        review.Offer.Should().BeNull();
        review.HasReviewed.Should().BeFalse();
        review.Preview.Should().BeEmpty();
        review.CanReview.Should().BeFalse();
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().BeEmpty();
        await review.Refresh(Token);
        await review.Decide(ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData((RequestOrigin)99)]
    [InlineData((RequestOrigin)(-1))]
    public async Task NativeConsumerRejectsNonuserChannelsAndForeignSource(RequestOrigin channel)
    {
        using var f = new Fixture();
        using var other = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var source = new ModelHandoffPresentation(f.Workflow);
        source.Open(offer, channel, static () => true).Should().BeNull();
        source.Open(offer, RequestOrigin.LocalUi, static () => false).Should().BeNull();
        new ModelHandoffPresentation(other.Workflow).Open(offer, RequestOrigin.LocalUi, static () => true).Should().BeNull();
        (await source.ReadPending(channel, static () => true, Token)).Should().BeEmpty();
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        await review.Decide(ModelHandoffDecision.Approve, channel, Token);
        review.Outcome.Should().Be(ModelHandoffOutcome.Denied);
        review.Reason.Should().Be(ModelTurnReason.OriginalUserRequired);
    }

    [Fact]
    public async Task ExplicitCancelUsesTheSameAtomicQuestionWorkflowAndDisposalNeverApproves()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var source = new ModelHandoffPresentation(f.Workflow);
        await using (var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!)
        {
            await review.Decide(ModelHandoffDecision.Cancel, RequestOrigin.LocalUi, Token);
            review.Outcome.Should().Be(ModelHandoffOutcome.Cancelled);
        }
        f.Questions.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Cancelled);
        var next = await Offer(f, policy);
        var closing = source.Open(next, RequestOrigin.LocalUi, static () => true)!;
        await closing.DisposeAsync();
        closing.CanReview.Should().BeFalse();
        closing.Offer.Should().BeNull();
        closing.Preview.Should().BeEmpty();
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().BeEmpty();
        f.Questions.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task LateCallbacksCannotPublishApprovalAfterLifetimeClosure(string closure)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var live = true;
        var source = new ModelHandoffPresentation(f.Workflow);
        var review = source.Open(offer, RequestOrigin.LocalUi, () => live)!;
        await review.Refresh(Token);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = () => { waiting.TrySetResult(); return release.Task; };
        using var cancellation = new CancellationTokenSource();
        var decision = review.Decide(ModelHandoffDecision.Approve, RequestOrigin.LocalUi, cancellation.Token);
        await waiting.Task;
        review.CanReview.Should().BeFalse();
        await review.Refresh(Token);
        Task? disposal = null;
        switch (closure)
        {
            case "close": review.Revoke(); break;
            case "privacy": live = false; source.RevokeAll(); break;
            case "cancel": cancellation.Cancel(); break;
            case "dispose": disposal = review.DisposeAsync().AsTask(); break;
        }
        release.SetResult();
        if (string.Equals(closure, "privacy", StringComparison.Ordinal)) { await decision; }
        else { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decision); }
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        review.Offer.Should().BeNull();
        if (disposal is not null) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposal); }
        else
        {
            try { await review.DisposeAsync(); }
            catch (OperationCanceledException) { }
        }
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("question-audit")]
    [InlineData("read")]
    public async Task NativeReviewPropagatesRequiredFailuresWithoutPublishingSuccess(string failure)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var source = new ModelHandoffPresentation(f.Workflow);
        var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        switch (failure)
        {
            case "audit": f.AuditFailure = true; break;
            case "question-audit": f.Questions.FailAudit = true; break;
            case "read": f.OnRead = () => throw new IOException("private path"); break;
        }
        await Assert.ThrowsAsync<IOException>(() => review.Decide(ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token));
        review.Offer.Should().BeNull();
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        await Assert.ThrowsAsync<IOException>(() => review.DisposeAsync().AsTask());
        f.Messages.Should().NotContain(text => text.Contains("private path", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingSourceRejectsExpiredOffersAndLatePrivacyObservations()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var source = new ModelHandoffPresentation(f.Workflow);
        f.Time.Now = offer.Context.ExpiresAt;
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().BeEmpty();
        f.Time.Now = DateTimeOffset.UnixEpoch;
        await Offer(f, policy);
        var checks = 0;
        (await source.ReadPending(RequestOrigin.LocalUi, () => ++checks == 1, Token)).Should().BeEmpty();
        source.RevokeAll();
    }

    [Fact]
    public async Task PendingSourceIsBoundedAndRetiredOffersCannotConsumeCapacity()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offers = new List<ModelHandoffOffer>();
        for (var index = 0; index < 16; index++) { offers.Add(await Offer(f, policy)); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.OfferAsync(policy, f.Context(),
            ModelHandoffReason.UserRequestedHosted, Token));
        f.Questions.Snapshot.Questions.Last().Status.Should().Be(QuestionStatus.Cancelled);
        var source = new ModelHandoffPresentation(f.Workflow);
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Count.Should().Be(16);
        offers[0].Retired = 1;
        await Offer(f, policy);
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Count.Should().Be(16);
        source.RevokeAll();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("expiry")]
    [InlineData("after-review")]
    public async Task ExactNativeQuestionObservationCannotReplaceBoundGenerationOrValidity(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var source = new ModelHandoffPresentation(f.Workflow);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        switch (change)
        {
            case "generation":
                f.Questions.Change(snapshot => snapshot with
                {
                    Session = snapshot.Session with { Generation = new(2) },
                    Questions = snapshot.Questions.SetItem(0, offer.Question with { SessionGeneration = new(2) }),
                });
                break;
            case "expiry":
                f.Questions.Change(snapshot => snapshot with
                {
                    Questions = snapshot.Questions.SetItem(0, offer.Question with { ExpiresAt = offer.Context.ExpiresAt.AddMinutes(1) }),
                });
                break;
            case "after-review": f.Questions.BeforeCommit = () => f.ControlRevision++; break;
        }
        await review.Refresh(Token);
        review.Offer.Should().BeNull();
        review.HasReviewed.Should().BeFalse();
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
    }

    [Fact]
    public async Task PendingSnapshotSkipsConcurrentlyRetiredReferencesWithoutRetargeting()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        await Offer(f, policy);
        await Offer(f, policy);
        var source = new ModelHandoffPresentation(f.Workflow);
        var reads = 0;
        f.OnRead = () => { if (++reads == 2) { source.RevokeAll(); } };
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().BeEmpty();
        f.OnRead = null;
        var offer = await Offer(f, policy);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadPending(RequestOrigin.LocalUi, static () => true, cancellation.Token));
        var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review.Refresh(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task ClosingAnUnreadOrIneligibleReviewRetiresContentWithoutConfirmation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var source = new ModelHandoffPresentation(f.Workflow);
        var admitted = true;
        await using var review = source.Open(offer, RequestOrigin.LocalUi, () => admitted)!;
        admitted = false;
        review.CanReview.Should().BeFalse();
        await review.Refresh(Token);
        review.Outcome.Should().Be(ModelHandoffOutcome.Stale);
        review.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        review.Offer.Should().BeNull();
        await review.DisposeAsync();
    }

    [Fact]
    public async Task NativePreviewIncludesTheCompleteExactMaximumEnvelopeWithoutTruncation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var overhead = f.Context(text: string.Empty).Serialize().Length;
        var text = new string('a', ModelContextEnvelope.MaximumInputUtf8Bytes - overhead);
        var offer = await Offer(f, policy, f.Context(text: text));
        var source = new ModelHandoffPresentation(f.Workflow);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        Encoding.UTF8.GetByteCount(review.Preview).Should().Be(ModelContextEnvelope.MaximumInputUtf8Bytes);
        review.Preview.Should().Contain(text);
        review.Preview.Should().Be(Encoding.UTF8.GetString(offer.Context.Serialize()));
    }

    [Fact]
    public async Task EligibilityLossAfterExactInspectionRetiresTheReturnedOfferBeforePublication()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var source = new ModelHandoffPresentation(f.Workflow);
        var checks = 0;
        await using var review = source.Open(offer, RequestOrigin.LocalUi, () => ++checks < 3)!;
        await review.Refresh(Token);
        review.Offer.Should().BeNull();
        review.HasReviewed.Should().BeFalse();
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task DismissedNativeLifetimeCancelsTheSameQuestionWithoutReopeningDisplayAdmission()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var offer = await Offer(f, await Configure(f));
        var source = new ModelHandoffPresentation(f.Workflow);
        var visible = true;
        await using var review = source.Open(offer, RequestOrigin.LocalUi, () => visible)!;
        await review.Refresh(Token);
        visible = false;
        await review.Dismiss(Token);
        review.Offer.Should().BeNull();
        review.Preview.Should().BeEmpty();
        review.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        f.Questions.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Cancelled);
    }
}
