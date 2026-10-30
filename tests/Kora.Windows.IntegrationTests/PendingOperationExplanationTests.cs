using AwesomeAssertions;

using Kora.Application.Context;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PendingOperationExplanationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplanationAndStorageFailureNeverLogQuestionOrExceptionContent(bool fail)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            f.Proposal = new(f.Request, new(Guid.NewGuid()), new(1),
                ClipboardPermissionSourceUsePolicy.CreateReadBinding(), HostOperationEffect.BoundedRead, f.Time.Now.AddMinutes(5));
            await f.PublishAsync();
            var presented = await f.RunAsync(() => f.Authorization.RequestGrantAsync(f.Request, Spec(), f.Token));
            var logger = new RecordingLogger();
            var model = new NativeQuestionViewModel(presented.Question!, f.Questions, new(new FailureStore(f.Store, fail), f.Time),
                new(f.Store, f.Time), () => true, f.Time, logger, explainClipboardRead: true);
            await model.ExplainPolicyAsync();
            model.HasPolicyExplanation.Should().Be(!fail);
            model.PolicyExplanationText.Should().NotContain("private-body-marker");
            logger.Messages.Should().OnlyContain(message => !message.Contains("private-body-marker", StringComparison.Ordinal));
            logger.Exceptions.Should().OnlyContain(exception => exception == null);
            if (fail)
            {
                logger.Messages.Should().ContainSingle().Which.Should().Contain("System.IO.IOException");
                var completion = () => model.Completion;
                await completion.Should().ThrowAsync<IOException>();
                model.Status.Should().Contain("Interaction/storage/audit failed");
            }
            else { logger.Messages.Should().BeEmpty(); model.Close("Test presentation closed."); }
            (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
            f.Count("session_admissions").Should().Be(0);
            (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
        });
    }

    [Theory]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task CurrentConcurrentExactGrantIsIdentifiedWithoutClearingOriginalReviewOrUsingIt(string scope)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            f.Proposal = new(f.Request, new(Guid.NewGuid()), new(1),
                ClipboardPermissionSourceUsePolicy.CreateReadBinding(), HostOperationEffect.BoundedRead, f.Time.Now.AddMinutes(5));
            await f.PublishAsync();
            var originalRequest = f.Request;
            var presented = await f.RunAsync(() => f.Authorization.RequestGrantAsync(f.Request, Spec(), f.Token));
            var originalProposal = f.Proposal;
            f.Request = new(new(Guid.NewGuid()), originalRequest.SessionId, new(Guid.NewGuid()),
                RequestOrigin.LocalUi, new(Guid.NewGuid()));
            await f.AdmitAsync(newSession: false);
            f.Proposal = new(f.Request, new(Guid.NewGuid()), new(1), originalProposal.Binding,
                HostOperationEffect.BoundedRead, originalProposal.ExpiresAt);
            await f.PublishAsync();
            var concurrent = await f.RunAsync(() => f.Authorization.RequestGrantAsync(f.Request, Spec(), f.Token));
            var approved = await f.RunAsync(() => f.Authorization.ApproveAsync(concurrent.Question!.Key, new([scope]),
                RequestOrigin.LocalUi, f.Token));
            var grant = approved.Grant!;
            f.Request = originalRequest;
            f.Proposal = originalProposal;
            var model = new NativeQuestionViewModel(presented.Question!, f.Questions, f.Authorization,
                new(f.Store, f.Time), () => true, f.Time, NullLogger<NativeQuestionViewModel>.Instance,
                explainClipboardRead: true);
            model.Edit(new(["once"]));
            await model.ReviewAsync();
            model.CompleteReviewPresentation(model.ReviewText);
            model.CanSubmit.Should().BeTrue();
            var reviewed = model.ReviewText;
            await model.ExplainPolicyAsync();
            model.PolicyExplanationText.Should().Contain(grant.Id.Value.ToString("D"))
                .And.Contain(grant.Scope.ToString()).And.Contain("revision 1").And.NotContain("private-body-marker");
            model.CanSubmit.Should().BeFalse();
            await model.ReturnToApprovalAsync();
            model.CanSubmit.Should().BeTrue();
            model.ReviewText.Should().Be(reviewed);
            model.Answer.Choices.Should().Equal("once");
            model.Key.Should().Be(presented.Question!.Key);
            model.Completion.IsCompleted.Should().BeFalse();
            (await f.Store.ReadGrantsAsync(f.Token)).Should().ContainSingle().Which.Should().Be(grant);
            f.Count("session_admissions").Should().Be(0);
            await model.CancelAsync();
            (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Cancelled);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownNativeOwnershipOrPrivateRevisionCannotOpenReadPermission(bool missingPrivate)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        using var native = new NativeQuestionHost(f.Store, f.Time, NullLogger<NativeQuestionViewModel>.Instance);
        native.BindGate(() => true);
        native.BindCallObservation(() => new(1, CallState.Clear, false, CallAwareSettings.Default));
        if (missingPrivate) { native.BindAdmissionRevision(() => 1); }
        else { native.BindPrivateRevision(() => 1); }
        var proposal = new HostOperationProposal(f.Request, new(Guid.NewGuid()), new(1),
            ClipboardPermissionSourceUsePolicy.CreateReadBinding(), HostOperationEffect.BoundedRead, f.Time.Now.AddMinutes(5));
        await f.RunAsync(async () =>
        {
            var present = () => native.RequestClipboardPermissionAsync(proposal, new(1), () => true,
                _ => throw new InvalidOperationException("An unknown source must not open a question."), f.Token);
            await present.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be established*");
        });
        (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Should().BeEmpty();
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
        f.Count("session_admissions").Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("changed-proposal")]
    [InlineData("changed-call")]
    public async Task OriginalPendingExplanationCannotRetargetOrSurviveWithdrawnEvidence(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            f.Proposal = new(f.Request, new(Guid.NewGuid()), new(1),
                ClipboardPermissionSourceUsePolicy.CreateReadBinding(), HostOperationEffect.BoundedRead, f.Time.Now.AddMinutes(5));
            await f.PublishAsync();
            var presented = await f.RunAsync(() => f.Authorization.RequestGrantAsync(f.Request, Spec(), f.Token));
            var current = true;
            var model = new NativeQuestionViewModel(presented.Question!, f.Questions, f.Authorization,
                new(f.Store, f.Time), () => current, f.Time, NullLogger<NativeQuestionViewModel>.Instance,
                explainClipboardRead: true);
            await model.ExplainPolicyAsync();
            model.HasPolicyExplanation.Should().BeTrue();
            model.PolicyExplanationText.Should().NotContain("private-body-marker");
            using (var connection = f.OpenRaw())
            {
                using var read = connection.CreateCommand();
                read.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
                var audit = (string)read.ExecuteScalar()!;
                audit.Should().Contain("approval.explain-pending-clipboard-read").And.NotContain("private-body-marker");
            }
            var key = model.Key;
            switch (change)
            {
                case "cancel":
                    await model.CancelAsync();
                    (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Cancelled);
                    break;
                case "private":
                    current = false;
                    model.RefreshEligibility();
                    break;
                case "changed-proposal":
                    f.Proposal = new(f.Request, new(Guid.NewGuid()), new(2), f.Proposal.Binding,
                        f.Proposal.Effect, f.Proposal.ExpiresAt);
                    await f.PublishAsync();
                    await model.ReturnToApprovalAsync();
                    break;
                case "changed-call":
                    f.Policy = f.Policy with { IsProtectedCall = true };
                    await f.PublishAsync();
                    await model.ReturnToApprovalAsync();
                    model.Status.Should().Be("pending-action-explanation-changed");
                    break;
            }
            model.HasPolicyExplanation.Should().BeFalse();
            model.PolicyExplanationText.Should().BeEmpty();
            model.CanSubmit.Should().BeFalse();
            model.Key.QuestionId.Should().Be(key.QuestionId);
            (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
            f.Count("session_admissions").Should().Be(0);
            var record = (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single();
            record.Status.Should().Be(change is "cancel" ? QuestionStatus.Cancelled : QuestionStatus.Pending);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePolicyReadAfterCloseOrPrivateLossNeverPublishesOrApproves(bool privateLoss)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            f.Proposal = new(f.Request, new(Guid.NewGuid()), new(1),
                ClipboardPermissionSourceUsePolicy.CreateReadBinding(), HostOperationEffect.BoundedRead, f.Time.Now.AddMinutes(5));
            await f.PublishAsync();
            var presented = await f.RunAsync(() => f.Authorization.RequestGrantAsync(f.Request, Spec(), f.Token));
            var delayed = new DelayedStore(f.Store);
            var current = true;
            var model = new NativeQuestionViewModel(presented.Question!, f.Questions, new(delayed, f.Time),
                new(f.Store, f.Time), () => current, f.Time, NullLogger<NativeQuestionViewModel>.Instance,
                explainClipboardRead: true);
            var reading = model.ExplainPolicyAsync();
            await delayed.Read.Task;
            if (privateLoss) { current = false; model.RefreshEligibility(); }
            else { model.Close("Original presentation closed."); }
            delayed.Release.SetResult();
            await reading;
            model.HasPolicyExplanation.Should().BeFalse();
            model.PolicyExplanationText.Should().BeEmpty();
            model.CanSubmit.Should().BeFalse();
            (await model.Completion).Outcome.Should().Be(HostInteractionOutcome.Denied);
            (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
            f.Count("session_admissions").Should().Be(0);
            (await f.Store.ReadQuestionsAsync(f.Request.SessionId, f.Token)).Single().Status.Should().Be(QuestionStatus.Pending);
        });
    }

    private static QuestionSpec Spec() => new("private-body-marker", QuestionKind.SingleChoice,
        [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
        purpose: "authorization", sourceId: "clipboard-read");

    private sealed class FailureStore(IHostInteractionStore store, bool fail) : IHostInteractionStore
    {
        public ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
            Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken) =>
            fail ? throw new IOException("private-body-marker") : store.TransactAsync(request, transition, cancellationToken);
    }

    private sealed class RecordingLogger : ILogger<NativeQuestionViewModel>
    {
        internal List<string> Messages { get; } = [];
        internal List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    private sealed class DelayedStore(IHostInteractionStore store) : IHostInteractionStore
    {
        internal TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
            Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken)
        {
            var result = await store.TransactAsync(request, transition, cancellationToken);
            Read.SetResult();
#pragma warning disable VSTHRD003 // Explicit barrier holds this exact completed read until the test withdraws presentation.
            await Release.Task.WaitAsync(cancellationToken);
#pragma warning restore VSTHRD003
            return result;
        }
    }
}
