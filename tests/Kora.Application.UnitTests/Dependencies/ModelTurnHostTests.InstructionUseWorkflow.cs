using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    public sealed class InstructionUseHarness : IDisposable
    {
        private readonly ModelTurnHostTests tracing = new();
        private readonly Fixture fixture = new();
        internal InstructionUseWorkflow Workflow { get; }
        internal ConversationMessage? Retained => fixture.Retained;
        internal int ModelCalls => fixture.Adapter.Calls;
        internal int RetainCalls => fixture.RetainCalls;
        internal string SourceText { get; }

        internal InstructionUseHarness()
        {
            fixture.Metadata = new(fixture.Session.SessionId, new(1), new("Work"));
            fixture.MetadataPage = new([new(fixture.Session, fixture.Metadata)], null);
            var source = new InstructionSource();
            var (broker, _, _) = InstructionBroker(fixture, source);
            SourceText = source.Snapshot!.Document.Text;
            Workflow = new(broker, new(fixture, new(fixture), fixture, NullLogger<SessionWorkspaceService>.Instance),
                fixture.ConversationService(fixture.PolicyHost, execution: fixture.PolicyHost), fixture.PolicyHost, fixture.Time);
        }

        public void Dispose()
        {
            fixture.Dispose();
            tracing.Dispose();
        }
    }

    // Reuses the exact retained-conversation/model fixture, rather than a second submission implementation.
    [Collection("Host tracing")]
    public sealed class InstructionUseWorkflowTests : IDisposable
    {
        private readonly ModelTurnHostTests tracing = new();
        public void Dispose() => tracing.Dispose();

        [Theory]
        [InlineData(RequestOrigin.LocalUi, true)]
        [InlineData(RequestOrigin.ActivatedVoice, true)]
        [InlineData(RequestOrigin.ActivatedVoice, false)]
        public async Task ReadableReviewPinsCompleteOriginalInputAndSeparatelyUsesTheExistingRetainedLocalRoute(RequestOrigin origin, bool qualified)
        {
            using var f = new Fixture(origin);
            using var root = f.Root();
            var (workflow, source, _, _) = Create(f, qualified: qualified);
            await using (workflow)
            {
                const string body = "  Original\r\nrequest  ";
                var review = await workflow.ReviewAsync("EXAMPLE", "WORK", body, origin, () => true, Token);
                review.Text.Should().Contain(source.Snapshot!.Document.Text).And.Contain(body).And.Contain("Work")
                    .And.Contain(origin.ToString()).And.Contain("no tools, hosted transfer").And.Contain("COMPLETE");
                review.ExpiresAt.Should().Be(f.Time.Now.AddMinutes(2));
                review.CanConfirm.Should().BeTrue();
                f.RetainCalls.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
                var receipt = await workflow.ConfirmAsync(review.Id, () => true, Token);
                receipt.Message.Body.Should().Be(body);
                receipt.Message.Request.Origin.Should().Be(origin);
                receipt.Message.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
                (receipt.Message.AssistantResponse?.Body).Should().Be(qualified ? "answer" : null);
                f.RetainCalls.Should().Be(1);
                f.Adapter.Calls.Should().Be(qualified ? 1 : 0);
                review.CanConfirm.Should().BeFalse();
                await workflow.Invoking(w => w.ConfirmAsync(review.Id, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                f.RetainCalls.Should().Be(1);
            }
        }

        [Theory]
        [InlineData("missing-source")]
        [InlineData("ambiguous-source")]
        [InlineData("missing-target")]
        [InlineData("ambiguous-target")]
        [InlineData("done")]
        [InlineData("changed-metadata")]
        [InlineData("paging-cycle")]
        [InlineData("disabled")]
        [InlineData("host-origin")]
        [InlineData("ineligible")]
        [InlineData("cancelled")]
        [InlineData("credential")]
        public async Task InvalidSelectionOrAdmissionHasNoModelRetentionEnablementOrSubstitution(string fault)
        {
            using var f = new Fixture();
            using var root = f.Root();
            var (workflow, source, catalogue, revisions) = Create(f);
            await using (workflow)
            {
                var target = new SessionWorkspaceEntry(f.Session, f.Metadata);
                if (fault is "ambiguous-source")
                {
                    var candidate = catalogue.Candidates[0];
                    catalogue.Publish([candidate, new("other", candidate.Kind, candidate.Name, candidate.Description,
                        "other", ["other"], candidate.Source, candidate.Version, candidate.Digest, candidate.Content)
                    { OwnedInstructionName = candidate.OwnedInstructionName, OwnedInstructionRevision = candidate.OwnedInstructionRevision }]);
                }
                if (fault is "missing-target") { f.MetadataPage = new([], null); }
                if (fault is "ambiguous-target") { f.MetadataPage = new([target, target], null); }
                if (fault is "done") { f.MetadataPage = new([target with { Authority = f.Session with { IsActive = false } }], null); }
                if (fault is "changed-metadata") { f.Metadata = f.Metadata! with { Revision = new(2) }; }
                if (fault is "paging-cycle") { f.MetadataPage = new([], Guid.NewGuid()); }
                if (fault is "disabled") { revisions.DisableExact(source.Snapshot!, catalogue); }
                using var cancellation = new CancellationTokenSource();
                if (fault is "cancelled") { cancellation.Cancel(); }
                await workflow.Invoking(w => w.ReviewAsync(fault is "missing-source" ? "other" : "example",
                    "Work", fault is "credential" ? "api_key=synthetic-only-value" : "Original",
                    fault is "host-origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
                    () => fault is not "ineligible", cancellation.Token)).Should().ThrowAsync<Exception>();
                f.RetainCalls.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
                await workflow.Invoking(w => w.ConfirmAsync(Guid.NewGuid(), () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
            }
        }

        [Theory]
        [InlineData("wrong-id")]
        [InlineData("expired")]
        [InlineData("source")]
        [InlineData("disable")]
        [InlineData("hold")]
        [InlineData("access")]
        [InlineData("generation")]
        [InlineData("metadata")]
        [InlineData("origin-revoked")]
        [InlineData("confirm-revoked")]
        [InlineData("stop")]
        public async Task ConfirmationCannotRenewOrReplaceTheOriginalReview(string fault)
        {
            using var f = new Fixture(RequestOrigin.ActivatedVoice);
            using var root = f.Root();
            var (workflow, source, catalogue, revisions) = Create(f);
            await using (workflow)
            {
                var original = true;
                var review = await workflow.ReviewAsync("example", "Work", "Original", RequestOrigin.ActivatedVoice, () => original, Token);
                if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(2); }
                if (fault is "source") { source.Snapshot = null; }
                if (fault is "disable") { revisions.DisableExact(source.Snapshot!, catalogue); }
                if (fault is "hold") { revisions.Hold("example"); }
                if (fault is "access") { f.ControlRevision++; }
                if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
                if (fault is "metadata") { f.Metadata = f.Metadata! with { Revision = new(2) }; }
                if (fault is "origin-revoked") { original = false; }
                if (fault is "stop") { await workflow.StopAsync(); }
                await workflow.Invoking(w => w.ConfirmAsync(fault is "wrong-id" ? Guid.NewGuid() : review.Id,
                    () => fault is not "confirm-revoked", Token)).Should().ThrowAsync<Exception>();
                f.RetainCalls.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
            }
        }

        [Fact]
        public async Task PendingReviewCannotBeReplacedAndStopAllowsOnlyAFreshReviewIdentity()
        {
            using var f = new Fixture();
            using var root = f.Root();
            var (workflow, _, _, _) = Create(f);
            await using (workflow)
            {
                var first = await workflow.ReviewAsync("example", "Work", "First", RequestOrigin.LocalUi, () => true, Token);
                await workflow.Invoking(w => w.ReviewAsync("example", "Work", "Replacement", RequestOrigin.LocalUi, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
                await workflow.StopAsync();
                var second = await workflow.ReviewAsync("example", "Work", "Second", RequestOrigin.LocalUi, () => true, Token);
                second.Id.Should().NotBe(first.Id);
                await workflow.Invoking(w => w.ConfirmAsync(first.Id, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                var receipt = await workflow.ConfirmAsync(second.Id, () => true, Token);
                receipt.Message.Body.Should().Be("Second");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ModelOrInvocationScopeCannotConfirmAnOriginalUserReview(bool invocation)
        {
            using var f = new Fixture();
            using var root = f.Root();
            var (workflow, _, _, _) = Create(f);
            await using (workflow)
            {
                var review = await workflow.ReviewAsync("example", "Work", "Original", RequestOrigin.LocalUi, () => true, Token);
                var request = HostRequest.Create(invocation ? RequestOrigin.LocalUi : RequestOrigin.HostSystem);
                if (invocation) { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
                using (Kora.Core.Diagnostics.HostActivity.BeginRoot(request, Kora.Core.Diagnostics.HostActivityLayer.Application,
                    Kora.Core.Diagnostics.HostOperation.Request))
                {
                    await workflow.Invoking(w => w.ConfirmAsync(review.Id, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                }
                f.Adapter.Calls.Should().Be(0);
                f.RetainCalls.Should().Be(0);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StopAndDisposalJoinCancellationIgnoringSourceAndRejectLateReviewPublication(bool dispose)
        {
            using var f = new Fixture();
            using var root = f.Root();
            f.Metadata = new(f.Session.SessionId, new(1), new("Work"));
            f.MetadataPage = new([new(f.Session, f.Metadata)], null);
            var source = new BlockingInstructionSource();
            var revisions = new InstructionRevisionCatalogue(new InstructionPreferences());
            var snapshot = source.Snapshot;
            var catalogue = new ArtifactCatalogue(revisions.Reconcile([snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            await using var workflow = new InstructionUseWorkflow(new(catalogue, revisions, source, f, f, f.Time),
                new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance),
                f.ConversationService(f.PolicyHost, execution: f.PolicyHost), f.PolicyHost, f.Time);
            var reviewing = workflow.ReviewAsync("example", "Work", "Original", RequestOrigin.LocalUi, () => true, Token);
            await Task.WhenAny(source.Entered.Task, reviewing);
            if (reviewing.IsCompleted) { await reviewing; }
            await workflow.Invoking(w => w.ReviewAsync("example", "Work", "Concurrent", RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            var stopping = dispose ? workflow.DisposeAsync().AsTask() : workflow.StopAsync();
            stopping.IsCompleted.Should().BeFalse();
            source.Release.SetResult();
            await reviewing.Invoking(async task => await task).Should().ThrowAsync<OperationCanceledException>();
            await stopping;
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CapturedAnswerMayFinishAfterUseDisableButStopJoinsIgnoringModelAndDoesNotPublishLateSuccess(bool stop)
        {
            using var f = new Fixture(RequestOrigin.ActivatedVoice);
            using var root = f.Root();
            var (workflow, source, catalogue, revisions) = Create(f);
            await using (workflow)
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                f.Adapter.Pending = release.Task;
                f.Adapter.OnRun = () => entered.SetResult();
                var review = await workflow.ReviewAsync("example", "Work", "Original", RequestOrigin.ActivatedVoice, () => true, Token);
                var submitting = workflow.ConfirmAsync(review.Id, () => true, Token);
                await Task.WhenAny(entered.Task, submitting);
                if (submitting.IsCompleted) { await submitting; }
                revisions.DisableExact(source.Snapshot!, catalogue);
                var stopping = stop ? workflow.StopAsync() : Task.CompletedTask;
                if (stop) { stopping.IsCompleted.Should().BeFalse(); }
                release.SetResult(f.Adapter.Reply);
                if (stop)
                {
                    await submitting.Invoking(async task => await task).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
                    f.Retained!.AssistantResponse.Should().BeNull();
                }
                else { (await submitting).Message.AssistantResponse!.Body.Should().Be("answer"); }
                await stopping;
                f.Adapter.Calls.Should().Be(1);
            }
        }

        private static (InstructionUseWorkflow Workflow, InstructionSource Source, ArtifactCatalogue Catalogue, InstructionRevisionCatalogue Revisions)
            Create(Fixture f, bool qualified = true)
        {
            f.Metadata = new(f.Session.SessionId, new(1), new("Work"));
            f.MetadataPage = new([new(f.Session, f.Metadata)], null);
            var source = new InstructionSource();
            var (broker, catalogue, revisions) = InstructionBroker(f, source);
            var host = qualified ? f.PolicyHost : f.Production();
            return (new(broker, new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance),
                f.ConversationService(host, execution: host), host, f.Time), source, catalogue, revisions);
        }

        private sealed class BlockingInstructionSource : IOwnedInstructionStore
        {
            internal OwnedInstructionSnapshot Snapshot { get; } = InstructionSnapshot();
            internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token)
            {
                Entered.TrySetResult();
                await Release.Task;
                return Snapshot;
            }
            public Task<IReadOnlyList<string>> List(CancellationToken token) => throw new NotSupportedException();
            public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? replacement,
                Func<bool> eligible, Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) =>
                throw new NotSupportedException();
        }
    }
}
