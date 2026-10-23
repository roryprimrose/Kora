using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task AttachmentAnswerRunsOneLocalToolFreeTurnAndRetainsVerifiedReferencesInExactConversation(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        f.SetAttachment("alpha exact marker");
        var workflow = f.AttachmentWorkflow(new(), new());
        var host = f.QualifiedAttachmentHost();
        var service = f.AttachmentConversationService(workflow, host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        using var review = await service.PrepareAttachmentAnswerAsync(target, f.Attachment!, "alpha", static () => true, Token, origin);
        var reference = f.Attachment!.File.Reference;
        f.Adapter.OnRun = () =>
        {
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
            wire.RootElement.GetProperty("Evidence")[0].GetProperty("Request")
                .GetProperty("RequestId").GetProperty("Value").GetGuid()
                .Should().Be(f.Adapter.Provenance!.Request.RequestId.Value);
            var id = wire.RootElement.GetProperty("Evidence")[0].GetProperty("Id").GetProperty("Value").GetGuid();
            f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                new(JsonSerializer.Serialize(new
                {
                    unsupported = false,
                    claims = new[] { new { text = "The marker is alpha.", evidenceIds = new[] { id } } },
                }), null));
        };
        var receipt = await service.SubmitAttachmentAnswerAsync(review, static () => true, Token);
        receipt.Message.Body.Should().Be("alpha");
        receipt.Message.Request.Origin.Should().Be(origin);
        receipt.Message.Request.SessionId.Should().Be(target.Authority.SessionId);
        receipt.Message.AssistantResponse!.Body.Should().Contain("The marker is alpha.")
            .And.Contain(reference.RevisionId.ToString("D")).And.Contain(reference.Digest);
        receipt.Task.State.Should().Be(HostTaskState.Succeeded);
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(1);
        f.Messages.Should().NotContain(text => text.Contains("alpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachmentUnqualifiedRuntimeRetainsExplicitUnavailableWithoutAnswerOrFallback()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha retained source");
        var workflow = f.AttachmentWorkflow(new(), new());
        var host = f.ConfiguredProfiles(production: true);
        var service = f.AttachmentConversationService(workflow, host);
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        var receipt = await service.SubmitAttachmentAnswerAsync(review, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull();
        receipt.Task.IsTerminal.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("source-deleted")]
    [InlineData("policy-revoked")]
    [InlineData("metadata")]
    [InlineData("audit-commit")]
    [InlineData("readback")]
    [InlineData("invalid-json")]
    public async Task AttachmentLateSourceOrCommitFailuresNeverPublishUnverifiedAnswerOrRepeat(string fault)
    {
        using var f = new Fixture();
        f.SetAttachment("alpha exact marker");
        var policy = new AttachmentPolicy();
        var workflow = f.AttachmentWorkflow(policy, new());
        var host = f.QualifiedAttachmentHost();
        var service = f.AttachmentConversationService(workflow, host);
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        f.Adapter.OnRun = () =>
        {
            if (fault is "source-deleted") { f.Attachment = null; }
            if (fault is "policy-revoked") { policy.Decision = LocalFileModelUseDecision.Denied; }
            if (fault is "metadata") { f.Metadata = new(f.Session.SessionId, new(1), new("changed")); }
        };
        if (fault is "audit-commit") { f.BeforeExecutionComplete = () => throw new IOException("Commit unavailable"); }
        if (fault is "readback") { f.BadExecutionReadback = true; }
        if (fault is "invalid-json")
        {
            var receipt = await service.SubmitAttachmentAnswerAsync(review, static () => true, Token);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unknown);
            receipt.Message.AssistantResponse.Should().BeNull();
        }
        else
        {
            var failure = await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
                .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
            failure.Which.Request.Should().Be(f.Retained!.Request);
        }
        f.Adapter.Calls.Should().Be(1);
        f.Retained!.AssistantResponse.Should().BeNull();
    }

    [Fact]
    public async Task AttachmentCancellationSuppressesNoncooperativeLateOutputAndKeepsHostSlotUntilQuiescence()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha marker");
        var workflow = f.AttachmentWorkflow(new(), new());
        var host = f.QualifiedAttachmentHost();
        var service = f.AttachmentConversationService(workflow, host);
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<Kora.Application.Dependencies.ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.OnRun = () => entered.SetResult();
        f.Adapter.Pending = pending.Task;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var submission = service.SubmitAttachmentAnswerAsync(review, static () => true, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        cancellation.Cancel();
        await FluentActions.Awaiting(async () => await submission).Should()
            .ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        host.IsQuiescent.Should().BeFalse();
        f.Retained!.AssistantResponse.Should().BeNull();
        pending.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late forbidden output", null)));
        await host.DisposeAsync();
        f.Retained.AssistantResponse.Should().BeNull();
        f.ExecutionCompletions.Should().Be(0);
    }

    [Fact]
    public async Task AttachmentPostCommitAuditFailureIsExplicitlyUnconfirmedAndNeverReplayed()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha marker");
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(new(), audit);
        var host = f.ConfiguredProfiles(production: true);
        var service = f.AttachmentConversationService(workflow, host);
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        audit.OnWrite = value =>
        {
            if (value.ActionId is "session.attachment.answer.local" && value.Outcome != SecurityAuditOutcome.Requested)
            { throw new IOException("Terminal audit unavailable"); }
        };
        var failure = await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
            .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        failure.Which.Request.Should().Be(f.Retained!.Request);
        f.RetainCalls.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        f.Retained!.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Retained.AssistantResponse.Should().BeNull();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AttachmentCanonicalPreRetainSourceRefusalDoesNotBecomePreviewRequestUncertainty(int refusedCall)
    {
        using var f = new Fixture();
        f.SetAttachment("alpha marker");
        var policy = new AttachmentPolicy();
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(policy, audit);
        var service = f.AttachmentConversationService(workflow, f.ConfiguredProfiles(production: true));
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        policy.OnAdmit = () =>
        {
            if (policy.Calls == refusedCall) { policy.Decision = LocalFileModelUseDecision.Denied; }
        };
        await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
            .Should().ThrowAsync<SessionAttachmentAnswerUnavailableException>();
        policy.Calls.Should().Be(refusedCall);
        f.RetainCalls.Should().Be(0);
        f.Retained.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task AttachmentFailureReportingCannotReplaceCanonicalUncertainRequestWithPreviewIdentity()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha marker");
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(new(), audit);
        var service = f.AttachmentConversationService(workflow, f.QualifiedAttachmentHost());
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        f.AfterExecutionStart = () => f.Attachment = null;
        audit.OnWrite = value =>
        {
            if (value.ActionId is "session.attachment.answer.local" && value.Outcome != SecurityAuditOutcome.Requested)
            { throw new IOException("Failure audit unavailable"); }
        };
        var failure = await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
            .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        failure.Which.Request.Should().Be(f.Retained!.Request);
        failure.Which.InnerException.Should().BeOfType<AggregateException>();
        f.RetainCalls.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        f.Retained.AssistantResponse.Should().BeNull();
    }

    [Fact]
    public async Task AttachmentDeferredConsentLinksOriginalReviewWithoutPrivateActivityTags()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha private source marker");
        var stopped = new List<System.Diagnostics.Activity>();
        using var attachmentListener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity => stopped.Add(activity),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(attachmentListener);
        var service = f.AttachmentConversationService(f.AttachmentWorkflow(new(), new()), f.ConfiguredProfiles(production: true));
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        var cause = review.Cause;
        _ = await service.SubmitAttachmentAnswerAsync(review, static () => true, Token);
        stopped.Should().Contain(activity => activity.Links.Any(link => link.Context == cause));
        stopped.Should().OnlyContain(activity => activity.IsStopped && activity.IdFormat == System.Diagnostics.ActivityIdFormat.W3C);
        stopped.SelectMany(activity => activity.Tags).Select(tag => tag.Value)
            .Should().NotContain(value => value != null && (value.Contains("alpha", StringComparison.Ordinal)
                || value.Contains("Synthetic", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AttachmentEvidenceReviewReadsOnlyRetainedCopyAndDoesNotAdmitModelOrRetainConversation()
    {
        using var f = new Fixture();
        f.SetAttachment("# Source\nalpha exact marker");
        var policy = new AttachmentPolicy();
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(policy, audit);
        using var review = await workflow.Prepare(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        review.Question.Should().Be("alpha");
        review.Evidence.Citations.Single().Excerpt.Should().Be(f.Attachment!.File.Text);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        policy.Calls.Should().Be(0);
        audit.Events.Select(value => value.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        review.Dispose();
        review.Invoking(value => _ = value.Question).Should().Throw<InvalidOperationException>();
        review.Invoking(value => _ = value.Source).Should().Throw<InvalidOperationException>();
        review.Invoking(value => _ = value.Evidence).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachmentEvidenceCancellationAuditsCancellationWithoutDisclosureOrRetain(bool duringRead)
    {
        using var f = new Fixture();
        f.SetAttachment("alpha retained source");
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(new(), audit);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (duringRead) { f.OnRead = cancellation.Cancel; }
        else { cancellation.Cancel(); }
        await workflow.Invoking(value => value.Prepare(new(f.Session, null), f.Attachment!, "alpha",
            static () => true, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        audit.Events.Select(value => value.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Cancelled);
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AttachmentWorkflowUnavailableDoesNotConsumeReviewedSourceOrStartConversation()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha retained source");
        var workflow = f.AttachmentWorkflow(new(), new());
        using var review = await workflow.Prepare(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        var service = f.ConversationService(f.ConfiguredProfiles(production: true));
        await service.Invoking(value => value.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!,
            "alpha", static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        review.Evidence.Citations.Should().ContainSingle();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AttachmentFailureLoggingUsesOnlyExceptionTypeNotPrivateSource()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha private retained marker");
        f.AttachmentFault = "malformed";
        var workflow = f.AttachmentWorkflow(new(), new(), enabledLogger: true);
        await workflow.Invoking(value => value.Prepare(new(f.Session, null), f.Attachment!, "alpha",
            static () => true, Token)).Should().ThrowAsync<InvalidDataException>();
        f.Messages.Should().Contain(value => value.Contains(nameof(InvalidDataException), StringComparison.Ordinal))
            .And.NotContain(value => value.Contains("alpha", StringComparison.Ordinal)
                || value.Contains("retained marker", StringComparison.Ordinal)
                || value.Contains(@"C:\Synthetic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachmentUnsampledRequestUsesNoInventedCauseAndAuditsCancelledSubmission()
    {
        listener.Dispose();
        using var sampled = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) =>
                options.Name is "session.request" ? System.Diagnostics.ActivitySamplingResult.None
                    : System.Diagnostics.ActivitySamplingResult.AllData,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(sampled);
        using var f = new Fixture();
        f.SetAttachment("alpha retained source");
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(new(), audit);
        using var review = await workflow.Prepare(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        review.Cause.Should().Be(default(System.Diagnostics.ActivityContext));
        var request = new HostRequest(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var receipt = await workflow.Submit(review, (create, _, _, _, _) =>
        {
            create(request, f.Time.Now).Request.Should().BeSameAs(request);
            var message = new Kora.Core.Conversation.ConversationMessage(request, f.Session.Generation,
                1, 2, f.Time.Now, "alpha", ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled);
            message.Validate();
            return System.Threading.Tasks.Task.FromResult(new Kora.Core.Conversation.ConversationSubmissionReceipt(
                message, new(request, new(2), HostTaskState.Cancelled)));
        }, request, static () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AttachmentPostCompletionSourceRevocationKeepsExactUncertaintyAndCompletesFailureAudit()
    {
        using var f = new Fixture();
        f.SetAttachment("alpha retained source");
        var audit = new AttachmentAudit();
        var workflow = f.AttachmentWorkflow(new(), audit);
        var service = f.AttachmentConversationService(workflow, f.ConfiguredProfiles(production: true));
        using var review = await service.PrepareAttachmentAnswerAsync(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        audit.OnWrite = value =>
        {
            if (value.ActionId is "session.attachment.answer.local" && value.Outcome == SecurityAuditOutcome.Failed)
            { f.Attachment = null; }
        };
        var failure = await service.Invoking(value => value.SubmitAttachmentAnswerAsync(review, static () => true, Token))
            .Should().ThrowAsync<Kora.Core.Conversation.ConversationSubmissionUnconfirmedException>();
        failure.Which.Request.Should().Be(f.Retained!.Request);
        audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        f.Retained.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Retained.AssistantResponse.Should().BeNull();
        f.RetainCalls.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task AttachmentQuestionCharacterBoundaryRejectsWholeQuestionWithoutTruncation(int characters, bool accepted)
    {
        using var f = new Fixture();
        f.SetAttachment("alpha marker");
        var workflow = f.AttachmentWorkflow(new(), new());
        var question = string.Concat(Enumerable.Repeat("alpha ", 42)) + new string(' ', characters - 252);
        if (accepted)
        {
            using var review = await workflow.Prepare(new(f.Session, null), f.Attachment!, question, static () => true, Token);
            review.Question.Should().Be(question);
        }
        else
        {
            await workflow.Invoking(value => value.Prepare(new(f.Session, null), f.Attachment!, question, static () => true, Token))
                .Should().ThrowAsync<InvalidDataException>();
        }
        f.RetainCalls.Should().Be(0);
    }

    [Fact]
    public async Task AttachmentCompleteEscapedEnvelopeBudgetIsCheckedBeforeReviewDisclosureOrModelUse()
    {
        using var f = new Fixture();
        f.SetAttachment(string.Concat(Enumerable.Repeat(new string('\t', 1980) + "alpha\n", 8)));
        var workflow = f.AttachmentWorkflow(new(), new());
        await workflow.Invoking(value => value.Prepare(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("unretained")]
    [InlineData("replaced")]
    [InlineData("storage-revision")]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("revoked")]
    [InlineData("copy-held")]
    [InlineData("swap-uncertain")]
    [InlineData("body-removed")]
    [InlineData("secret")]
    [InlineData("no-match")]
    [InlineData("truncated")]
    [InlineData("bad-question")]
    [InlineData("invalid-unicode")]
    [InlineData("audit")]
    [InlineData("malformed")]
    public async Task InvalidAttachmentOrIncompleteEvidenceNeverAdmitsModelOrDisclosesReview(string fault)
    {
        using var f = new Fixture();
        var text = fault is "secret" ? "alpha api_key=synthetic-only-value"
            : fault is "truncated" ? string.Join("\n", Enumerable.Repeat(new string('a', 2040) + " alpha", 12))
            : "alpha exact marker";
        f.SetAttachment(text);
        var expected = f.Attachment!;
        var target = new SessionWorkspaceEntry(f.Session, null);
        if (fault is "foreign") { expected = expected with { Session = new(Guid.NewGuid()) }; }
        if (fault is "unretained") { f.Attachment = null; }
        if (fault is "replaced") { f.SetAttachment("alpha replacement"); }
        if (fault is "storage-revision") { f.Attachment = expected with { StorageRevision = new(2) }; }
        if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
        if (fault is "done") { f.Session = f.Session with { IsActive = false }; }
        f.AttachmentFault = fault;
        var audit = new AttachmentAudit { Fail = fault is "audit" };
        var workflow = f.AttachmentWorkflow(new(), audit);
        var question = fault is "no-match" ? "zeta" : fault is "bad-question" ? new string('x', 257)
            : fault is "invalid-unicode" ? "\ud800" : "alpha";
        await workflow.Invoking(value => value.Prepare(target, expected, question, () => fault is not "revoked", Token))
            .Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("control")]
    [InlineData("deleted")]
    [InlineData("replaced")]
    [InlineData("foreign-owner")]
    [InlineData("policy-unavailable")]
    [InlineData("policy-denied")]
    [InlineData("cancelled")]
    public async Task AttachmentModelUseRequiresFreshSeparateConsentAndSourcePolicyBeforeAnyRetain(string fault)
    {
        using var f = new Fixture();
        f.SetAttachment("alpha source");
        var policy = new AttachmentPolicy();
        var workflow = f.AttachmentWorkflow(policy, new());
        using var review = await workflow.Prepare(new(f.Session, null), f.Attachment!, "alpha", static () => true, Token);
        if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(3); }
        if (fault is "revoked") { review.Dispose(); }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "deleted") { f.Attachment = null; }
        if (fault is "replaced") { f.SetAttachment("alpha new source"); }
        if (fault is "foreign-owner") { workflow = f.AttachmentWorkflow(policy, new()); }
        if (fault is "policy-unavailable") { policy.Decision = LocalFileModelUseDecision.Unavailable; }
        if (fault is "policy-denied") { policy.Decision = LocalFileModelUseDecision.Denied; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancelled") { cancellation.Cancel(); }
        var calls = 0;
        await workflow.Invoking(value => value.Submit(review, (_, _, _, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("Must not submit");
        }, new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            static () => true, cancellation.Token)).Should().ThrowAsync<Exception>();
        calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    private sealed partial class Fixture : ISessionFileStore, Microsoft.Extensions.Logging.ILogger<SessionAttachmentAnswerWorkflow>
    {
        internal Kora.Application.Dependencies.ModelTurnHost QualifiedAttachmentHost()
        {
            ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
            ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local, [Profile()]);
            return ConfiguredProfiles();
        }
        internal SessionConversationService AttachmentConversationService(SessionAttachmentAnswerWorkflow workflow,
            Kora.Application.Dependencies.ModelTurnHost host) =>
            new(this, this, new(this, new(this), this, NullLogger<SessionWorkspaceService>.Instance),
                this, host, Time, this, host, attachmentAnswers: workflow);
        internal SessionFileAttachment? Attachment { get; set; }
        internal string AttachmentFault { get; set; } = string.Empty;
        internal void SetAttachment(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            Attachment = new(Session.SessionId, Session.Generation, new(1),
                new(new(Guid.NewGuid(), Guid.NewGuid(), Request,
                    new(@"C:\Synthetic\retained.md", "retained-identity", bytes.Length, Time.Now)), bytes, Time.Now));
        }
        internal SessionAttachmentAnswerWorkflow AttachmentWorkflow(AttachmentPolicy policy, AttachmentAudit audit,
            bool enabledLogger = false) =>
            new(new(this, new(this), this, NullLogger<SessionWorkspaceService>.Instance), this,
                new LocalFileLexicalRetrieval(), policy, Time, audit,
                enabledLogger ? this : NullLogger<SessionAttachmentAnswerWorkflow>.Instance);
        public ValueTask<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token) =>
            AttachmentFault is "malformed" ? throw new InvalidDataException("Malformed retained attachment")
                : ValueTask.FromResult(Attachment);
        public ValueTask<SessionFileRemoval> PreviewRemoval(HostId<SessionIdentity> session, CancellationToken token) =>
            ValueTask.FromResult(new SessionFileRemoval(Guid.NewGuid(), session, Session.Generation,
                Attachment?.StorageRevision ?? new(1), Attachment?.File.Reference
                    ?? new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64)), "inventory")
            {
                BodyRetained = AttachmentFault is not "body-removed",
                ReplacementCopyVerificationPending = AttachmentFault is "copy-held",
                ReplacementSwapUnconfirmed = AttachmentFault is "swap-uncertain",
            });
        public ValueTask<SessionFileAttachment> Attach(HostRequest request, HostRevision generation, LocalFileRevision revision,
            ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionFileAttachment> Replace(HostRequest request, SessionFileRemoval previous,
            LocalFileRevision revision, ReadOnlyMemory<byte> bytes, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask Remove(HostRequest request, SessionFileRemoval review, Func<bool> admitted, CancellationToken token) =>
            throw new NotSupportedException();
    }

    private sealed class AttachmentPolicy : ILocalFileModelUsePolicy
    {
        internal LocalFileModelUseDecision Decision { get; set; } = LocalFileModelUseDecision.Allowed;
        internal int Calls { get; private set; }
        internal Action? OnAdmit { get; set; }
        public Task<LocalFileModelUseDecision> AdmitAsync(LocalFileRevision source, string question,
            LocalFileSearchResult excerpts, CancellationToken token)
        {
            Calls++;
            OnAdmit?.Invoke();
            return System.Threading.Tasks.Task.FromResult(Decision);
        }
    }

    private sealed class AttachmentAudit : ISecurityAuditLog
    {
        internal bool Fail { get; set; }
        internal Action<SecurityAuditEvent>? OnWrite { get; set; }
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent value)
        {
            _ = HostActivity.RequireCurrent();
            if (Fail) { throw new IOException("Required audit unavailable"); }
            OnWrite?.Invoke(value);
            Events.Add(value);
        }
    }
}
