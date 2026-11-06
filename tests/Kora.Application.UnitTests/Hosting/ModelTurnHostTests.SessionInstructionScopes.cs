using System.Text.Json;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Collection("Host tracing")]
    public sealed class SessionInstructionScopeTests : IDisposable
    {
        private readonly ModelTurnHostTests tracing = new();
        public void Dispose() => tracing.Dispose();

        [Fact]
        public async Task FreshExpiredOrdinaryRefusesApplyingInstructionsAndRetiresBareReviewAcrossScopeChanges()
        {
            using var f = new Fixture { TrackPendingRecords = true };
            var old = SeedExpiredOrdinary(f);
            var (sources, catalogue, _) = InstructionBroker(f, new InstructionSource());
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var host = ConfigureCancellation(f);
            await using var conversation = Conversation(f, workspace, scopes, host);
            var commands = conversation.CreateRequestCommands();
            var bare = scopes.ReviewBareRequest(target);
            bare().Should().BeTrue();
            var review = (await commands.ExecuteAsync(ExpiredReview(old), RequestOrigin.LocalUi, () => true, Token)).RequestReview!;
            await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                () => true, () => true, Token), () => true, Token);
            bare().Should().BeFalse();
            scopes.Invoking(value => value.ReviewBareRequest(target)).Should().Throw<InvalidOperationException>();
            await commands.Invoking(value => value.ExecuteAsync(FailedConfirm(review), RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            await commands.Invoking(value => value.ExecuteAsync(ExpiredReview(old), RequestOrigin.LocalUi, () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            scopes.Stop(target, RequestOrigin.LocalUi, () => true);
            bare().Should().BeFalse();
            var fresh = scopes.ReviewBareRequest(target);
            fresh().Should().BeTrue();
            scopes.RetireReviews();
            fresh().Should().BeFalse();
            var last = scopes.ReviewBareRequest(target);
            scopes.Dispose();
            last().Should().BeFalse();
            scopes.Invoking(value => value.ReviewBareRequest(target)).Should().Throw<ObjectDisposedException>();
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LocalRequestProfilePreservesInstructionSourceReviewAndRejectsChangedSourceBeforeRetention(bool sourceChanged)
        {
            using var f = new Fixture();
            f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
            f.ProfileCatalogue.FieldCatalogue = true;
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                static () => true, static () => true, Token), static () => true, Token);
            var host = f.ConfiguredProfiles();
            var conversation = Conversation(f, workspace, scopes, host);
            var commands = new SessionRequestCommandService(workspace, conversation, f, f.Time);
            var profile = Profile() with { Reasoning = new("exact-effort"), Context = new("exact-window") };
            var reviewed = (await commands.ExecuteAsync(LocalProfileCommand(f, "Original guided request", profile),
                RequestOrigin.LocalUi, static () => true, Token)).RequestReview!;
            if (sourceChanged)
            {
                source.Snapshot = InstructionSnapshot("changed guidance");
                await commands.Invoking(value => value.ExecuteAsync(LocalProfileConfirm(reviewed), RequestOrigin.LocalUi,
                    static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
                f.RetainCalls.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
            }
            else
            {
                var receipt = (await commands.ExecuteAsync(LocalProfileConfirm(reviewed), RequestOrigin.LocalUi,
                    static () => true, Token)).RequestSubmission!;
                receipt.Message.AssistantResponse!.Body.Should().Be("answer");
                f.Adapter.Profile.Should().Be(profile);
                using var wire = JsonDocument.Parse(f.Adapter.Wire!);
                wire.RootElement.GetProperty("Evidence").EnumerateArray().Single().GetProperty("Content")
                    .GetString().Should().Be(source.Snapshot!.Document.Text);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                f.ProfilePreferences.Writes.Should().Be(0);
            }
        }

        [Theory]
        [InlineData(RequestOrigin.LocalUi, true)]
        [InlineData(RequestOrigin.ActivatedVoice, true)]
        [InlineData(RequestOrigin.LocalUi, false)]
        public async Task ApplyTwoRealOrdinaryRequestsStopThenContextFreeRequest(RequestOrigin origin, bool qualified)
        {
            using var f = new Fixture(origin);
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            var audit = new ScopeAudit();
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, audit, f.Time);
            var commands = new SessionInstructionScopeCommandService(scopes);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var reviewText = await commands.ExecuteAsync(SessionCommand.Parse("session instruction apply \"example\"", "Kora")!,
                target, origin, static () => true, static () => true, Token);
            reviewText.Should().Contain(source.Snapshot!.Document.Text).And.Contain("ordinary local");
            f.Adapter.Calls.Should().Be(0);
            f.RetainCalls.Should().Be(0);
            await commands.ExecuteAsync(new(SessionCommandOperation.InstructionScopeConfirm), target, origin,
                static () => true, static () => true, Token);
            f.RetainCalls.Should().Be(0);
            var host = qualified ? f.PolicyHost : f.Production();
            var conversation = Conversation(f, workspace, scopes, host);
            for (var index = 0; index < 2; index++)
            {
                var receipt = await conversation.SubmitAsync(target, "Original request " + index, static () => true, Token, origin: origin);
                receipt.Message.Request.Origin.Should().Be(origin);
                receipt.Message.Outcome.Should().Be(qualified ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
                receipt.Message.AssistantResponse?.Body.Should().Be(qualified ? "answer" : null);
                if (qualified)
                {
                    using var wire = JsonDocument.Parse(f.Adapter.Wire!);
                    wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().Single();
                    evidence.GetProperty("Content").GetString().Should().Be(source.Snapshot.Document.Text);
                    evidence.GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
                }
            }
            await commands.ExecuteAsync(new(SessionCommandOperation.InstructionScopeStop), target, origin,
                static () => true, static () => true, Token);
            (await conversation.SubmitAsync(target, "After stop", static () => true, Token, origin: origin))
                .Message.Body.Should().Be("After stop");
            if (qualified)
            {
                using var wire = JsonDocument.Parse(f.Adapter.Wire!);
                wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
            }
            f.RetainCalls.Should().Be(3);
            audit.Events.Where(item => item.ActionId.StartsWith("session.instruction-scope.", StringComparison.Ordinal))
                .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
                    SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Messages.Should().NotContain(item => item.Contains(InstructionBody, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("disable")]
        [InlineData("hold")]
        [InlineData("source")]
        [InlineData("withdrawn")]
        [InlineData("access")]
        [InlineData("generation")]
        [InlineData("metadata")]
        [InlineData("source-epoch")]
        public async Task BoundRequestsRevalidateExactSourceTargetAndEpochWithoutFallback(string fault)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, revisions) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var review = await scopes.ReviewAsync(target, catalogue.Artifacts.Single(), static () => true, static () => true, Token);
            await scopes.ConfirmAsync(review, static () => true, Token);
            if (fault is "disable") { revisions.DisableExact(source.Snapshot!, catalogue); }
            if (fault is "hold" or "source-epoch")
            {
                revisions.Hold(source.Snapshot!.Name);
                revisions.CompleteActivation(source.Snapshot, revisions.PrepareActivation(source.Snapshot), catalogue);
            }
            if (fault is "source") { source.Snapshot = InstructionSnapshot("replacement"); }
            if (fault is "withdrawn") { source.Snapshot = null; }
            if (fault is "access") { f.ControlRevision++; }
            if (fault is "generation") { f.Session = f.Session with { Generation = new(2) }; }
            if (fault is "metadata")
            { f.Metadata = new(f.Session.SessionId, new(1), new("renamed")); }
            var conversation = Conversation(f, workspace, scopes, f.PolicyHost);
            await conversation.Invoking(value => value.SubmitAsync(target, "Must not fall back", static () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            f.Adapter.Calls.Should().Be(0);
            f.RetainCalls.Should().Be(0);
            scopes.Status(target).Should().StartWith("No session instruction applies.");
        }

        [Theory]
        [InlineData("expired")]
        [InlineData("rollback")]
        [InlineData("retired")]
        [InlineData("review-input")]
        [InlineData("original-input")]
        [InlineData("access")]
        [InlineData("foreign-broker")]
        [InlineData("replay")]
        public async Task SeparateConfirmationNeverRenewsStaleOriginalAuthority(string fault)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var host = true; var input = true;
            var target = new SessionWorkspaceEntry(f.Session, null);
            var review = await scopes.ReviewAsync(target, catalogue.Artifacts.Single(), () => host, () => input, Token,
                RequestOrigin.ActivatedVoice);
            if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(2); }
            if (fault is "rollback") { f.Time.Now = f.Time.Now.AddTicks(-1); }
            if (fault is "retired") { scopes.RetireReviews(); }
            if (fault is "review-input") { input = false; }
            if (fault is "original-input") { host = false; }
            if (fault is "access") { f.ControlRevision++; }
            if (fault is "replay") { await scopes.ConfirmAsync(review, static () => true, Token); }
            using var foreign = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            await (fault is "foreign-broker" ? foreign : scopes).Invoking(value => value.ConfirmAsync(review, static () => true, Token))
                .Should().ThrowAsync<InvalidOperationException>();
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }

        [Theory]
        [InlineData("apply-requested")]
        [InlineData("apply-terminal")]
        [InlineData("stop-requested")]
        [InlineData("stop-terminal")]
        public async Task AuditFailureNeverLeavesSuccessfulApplicabilityOrResurrectsStop(string failure)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            var audit = new ScopeAudit();
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, audit, f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var review = await scopes.ReviewAsync(target, catalogue.Artifacts.Single(), static () => true, static () => true, Token);
            void Fail(SecurityAuditEvent entry)
            {
                if (entry.ActionId.EndsWith(failure.StartsWith("apply", StringComparison.Ordinal) ? ".apply" : ".stop", StringComparison.Ordinal)
                    && entry.Outcome == (failure.EndsWith("requested", StringComparison.Ordinal)
                        ? SecurityAuditOutcome.Requested : SecurityAuditOutcome.Succeeded))
                { throw new IOException("audit unavailable"); }
            }
            if (failure.StartsWith("apply", StringComparison.Ordinal))
            {
                audit.OnWrite = Fail;
                await scopes.Invoking(value => value.ConfirmAsync(review, static () => true, Token)).Should().ThrowAsync<IOException>();
            }
            else
            {
                await scopes.ConfirmAsync(review, static () => true, Token);
                audit.OnWrite = Fail;
                scopes.Invoking(value => value.Stop(target, RequestOrigin.LocalUi, static () => true)).Should().Throw<IOException>();
            }
            scopes.Status(target).Should().StartWith("No session instruction applies.");
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }

        private static SessionWorkspaceService Workspace(Fixture f) =>
            new(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);

        [Theory]
        [InlineData("host")]
        [InlineData("review")]
        [InlineData("done")]
        [InlineData("source")]
        [InlineData("missing-review")]
        [InlineData("wrong-target")]
        [InlineData("operation")]
        [InlineData("cancel")]
        public async Task ScopeCommandFailuresNeverSelectFallbackOrCreateApplicability(string fault)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            using var scopes = new SessionInstructionScopeBroker(sources, Workspace(f), new ScopeAudit(), f.Time);
            var commands = new SessionInstructionScopeCommandService(scopes);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var command = new SessionCommand(SessionCommandOperation.InstructionScopeReview) { ScopeInstructionName = "example" };
            if (fault is "wrong-target")
            {
                await commands.ReviewAsync(target, catalogue.Artifacts.Single(), RequestOrigin.LocalUi,
                    static () => true, static () => true, Token);
                target = target with { Authority = f.Session with { Generation = new(2) } };
            }
            if (fault is "source") { command = command with { ScopeInstructionName = "not available" }; }
            if (fault is "done") { target = target with { Authority = f.Session with { IsActive = false } }; }
            if (fault is "missing-review" or "wrong-target") { command = new(SessionCommandOperation.InstructionScopeConfirm); }
            if (fault is "operation") { command = new(SessionCommandOperation.List); }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            if (fault is "cancel") { cancellation.Cancel(); }
            await commands.Invoking(value => value.ExecuteAsync(command, target, RequestOrigin.LocalUi,
                () => fault is not "host", () => fault is not "review", cancellation.Token)).Should().ThrowAsync<Exception>();
            commands.Pending.Should().BeNull();
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }

        [Theory]
        [InlineData("retire-all")]
        [InlineData("disable")]
        [InlineData("wrong-target")]
        [InlineData("rename")]
        [InlineData("dispose")]
        public async Task StatusAndLifecycleCannotResurrectRetiredScope(string transition)
        {
            using var f = new Fixture();
            f.Metadata = new(f.Session.SessionId, new(1), new("Readable target"));
            var source = new InstructionSource();
            var (sources, catalogue, revisions) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, f.Metadata);
            var review = await scopes.ReviewAsync(target, catalogue.Artifacts.Single(), static () => true, static () => true, Token);
            review.Preview.Should().Contain("Readable target");
            await scopes.ConfirmAsync(review, static () => true, Token);
            scopes.Status(target).Should().Contain("Applying example to Readable target");
            if (transition is "retire-all") { scopes.RetireAll(); }
            if (transition is "disable") { revisions.DisableExact(source.Snapshot!, catalogue); }
            if (transition is "wrong-target") { target = target with { Metadata = f.Metadata with { Revision = new(2) } }; }
            if (transition is "rename")
            {
                await workspace.Invoking(value => value.RenameAsync(target.Authority.SessionId, new(1), 1, new("new"),
                    RequestOrigin.LocalUi, Token)).Should().ThrowAsync<NotSupportedException>();
            }
            if (transition is "dispose") { scopes.Dispose(); scopes.Dispose(); }
            scopes.Status(target).Should().NotStartWith("Applying");
            scopes.Status(target).Should().NotStartWith("Applying");
            f.Adapter.Calls.Should().Be(0);
        }

        [Theory]
        [InlineData("review-retired-during-read")]
        [InlineData("review-input-after-read")]
        [InlineData("confirm-input")]
        [InlineData("audit-revocation")]
        [InlineData("stop-input")]
        [InlineData("source-retired")]
        [InlineData("foreign-source")]
        public async Task AdmissionChangesAcrossReadsAndAuditNeverInstallScope(string fault)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            var audit = new ScopeAudit();
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, audit, f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var eligible = true;
            if (fault is "review-retired-during-read") { f.OnRead = scopes.RetireReviews; }
            if (fault is "review-input-after-read") { f.OnRead = () => eligible = false; }
            if (fault.StartsWith("review", StringComparison.Ordinal))
            {
                await scopes.Invoking(value => value.ReviewAsync(target, catalogue.Artifacts.Single(),
                    static () => true, () => eligible, Token)).Should().ThrowAsync<InvalidOperationException>();
                return;
            }
            if (fault is "source-retired" or "foreign-source")
            {
                var proof = await sources.ReviewSourceAsync(target, catalogue.Artifacts.Single(), static () => true, Token);
                if (fault is "source-retired") { proof.Retire(); }
                var actual = fault is "foreign-source" ? InstructionBroker(f, source).Broker : sources;
                actual.Invoking(value => value.CreateRequestReview(proof, "original", RequestOrigin.LocalUi))
                    .Should().Throw<InvalidOperationException>();
                return;
            }
            if (fault is "stop-input")
            {
                scopes.Invoking(value => value.Stop(target, RequestOrigin.LocalUi, static () => false))
                    .Should().Throw<InvalidOperationException>();
                return;
            }
            var review = await scopes.ReviewAsync(target, catalogue.Artifacts.Single(), static () => true, () => eligible, Token);
            if (fault is "audit-revocation") { audit.OnWrite = _ => eligible = false; }
            await scopes.Invoking(value => value.ConfirmAsync(review, () => fault is not "confirm-input", Token))
                .Should().ThrowAsync<InvalidOperationException>();
            scopes.Status(target).Should().StartWith("No session instruction applies.");
        }

        [Theory]
        [InlineData("stop")]
        [InlineData("disable")]
        public async Task CapturedAnswerMayFinishButStopAndDisableRejectNewCapture(string transition)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, revisions) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                static () => true, static () => true, Token), static () => true, Token);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Pending = release.Task;
            f.Adapter.OnRun = () => entered.SetResult();
            var conversation = Conversation(f, workspace, scopes, f.PolicyHost);
            var beforeCalls = 0;
            var submission = conversation.SubmitAsync(target, "Actual pending answer", static () => true, Token,
                _ => { beforeCalls++; return Task.CompletedTask; });
            await entered.Task;
            if (transition is "stop") { scopes.Stop(target, RequestOrigin.LocalUi, static () => true); }
            else { revisions.DisableExact(source.Snapshot!, catalogue); }
            submission.IsCompleted.Should().BeFalse();
            f.PolicyHost.IsQuiescent.Should().BeFalse();
            release.SetResult(f.Adapter.Reply);
            var receipt = await submission;
            receipt.Message.AssistantResponse!.Body.Should().Be("answer");
            beforeCalls.Should().Be(1);
            f.PolicyHost.IsQuiescent.Should().BeTrue();
            scopes.Status(target).Should().NotStartWith("Applying");
        }

        [Theory]
        [InlineData("stop-read")]
        [InlineData("dispose-read")]
        [InlineData("different-target")]
        [InlineData("retention")]
        public async Task BoundRequestFailureHasNoFallbackOrFabricatedAnswer(string fault)
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                static () => true, static () => true, Token), static () => true, Token);
            if (fault is "stop-read") { f.OnRead = () => scopes.Stop(target, RequestOrigin.LocalUi, static () => true); }
            if (fault is "dispose-read") { f.OnRead = scopes.Dispose; }
            if (fault is "different-target") { target = target with { Authority = f.Session with { Generation = new(2) } }; }
            if (fault is "retention") { f.RetainError = new IOException("store unavailable"); }
            await Conversation(f, workspace, scopes, f.PolicyHost)
                .Invoking(value => value.SubmitAsync(target, "No fabricated answer", static () => true, Token))
                .Should().ThrowAsync<Exception>();
            f.Adapter.Calls.Should().Be(0);
        }

        [Fact]
        public async Task ExplicitQuestionHostedAndOneShotPathsNeverReceiveImplicitSessionInstruction()
        {
            using var f = new Fixture();
            var source = new InstructionSource();
            var (sources, catalogue, _) = InstructionBroker(f, source);
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                static () => true, static () => true, Token), static () => true, Token);
            var oneShot = await sources.ReviewAsync(target, catalogue.Artifacts.Single(), "Separate one shot", static () => true, Token);
            var conversation = new SessionConversationService(f, f, workspace, f, f.PolicyHost, f.Time,
                NullLogger<SessionConversationService>.Instance, execution: f.PolicyHost, interactions: f.Questions,
                sessionInstructionScopes: scopes);
            await conversation.SubmitInstructionAsync(oneShot, static () => true, Token);
            using (var wire = JsonDocument.Parse(f.Adapter.Wire!))
            { wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1); }
            source.Snapshot = null;
            var admission = new TestAdmission(ModelTurnOutcome.Unavailable);
            var excluded = new SessionConversationService(f, f, workspace, f, admission, f.Time,
                NullLogger<SessionConversationService>.Instance, sessionInstructionScopes: scopes);
            await excluded.SubmitAsync(target, "Question-enabled request", static () => true, Token,
                questionPresenter: static (_, _, _, _, _, _) => throw new InvalidOperationException("No question expected."));
            admission.Context!.Evidence.Should().BeEmpty();
            await excluded.SubmitCopilotAsync(target, "Explicit hosted request", static () => true, Token);
            f.RetainCalls.Should().Be(3);
        }

        [Fact]
        public async Task CommandStatusReplacementAndPostReadInputRevocationAreExplicit()
        {
            using var f = new Fixture();
            var (sources, catalogue, _) = InstructionBroker(f, new InstructionSource());
            using var scopes = new SessionInstructionScopeBroker(sources, Workspace(f), new ScopeAudit(), f.Time);
            var commands = new SessionInstructionScopeCommandService(scopes);
            var target = new SessionWorkspaceEntry(f.Session, null);
            (await commands.ExecuteAsync(new(SessionCommandOperation.InstructionScopeStatus), target, RequestOrigin.LocalUi,
                static () => true, static () => true, Token)).Should().StartWith("No session instruction applies.");
            await commands.ReviewAsync(target, catalogue.Artifacts.Single(), RequestOrigin.LocalUi,
                static () => true, static () => true, Token);
            var old = commands.Pending!;
            await commands.ReviewAsync(target, catalogue.Artifacts.Single(), RequestOrigin.LocalUi,
                static () => true, static () => true, Token);
            old.CanConfirm.Should().BeFalse();
            var reads = 0;
            await commands.Invoking(value => value.ReviewAsync(target, catalogue.Artifacts.Single(), RequestOrigin.LocalUi,
                static () => true, () => ++reads == 1, Token)).Should().ThrowAsync<InvalidOperationException>();
            commands.Pending.Should().BeNull();
        }

        [Fact]
        public async Task ReplacementOrStopBetweenPreparationAndCaptureCannotAuthorizeOldRequest()
        {
            using var f = new Fixture();
            var (sources, catalogue, _) = InstructionBroker(f, new InstructionSource());
            using var scopes = new SessionInstructionScopeBroker(sources, Workspace(f), new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            async Task Install() => await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                static () => true, static () => true, Token), static () => true, Token);
            await Install();
            var prepared = (await scopes.PrepareRequestAsync(target, "Real original request", RequestOrigin.LocalUi, Token))!;
            scopes.IsRequestAdmitted(prepared).Should().BeTrue();
            await Install();
            scopes.IsRequestAdmitted(prepared).Should().BeFalse();
            prepared.Consume();
            prepared.Capture(f.Request, f.Time.Now);
            prepared.Invoking(value => value.BeginExecution(new(new(Guid.NewGuid()), f.Request, ModelProviderSelection.OllamaCandidate)))
                .Should().Throw<InvalidOperationException>();
            scopes.Stop(target, RequestOrigin.LocalUi, static () => true);
            scopes.IsRequestAdmitted(prepared).Should().BeFalse();
            scopes.Dispose();
            scopes.IsRequestAdmitted(prepared).Should().BeFalse();
            f.Adapter.Calls.Should().Be(0);
        }

        [Fact]
        public async Task SourceReviewWithoutTracingStillRequiresLiveHostAtConfirmationAndRejectsSystemRelabelling()
        {
            tracing.Dispose();
            using var f = new Fixture();
            var (sources, catalogue, _) = InstructionBroker(f, new InstructionSource());
            using var scopes = new SessionInstructionScopeBroker(sources, Workspace(f), new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            var review = await scopes.ReviewAsync(target, catalogue.Artifacts.Single(), static () => true, static () => true, Token);
            using var restoredTracing = new ModelTurnHostTests();
            using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Policy))
            {
                await scopes.Invoking(value => value.ConfirmAsync(review, static () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            using (f.Root())
            { await scopes.ConfirmAsync(review, static () => true, Token); }
            scopes.Status(target).Should().StartWith("Applying example");
        }

        [Theory]
        [InlineData(ModelTurnOutcome.Cancelled)]
        [InlineData(ModelTurnOutcome.Unknown)]
        public async Task BoundRequestRetainsTruthfulNonSuccessWithOriginalCausalLinks(ModelTurnOutcome outcome)
        {
            using var f = new Fixture();
            var (sources, catalogue, _) = InstructionBroker(f, new InstructionSource());
            var workspace = Workspace(f);
            using var scopes = new SessionInstructionScopeBroker(sources, workspace, new ScopeAudit(), f.Time);
            var target = new SessionWorkspaceEntry(f.Session, null);
            await scopes.ConfirmAsync(await scopes.ReviewAsync(target, catalogue.Artifacts.Single(),
                static () => true, static () => true, Token), static () => true, Token);
            f.Adapter.Reply = new(outcome, ModelProviderSelection.OllamaCandidate, null);
            using var root = f.Root();
            var receipt = await Conversation(f, workspace, scopes, f.PolicyHost).SubmitAsync(target, "Original ordinary input",
                static () => true, Token, links: [new(root.Activity!.Context)]);
            receipt.Message.Outcome.Should().Be(outcome);
            receipt.Message.AssistantResponse.Should().BeNull();
            f.RetainCalls.Should().Be(1);
        }

        [Fact]
        public async Task OneShotTwoMinuteExpiryStillRevalidatesAfterAwaitedSourceAndMetadataRead()
        {
            using var f = new Fixture();
            var (sources, catalogue, _) = InstructionBroker(f, new InstructionSource());
            var review = await sources.ReviewAsync(new(f.Session, null), catalogue.Artifacts.Single(),
                "Original request", static () => true, Token);
            f.BeforeRead = () => { f.Time.Now = f.Time.Now.AddMinutes(2); return Task.CompletedTask; };
            await review.Invoking(value => value.ValidateSourceAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
            f.RetainCalls.Should().Be(0);
            f.Adapter.Calls.Should().Be(0);
        }
        private sealed class ScopeAudit : ISecurityAuditLog
        {
            internal List<SecurityAuditEvent> Events { get; } = [];
            internal Action<SecurityAuditEvent>? OnWrite { get; set; }
            public void Write(SecurityAuditEvent auditEvent)
            {
                var request = HostActivity.RequireCurrent().Request;
                auditEvent.CorrelationId.Should().Be(request.RequestId.Value);
                auditEvent.TargetId.Should().Be("session." + request.SessionId.Value.ToString("N"));
                OnWrite?.Invoke(auditEvent);
                Events.Add(auditEvent);
            }
        }
        private static SessionConversationService Conversation(Fixture f, SessionWorkspaceService workspace,
            SessionInstructionScopeBroker scopes, Kora.Application.Dependencies.ModelTurnHost host) =>
            new(f, f, workspace, f, host, f.Time, NullLogger<SessionConversationService>.Instance,
                execution: host, sessionInstructionScopes: scopes);
    }
}
