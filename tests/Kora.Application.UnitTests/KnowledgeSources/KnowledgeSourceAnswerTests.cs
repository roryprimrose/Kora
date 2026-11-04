using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Context;
using Kora.Application.KnowledgeSources;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.KnowledgeSources;

[Collection("Host tracing")]
public sealed class KnowledgeSourceAnswerTests : IDisposable
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ChosenNamedEvidenceUsesOriginalQuestionAndOnlySelectedFilePolicyAndModelDisclosure(RequestOrigin origin)
    {
        using var root = Root(origin);
        using var f = new Fixture();
        var retrieved = (await f.Prepare()).Review!;
        var selected = retrieved.SelectEvidence([retrieved.Search.Citations[1]]);
        f.ModelCalls.Should().Be(0);
        f.PolicySources.Should().BeEmpty();
        LocalFileAnswerResult result;
        using (Root()) { result = await f.Workflow.AnswerAsync(selected, () => f.Eligible, Token); }
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        result.Claims.Single().Citations.Should().Equal(selected.Search.Citations);
        f.PolicySources.Should().Equal(f.Revision.Files[1]);
        f.Context!.Evidence.Should().ContainSingle().Which.Content.Should().Be(f.Revision.Files[1].Text);
        f.Context.UserRequest.Should().Be(retrieved.Question);
        f.Context.Request.Should().BeSameAs(retrieved.Target.Request);
        f.Context.Request.Origin.Should().Be(origin);
        f.Context.Tools.Should().BeEmpty();
        f.OriginalReads.Should().Be(0);
        (await f.Workflow.AnswerAsync(retrieved, () => true, Token)).Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        (await f.Workflow.AnswerAsync(selected, () => true, Token)).Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        f.ModelCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("expired")]
    [InlineData("privacy")]
    public async Task ChosenReviewCannotSubstituteForeignRetrievalOrRenewAuthority(string defect)
    {
        using var root = Root();
        using var f = new Fixture();
        var retrieved = (await f.Prepare()).Review!;
        var selected = retrieved.SelectEvidence([retrieved.Search.Citations[0]]);
        if (defect is "foreign")
        {
            selected = new KnowledgeSourceAnswerReview(retrieved.Source, retrieved.Revision, retrieved.Question,
                retrieved.RetrievedSearch, retrieved.Target).SelectEvidence([retrieved.Search.Citations[0]]);
        }
        if (defect is "stale") { retrieved.InvalidateSelection(); }
        if (defect is "expired") { f.Clock.Now = retrieved.ExpiresAt; }
        if (defect is "privacy") { f.Eligible = false; }
        var result = await f.Workflow.AnswerAsync(selected, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        result.Claims.Should().BeEmpty();
        f.PolicySources.Should().BeEmpty();
        f.ModelCalls.Should().Be(0);
        f.Admissions.Should().Be(0);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("admission")]
    [InlineData("model")]
    public async Task ChangedChosenSetAcrossAwaitCannotDispatchOrPublishOldSelection(string stage)
    {
        using var root = Root();
        using var f = new Fixture();
        var retrieved = (await f.Prepare()).Review!;
        var selected = retrieved.SelectEvidence([retrieved.Search.Citations[0]]);
        f.At = actual => { if (string.Equals(actual, stage, StringComparison.Ordinal)) { retrieved.InvalidateSelection(); } };
        var result = await f.Workflow.AnswerAsync(selected, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        result.Claims.Should().BeEmpty();
        f.ModelCalls.Should().Be(stage is "model" ? 1 : 0);
        if (stage is not "model") { f.Context.Should().BeNull(); }
    }

    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public KnowledgeSourceAnswerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task FreshQuestionReviewsOnlySelectedRetainedSourceThenSeparatelyConsumesOnePrivateLocalTurn(RequestOrigin origin)
    {
        using var root = Root(origin);
        using var f = new Fixture(correlated: origin == RequestOrigin.ActivatedVoice);
        var historical = f.Revision.Review.Request;
        var prepared = await f.Workflow.PrepareAsync(f.Source, "  alpha question?  ", () => f.Eligible, Token);
        var review = prepared.Review!;
        prepared.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        review.Question.Should().Be("  alpha question?  ");
        review.Revision.Should().BeSameAs(f.Revision);
        review.Revision.Review.Request.Should().BeSameAs(historical);
        review.Target.SourceRequest.Should().BeSameAs(HostActivity.RequireCurrent().Request);
        review.Target.SourceRequest.Should().NotBeSameAs(historical);
        review.Target.Request.Origin.Should().Be(origin);
        f.ModelCalls.Should().Be(0);
        f.PolicySources.Should().BeEmpty();
        LocalFileAnswerResult result;
        using (Root()) { result = await f.Workflow.AnswerAsync(review, () => f.Eligible, Token); }
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        result.ExecutionState.Should().Be(HostTaskState.Succeeded);
        result.Claims.Single().Citations.Should().Equal(review.Search.Citations);
        result.Claims.Single().Citations.Select(item => item.DisplayIdentity)
            .Should().Equal(@"A\notes.md", @"B\notes.md");
        f.Context!.UserRequest.Should().Be(review.Question);
        f.Context.Tools.Should().BeEmpty();
        f.Context.ToolMode.Should().Be(ModelToolMode.None);
        f.Context.Evidence.Should().OnlyContain(item => item.Disclosure == ModelEvidenceDisclosure.LocalOnly);
        f.Context.Evidence.Select(item => item.Content).Should().Equal(f.Revision.Files.Select(item => item.Text));
        f.PolicySources.Should().Equal(f.Revision.Files);
        f.Requests.Should().OnlyContain(item => item == f.Source);
        f.OriginalReads.Should().Be(0);
        foreach (var citation in result.Claims.Single().Citations)
        { review.Revision.ResolveCitation(citation).Text.Should().Be(citation.Excerpt); }
        f.OriginalReads.Should().Be(0);
        var repeated = await f.Workflow.AnswerAsync(review, () => true, Token);
        repeated.Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        f.ModelCalls.Should().Be(1);
        string.Join(' ', f.Events.Select(item => item.ActionId + item.TargetId + item.ReasonCode))
            .Should().NotContain("alpha").And.NotContain("notes.md");
    }

    [Theory]
    [InlineData("absent", LocalFileAnswerOutcome.NoEvidence)]
    [InlineData("!!!", LocalFileAnswerOutcome.InvalidQuestion)]
    [InlineData("", LocalFileAnswerOutcome.InvalidQuestion)]
    public async Task NoEvidenceOrInvalidQuestionHasNoPolicyContextOrModelDispatch(string question, LocalFileAnswerOutcome expected)
    {
        using var root = Root();
        using var f = new Fixture();
        var result = await f.Workflow.PrepareAsync(f.Source, question, () => true, Token);
        result.Outcome.Should().Be(expected);
        result.Review.Should().BeNull();
        f.ModelCalls.Should().Be(0);
        f.Admissions.Should().Be(0);
        f.PolicySources.Should().BeEmpty();
        f.Context.Should().BeNull();
        if (expected == LocalFileAnswerOutcome.NoEvidence) { f.DisposedSelections.Should().Be(1); }
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("remove")]
    [InlineData("refresh")]
    [InlineData("close")]
    public async Task RevocationJoinsIgnoringModelAndTurnDisposalBeforeMutationOrSourceOwnershipRelease(string operation)
    {
        using var root = Root();
        using var f = new Fixture { DelayModel = true, DelayDisposal = true };
        var review = (await f.Prepare()).Review!;
        var answer = f.Workflow.AnswerAsync(review, () => f.Eligible, Token);
        await f.ModelEntered.Task.WaitAsync(Token);
        Task mutation = operation switch
        {
            "disable" => f.Manager.DisableAsync(f.Source, () => true, Token),
            "remove" => f.Manager.RemoveAsync(f.Source, f.Clock.GetUtcNow(), () => true, Token),
            "refresh" => f.Manager.ReviewAsync(f.Source.Name, f.Source.Folder.CanonicalPath, f.Source, () => true, Token),
            _ => f.Manager.RevokeAnswerUsesAsync(),
        };
        await f.ModelCancelled.Task.WaitAsync(Token);
        review.IsRevoked.Should().BeTrue();
        mutation.IsCompleted.Should().BeFalse();
        answer.IsCompleted.Should().BeFalse();
        f.DisposedSelections.Should().Be(0);
        f.Mutations.Should().Be(0);
        f.ModelRelease.SetResult();
        await f.DisposeEntered.Task.WaitAsync(Token);
        mutation.IsCompleted.Should().BeFalse();
        f.DisposedSelections.Should().Be(0);
        f.DisposeRelease.SetResult();
        await FluentActions.Awaiting(() => answer).Should().ThrowAsync<OperationCanceledException>();
        await mutation;
        f.DisposedSelections.Should().Be(1);
        f.Mutations.Should().Be(operation is "close" ? 0 : 1);
        f.Context.Should().NotBeNull();
        f.Events.Where(item => item.ActionId is "knowledge.answer.local").Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Cancelled);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("admission")]
    [InlineData("model")]
    public async Task AccessOrMembershipLossAtEveryAsyncConsumerBoundarySuppressesPublication(string stage)
    {
        using var root = Root();
        using var f = new Fixture();
        var review = (await f.Prepare()).Review!;
        f.At = actual => { if (string.Equals(actual, stage, StringComparison.Ordinal)) { f.InvalidAccess = true; } };
        var result = await f.Workflow.AnswerAsync(review, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        result.Claims.Should().BeEmpty();
        review.IsRevoked.Should().BeTrue();
        f.ModelCalls.Should().Be(stage is "model" ? 1 : 0);
        if (stage is not "model") { f.Context.Should().BeNull(); }
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("admission")]
    [InlineData("model")]
    public async Task PrivacyOrOwnershipLossAcrossAwaitNeverPublishesLateOutput(string stage)
    {
        using var root = Root();
        using var f = new Fixture();
        var review = (await f.Prepare()).Review!;
        f.At = actual => { if (string.Equals(actual, stage, StringComparison.Ordinal)) { f.Eligible = false; } };
        var result = await f.Workflow.AnswerAsync(review, () => f.Eligible, Token);
        result.Claims.Should().BeEmpty();
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Stale);
        f.ModelCalls.Should().Be(stage is "model" ? 1 : 0);
    }

    [Theory]
    [InlineData(LocalFileModelUseDecision.Denied, LocalFileAnswerOutcome.Denied)]
    [InlineData(LocalFileModelUseDecision.Unavailable, LocalFileAnswerOutcome.SourceUseUnavailable)]
    [InlineData(LocalFileModelUseDecision.SourceUsePolicyMissing, LocalFileAnswerOutcome.SourceUseUnavailable)]
    public async Task EveryEvidenceBearingCompleteFileMustPassSharedPolicyBeforeContext(LocalFileModelUseDecision decision,
        LocalFileAnswerOutcome expected)
    {
        using var root = Root();
        using var f = new Fixture { SecondDecision = decision };
        var result = await f.Workflow.AnswerAsync((await f.Prepare()).Review!, () => true, Token);
        result.Outcome.Should().Be(expected);
        result.SourceUseDecision.Should().Be(decision);
        f.PolicySources.Should().Equal(f.Revision.Files);
        f.Context.Should().BeNull();
        f.Admissions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RiskOutsideMatchingExcerptStillDeniesWholeEvidenceFile(bool chosen)
    {
        using var root = Root();
        using var f = new Fixture(secondText: "alpha evidence\n\npassword=synthetic-secret");
        var review = (await f.Prepare()).Review!;
        if (chosen)
        {
            review = review.SelectEvidence([review.Search.Citations.Single(item =>
                string.Equals(item.DisplayIdentity, @"B\notes.md", StringComparison.Ordinal))]);
        }
        var result = await f.Workflow.AnswerAsync(review, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        f.ModelCalls.Should().Be(0);
        f.Context.Should().BeNull();
        f.PolicySources.Should().Equal(chosen ? [f.Revision.Files[1]] : f.Revision.Files);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("membership")]
    [InlineData("read")]
    [InlineData("inspect")]
    [InlineData("disabled")]
    public async Task ChangedOrUnavailableRetainedSourcesNeverFallBackOrRecapture(string defect)
    {
        using var root = Root();
        using var f = new Fixture();
        if (defect is "revision") { f.ChangedRevision = true; }
        if (defect is "membership") { f.WrongMembership = true; }
        if (defect is "read" or "inspect") { f.FailureStage = defect; }
        if (defect is "disabled") { f.Enabled = false; }
        var prepared = await f.Prepare();
        prepared.Outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
        f.ModelCalls.Should().Be(0);
        f.OriginalReads.Should().Be(0);
        f.Context.Should().BeNull();
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("revoked")]
    [InlineData("foreign-review")]
    [InlineData("origin")]
    [InlineData("gate")]
    public async Task StaleOrForeignConsentHasNoModelEffect(string defect)
    {
        using var root = Root();
        using var f = new Fixture();
        var review = (await f.Prepare()).Review!;
        switch (defect)
        {
            case "expiry": f.Clock.Now = review.ExpiresAt; break;
            case "revoked": review.Revoke(); break;
            case "foreign-review": review = new(review.Source, review.Revision, review.Question, review.Search, review.Target); break;
            case "gate": f.Eligible = false; break;
        }
        using var input = Root(defect is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        (await f.Workflow.AnswerAsync(review, () => f.Eligible, Token)).Outcome.Should().Be(LocalFileAnswerOutcome.Denied);
        f.Admissions.Should().Be(0);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("state")]
    [InlineData("reason")]
    [InlineData("no-model")]
    [InlineData("outcome")]
    [InlineData("invalid-reason")]
    [InlineData("different-outcome")]
    [InlineData("foreign-id")]
    [InlineData("duplicate-id")]
    [InlineData("hosted")]
    [InlineData("malformed")]
    public async Task ForgedReceiptOrCitationCannotBecomeRetainedClaims(string defect)
    {
        using var root = Root();
        using var f = new Fixture { ReceiptDefect = defect };
        var result = await f.Workflow.AnswerAsync((await f.Prepare()).Review!, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.InvalidResponse);
        result.Claims.Should().BeEmpty();
        f.ModelCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable, LocalFileAnswerOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Denied, LocalFileAnswerOutcome.Denied)]
    [InlineData(ModelTurnOutcome.Cancelled, LocalFileAnswerOutcome.Cancelled)]
    public async Task MissingRuntimeOrDeniedAdmissionIsTruthfulWithoutProviderFallback(ModelTurnOutcome outcome, LocalFileAnswerOutcome expected)
    {
        using var root = Root();
        using var f = new Fixture { Rejection = outcome };
        var result = await f.Workflow.AnswerAsync((await f.Prepare()).Review!, () => true, Token);
        result.Outcome.Should().Be(expected);
        f.ModelCalls.Should().Be(0);
        f.Context.Should().BeNull();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("terminal")]
    [InlineData("gate")]
    [InlineData("disposed")]
    public async Task AnswerReviewAndNativeUseIndependentlyRequireOriginalLiveHostAdmission(string defect)
    {
        var request = HostRequest.Create(defect is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (defect is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var f = new Fixture();
        if (defect is "terminal") { root.Complete(HostOperationOutcome.Completed); }
        if (defect is "gate") { f.Eligible = false; }
        if (defect is "disposed") { f.Manager.Dispose(); }
        var create = () => f.Manager.OpenAnswerUseAsync(f.Source, () => f.Eligible, Token);
        await create.Should().ThrowAsync<InvalidOperationException>();
        if (defect is not "disposed")
        { (await f.Prepare()).Outcome.Should().Be(LocalFileAnswerOutcome.Denied); }
        f.ModelCalls.Should().Be(0);
    }

    [Fact]
    public async Task NativeSourceOwnerRefusesConcurrentAcquisitionAndWorkflowDoesNotTakeAnOthersLease()
    {
        using var root = Root();
        using var f = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.InspectWait = release.Task;
        f.At = stage => { if (stage is "inspect") { entered.TrySetResult(); } };
        var opening = f.Manager.OpenAnswerUseAsync(f.Source, () => true, Token);
        await entered.Task.WaitAsync(Token);
        await FluentActions.Awaiting(() => f.Manager.OpenAnswerUseAsync(f.Source, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        (await f.Prepare()).Outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
        release.SetResult();
        var use = await opening;
        await use.ValidateAsync(Token);
        f.DisposedSelections.Should().Be(0);
        await f.Manager.RevokeAnswerUsesAsync();
        f.DisposedSelections.Should().Be(1);
    }

    [Fact]
    public async Task NativeUseOwnsOneWorkerAndDisposalIsIdempotentOnlyAfterActualJoin()
    {
        using var root = Root();
        using var f = new Fixture();
        var use = await f.Manager.OpenAnswerUseAsync(f.Source, () => true, Token);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = use.RunAsync(async _ => { entered.SetResult(); await release.Task; return 42; }, Token);
        await entered.Task.WaitAsync(Token);
        await FluentActions.Awaiting(() => use.RunAsync(_ => Task.FromResult(1), Token)).Should().ThrowAsync<InvalidOperationException>();
        var dispose = () => use.Dispose();
        dispose.Should().Throw<InvalidOperationException>();
        f.DisposedSelections.Should().Be(0);
        release.SetResult();
        (await work).Should().Be(42);
        await use.DisposeAsync();
        await use.DisposeAsync();
        use.Dispose();
        f.DisposedSelections.Should().Be(1);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("source")]
    [InlineData("metadata")]
    public async Task NativeUseChecksCompleteEnabledSnapshotBindingBeforeAnyModel(string defect)
    {
        using var root = Root();
        using var f = new Fixture();
        var source = defect switch
        {
            "state" => f.Source with { State = KnowledgeSourceState.Disabled },
            "source" => f.Other,
            _ => f.Source with { Folder = new(f.Source.Folder.CanonicalPath, "changed", f.Source.Folder.Scope,
                [new(f.Source.Folder.CanonicalPath, "changed"), .. f.Source.Folder.Directories.Skip(1)], f.Source.Folder.Files) },
        };
        await using var use = f.NewUse(source);
        await FluentActions.Awaiting(() => use.ValidateAsync(Token)).Should().ThrowAsync<InvalidDataException>();
        use.IsCurrent.Should().BeFalse();
        f.ModelCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("audit")]
    [InlineData("unexpected")]
    public async Task FailedRiskOrTerminalEvidenceNeverReturnsSuccessShapedClaims(string failure)
    {
        using var root = Root();
        using var f = new Fixture();
        var review = (await f.Prepare()).Review!;
        f.FailureStage = failure;
        if (failure is "policy")
        {
            (await f.Workflow.AnswerAsync(review, () => true, Token)).Outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
            f.Context.Should().BeNull();
        }
        else
        {
            var answer = () => f.Workflow.AnswerAsync(review, () => true, Token);
            if (failure is "audit") { await answer.Should().ThrowAsync<IOException>(); }
            else { await answer.Should().ThrowAsync<ArgumentException>(); }
        }
    }

    [Theory]
    [InlineData("review")]
    [InlineData("local")]
    public async Task UnavailableSourceDiagnosticsContainOnlyFailureTypeWithoutPrivateContentOrModelDispatch(string operation)
    {
        using var root = Root();
        var logger = new RecordingLogger();
        using var f = new Fixture(logger: logger);
        LocalFileAnswerOutcome outcome;
        if (operation is "review")
        {
            f.FailureStage = "inspect";
            var prepared = await f.Prepare();
            prepared.Review.Should().BeNull();
            outcome = prepared.Outcome;
        }
        else
        {
            var review = (await f.Prepare()).Review!;
            f.FailureStage = "policy";
            var result = await f.Workflow.AnswerAsync(review, () => true, Token);
            result.Claims.Should().BeEmpty();
            outcome = result.Outcome;
        }

        outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
        logger.Level.Should().Be(LogLevel.Warning);
        logger.EventId.Id.Should().Be(1);
        logger.Exception.Should().BeNull();
        logger.Entries.Should().ContainSingle().Which.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
            .Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["FailureType"] = nameof(IOException),
                ["{OriginalFormat}"] = "Named source answer unavailable. Failure type: {FailureType}",
            });
        f.ModelCalls.Should().Be(0);
        f.Admissions.Should().Be(0);
        f.Context.Should().BeNull();
        f.OriginalReads.Should().Be(0);
        f.DisposedSelections.Should().Be(0);
        await f.Manager.RevokeAnswerUsesAsync();
        f.DisposedSelections.Should().Be(operation is "review" ? 0 : 1);
        f.Events.Where(item => string.Equals(item.ActionId, "knowledge.answer." + operation, StringComparison.Ordinal))
            .Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task NonEvidenceFilesAreNotDisclosedAndCancellationBeforePolicyCannotDispatch()
    {
        using var root = Root();
        using var f = new Fixture(secondText: "unrelated text");
        var review = (await f.Prepare()).Review!;
        (await f.Workflow.AnswerAsync(review, () => true, Token)).Outcome.Should().Be(LocalFileAnswerOutcome.Succeeded);
        f.PolicySources.Should().Equal(f.Revision.Files[0]);
        f.Context!.Evidence.Should().ContainSingle();
        var next = (await f.Prepare()).Review!;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await FluentActions.Awaiting(() => f.Workflow.AnswerAsync(next, () => true, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        f.ModelCalls.Should().Be(1);
    }

    [Fact]
    public async Task CompleteSerializedInputLimitRejectsAllEightReviewedExcerptsBeforeModelExecution()
    {
        using var root = Root();
        using var f = new Fixture(fileCount: 8, textOverride: "alpha " + new string('"', 2042));
        var review = (await f.Prepare()).Review!;
        review.Search.Citations.Should().HaveCount(8);
        review.ExcerptUtf8Bytes.Should().Be(16 * 1024);
        var result = await f.Workflow.AnswerAsync(review, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.ContextLimit);
        f.PolicySources.Should().HaveCount(8);
        f.Admissions.Should().Be(1);
        f.ModelCalls.Should().Be(0);
        f.Context.Should().BeNull();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("generation")]
    public async Task ForeignAdmittedTurnCannotRebindNamedEvidence(string defect)
    {
        using var root = Root();
        using var f = new Fixture { LeaseDefect = defect };
        var result = await f.Workflow.AnswerAsync((await f.Prepare()).Review!, () => true, Token);
        result.Outcome.Should().Be(LocalFileAnswerOutcome.Unavailable);
        f.ModelCalls.Should().Be(0);
    }

    private sealed class RecordingLogger : ILogger<KnowledgeSourceAnswerWorkflow>
    {
        internal List<IReadOnlyList<KeyValuePair<string, object?>>> Entries { get; } = [];
        internal LogLevel Level { get; private set; }
        internal EventId EventId { get; private set; }
        internal Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            EventId = eventId;
            Exception = exception;
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToArray());
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IKnowledgeSourceInspector, IKnowledgeSourceStore, ILocalFileModelUsePolicy,
        IModelContextRunner, ISecurityAuditLog, IDisposable
    {
        internal readonly Clock Clock = new();
        internal readonly KnowledgeSource Source;
        internal readonly KnowledgeSource Other;
        internal readonly KnowledgeSourceRevision Revision;
        internal readonly KnowledgeSourceManager Manager;
        internal readonly KnowledgeSourceAnswerWorkflow Workflow;
        internal readonly List<LocalFileRevision> PolicySources = [];
        internal readonly List<KnowledgeSource> Requests = [];
        internal readonly List<SecurityAuditEvent> Events = [];
        internal readonly TaskCompletionSource ModelEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ModelRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ModelCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Eligible = true;
        internal bool Enabled = true;
        internal bool InvalidAccess;
        internal bool WrongMembership;
        internal bool ChangedRevision;
        internal bool DelayModel;
        internal bool DelayDisposal;
        internal string? FailureStage;
        internal string? ReceiptDefect;
        internal string? LeaseDefect;
        internal LocalFileModelUseDecision? SecondDecision;
        internal ModelTurnOutcome? Rejection;
        internal Action<string>? At;
        internal Task? InspectWait;
        internal int OriginalReads;
        internal int DisposedSelections;
        internal int Admissions;
        internal int ModelCalls;
        internal int Mutations;
        internal ModelContextEnvelope? Context;

        internal Fixture(string secondText = "alpha second retained evidence", int fileCount = 2, string? textOverride = null,
            bool correlated = false, ILogger<KnowledgeSourceAnswerWorkflow>? logger = null)
        {
            const string root = @"C:\SyntheticNamedSource";
            string[] texts = textOverride is null ? ["alpha first retained evidence", secondText]
                : Enumerable.Repeat(textOverride, fileCount).ToArray();
            var metadata = new KnowledgeSourceMetadata(root, "root", KnowledgeSourceScope.BoundedDescendants,
                [new(root, "root"), new(root + @"\A", "A"), new(root + @"\B", "B")],
                texts.Select((text, index) => new LocalFileMetadata(root + (fileCount == 2 ? index == 0 ? @"\A\notes.md" : @"\B\notes.md"
                    : $@"\{index:D2}.md"),
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8.GetByteCount(text), Clock.Now)));
            Source = new(Guid.NewGuid(), 3, "Team", metadata, KnowledgeSourceState.Enabled, Clock.Now, 100);
            Other = Source with { Id = Guid.NewGuid(), Name = "Other" };
            var historical = new KnowledgeSourceReview(Guid.NewGuid(), Source.Id, HostRequest.Create(RequestOrigin.LocalUi), metadata,
                correlated ? HostActivity.RequireCurrent().Activity!.Context : default);
            Revision = new(historical, metadata.Files.Select((file, index) => new LocalFileRevision(
                new(historical.ReviewId, historical.SourceId, historical.Request, file, historical.Cause), Encoding.UTF8.GetBytes(texts[index]), Clock.Now)));
            Manager = new(this, this, this, Clock);
            Workflow = new(Manager, new LocalFileLexicalRetrieval(), this, this, this, Clock,
                logger ?? NullLogger<KnowledgeSourceAnswerWorkflow>.Instance);
        }

        internal Task<KnowledgeSourceAnswerPreparation> Prepare() =>
            Workflow.PrepareAsync(Source, "alpha?", () => Eligible, Token);
        internal KnowledgeSourceAnswerUse NewUse(KnowledgeSource source) =>
            new(source, Revision, new Selection(this), this, () => Eligible, Clock);

        public async Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope, CancellationToken cancellationToken)
        {
            At?.Invoke("inspect");
            if (InspectWait is not null) { await InspectWait; }
            if (FailureStage is "inspect") { throw new IOException("synthetic unavailable"); }
            return new Selection(this);
        }
        private sealed class Selection(Fixture owner) : IKnowledgeSourceSelection
        {
            public KnowledgeSourceMetadata Metadata => owner.WrongMembership ? new(owner.Source.Folder.CanonicalPath,
                "changed", owner.Source.Folder.Scope, [new(owner.Source.Folder.CanonicalPath, "changed")], owner.Source.Folder.Files)
                : owner.Source.Folder;
            public Task ValidateAsync(CancellationToken cancellationToken)
            {
                owner.At?.Invoke("validate");
                if (owner.InvalidAccess) { throw new InvalidDataException("synthetic changed access"); }
                return Task.CompletedTask;
            }
            public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
            { owner.OriginalReads++; throw new InvalidOperationException("Named answering must never capture original bytes."); }
            public void Dispose() => owner.DisposedSelections++;
        }
        public Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeSource>>([Source, Other]);
        public Task<KnowledgeSourceRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        {
            Requests.Add(exact);
            if (!Enabled || exact != Source || FailureStage is "read") { throw new InvalidOperationException("Unavailable exact source"); }
            return Task.FromResult(ChangedRevision
                ? new KnowledgeSourceRevision(Revision.Review, Revision.Files, new(Source.Id, Guid.NewGuid())) : Revision);
        }
        public Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { Mutations++; Enabled = false; return Task.FromResult(exact with { Version = exact.Version + 1, State = KnowledgeSourceState.Unavailable }); }
        public Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { Mutations++; Enabled = false; return Task.CompletedTask; }
        public Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        { Mutations++; Enabled = false; return Task.CompletedTask; }
        public Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, KnowledgeSourceRevision snapshot,
            IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
            LocalFileSearchResult excerpts, CancellationToken cancellationToken)
        {
            PolicySources.Add(source);
            At?.Invoke("policy");
            if (FailureStage is "policy") { throw new IOException("synthetic source policy failure"); }
            if (FailureStage is "unexpected") { throw new ArgumentException("synthetic unexpected failure"); }
            return PolicySources.Count == 2 && SecondDecision is { } denied ? denied
                : await LocalSourceUsePolicy.Instance.AdmitAsync(source, question, excerpts, cancellationToken);
        }
        public async Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null)
        {
            Admissions++;
            At?.Invoke("admission");
            if (validateSource is not null) { await validateSource(token); }
            return Rejection is { } rejected ? new(new LocalContextTurnReceipt(target.Request, rejected, LocalContextTurnReason.SessionNotCurrent))
                : new(new Turn(this, LeaseDefect is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : target.Request));
        }
        private sealed class Turn(Fixture owner, HostRequest request) : IAdmittedModelContextTurn
        {
            public HostRequest Request => request;
            public HostRevision SessionGeneration => owner.LeaseDefect is "generation" ? default : new(1);
            public Task<LocalContextTurnReceipt> Completion { get; private set; } =
                Task.FromResult(new LocalContextTurnReceipt(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled));
            public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token) =>
                Completion = owner.Run(context, token);
            public async ValueTask DisposeAsync()
            {
                owner.DisposeEntered.TrySetResult();
                if (owner.DelayDisposal) { await owner.DisposeRelease.Task; }
            }
        }
        private async Task<LocalContextTurnReceipt> Run(ModelContextEnvelope context, CancellationToken token)
        {
            Context = context;
            ModelCalls++;
            using var registration = token.Register(() => ModelCancelled.TrySetResult());
            ModelEntered.TrySetResult();
            if (DelayModel) { await ModelRelease.Task; }
            At?.Invoke("model");
            var ids = context.Evidence.Select(item => item.Id.Value).ToArray();
            var answer = ReceiptDefect switch
            {
                "foreign-id" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "synthetic", evidenceIds = new[] { Guid.NewGuid() } } } }),
                "duplicate-id" => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "synthetic", evidenceIds = new[] { ids[0], ids[0] } } } }),
                "malformed" => "not json",
                _ => JsonSerializer.Serialize(new { unsupported = false, claims = new[] { new { text = "synthetic supported claim", evidenceIds = ids } } }),
            };
            var model = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                new(new(Guid.NewGuid()), context.Request, ReceiptDefect is "hosted"
                    ? ModelProviderSelection.CopilotCandidate : ModelProviderSelection.OllamaCandidate), new(answer, null));
            var receipt = new LocalContextTurnReceipt(context.Request, ModelTurnOutcome.Succeeded,
                LocalContextTurnReason.Completed, HostTaskState.Succeeded, model);
            return ReceiptDefect switch
            {
                "request" => receipt with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
                "state" => receipt with { DurableState = HostTaskState.Unknown },
                "reason" => receipt with { Reason = LocalContextTurnReason.Cancelled },
                "no-model" => receipt with { ModelResult = null },
                "outcome" => receipt with { Outcome = (ModelTurnOutcome)99 },
                "invalid-reason" => receipt with { Reason = (LocalContextTurnReason)99 },
                "different-outcome" => receipt with { ModelResult = model with { Outcome = ModelTurnOutcome.Denied } },
                _ => receipt,
            };
        }
        public void Write(SecurityAuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (FailureStage is "audit" && auditEvent.ActionId is "knowledge.answer.local"
                && auditEvent.Outcome != SecurityAuditOutcome.Requested)
            { throw new IOException("synthetic terminal evidence failure"); }
        }
        public void Dispose() => Manager.Dispose();
    }
}
