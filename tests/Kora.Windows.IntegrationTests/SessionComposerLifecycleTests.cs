using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionComposerLifecycleTests
{
    private const string TargetBody = "  target input e\u0301\r\n  ";
    private const string OtherBody = "  unrelated input \u03a9 e\u0301\r\nexact trailing space  ";

    [Theory]
    [InlineData("native", false)]
    [InlineData("native", true)]
    [InlineData("typed", false)]
    [InlineData("typed", true)]
    [InlineData("voice", false)]
    [InlineData("voice", true)]
    public async Task DoneAndResumeRetireOnlyTargetInputAndRestoreExactUnrelatedBody(string route, bool resume)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize(resume);
            var model = f.Model;
            var otherAuthority = f.Other.Authority;
            var otherRetention = await f.Native.Interactions.ReadRetentionObservationAsync(otherAuthority.SessionId, f.Native.Token);
            var targetDraft = await f.Native.Interactions.ReadConversationDraftAsync(f.Target.Authority.SessionId, f.Native.Token);
            var otherDraft = await f.Native.Interactions.ReadConversationDraftAsync(otherAuthority.SessionId, f.Native.Token);
            var stale = model.CaptureDraftPresentationEligibility();
            var decisions = 0;
            model.DecideComposerDraft = () => { decisions++; return Task.FromResult(ComposerDraftDecision.Cancel); };

            if (route is "native") { await model.ChangeLifecycleAsync(resume); }
            else
            {
                var command = new SessionCommand(resume ? SessionCommandOperation.Resume : SessionCommandOperation.Done,
                    f.Target.Authority.SessionId.Value, f.Target.Authority.Generation.Value);
                if (route is "typed")
                {
                    f.Native.Main.CommandText = "session " + (resume ? "resume " : "done ")
                        + command.SessionId!.Value.ToString("D") + " " + command.Generation;
                    await f.Native.Main.RunTypedCommand.ExecuteAsync();
                }
                else
                {
                    f.Native.Main.BindVoiceOwnershipGate(static () => true);
                    await f.Native.Main.SetVoiceConsentAsync(true);
                    await f.Native.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await f.Native.Main.EndPushToTalkCommand.ExecuteAsync();
                    await f.Native.Main.ExecuteSessionCommandAsync(command, SecurityAuditInitiator.VoiceCommand);
                }
            }
            Dispatcher.UIThread.RunJobs();
            var changed = await f.Native.Interactions.ReadMetadataAsync(f.Target.Authority.SessionId, f.Native.Token);
            changed.Authority.IsActive.Should().Be(resume);
            changed.Authority.Generation.Value.Should().Be(f.Target.Authority.Generation.Value + 1);
            stale().Should().BeFalse();
            decisions.Should().Be(0, "target lifecycle retirement remains intentional, not a new Discard policy");
            model.ComposerBody.Should().BeEmpty();
            var originalModel = model;
            f.Controller.Open();
            f.Controller.OpenCompact();
            model = f.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == otherAuthority.SessionId));
            model.ComposerBody.Should().Be(OtherBody);
            model.Should().BeSameAs(originalModel, "exact-session retirement must not dispose the shared buffer owner");
            f.Workspace.IsVisible.Should().BeTrue();
            f.Compact.IsVisible.Should().BeTrue();
            model.CanRead.Should().BeTrue();
            model.CanSubmitConversation.Should().BeTrue();
            f.Workspace.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(OtherBody);
            f.Compact.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(OtherBody);
            f.Interaction.SessionView.FindControl<TextBox>("SessionComposer")!.Text.Should().Be(OtherBody);
            f.Interaction.SessionView.DataContext.Should().BeSameAs(model);
            model.SelectedSessionRecord!.Authority.Should().Be(otherAuthority);
            (await f.Native.Interactions.ReadRetentionObservationAsync(otherAuthority.SessionId, f.Native.Token))
                .MatchesRecordedState(otherRetention).Should().BeTrue();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == changed.Authority.SessionId));
            model.ComposerBody.Should().BeEmpty("retired target input must not return after Refresh or Resume");
            (await f.Native.Interactions.ReadConversationDraftAsync(changed.Authority.SessionId, f.Native.Token))
                .Should().Be(targetDraft with { Session = changed.Authority });
            (await f.Native.Interactions.ReadConversationDraftAsync(otherAuthority.SessionId, f.Native.Token)).Should().Be(otherDraft);
            (await f.Native.Interactions.ReadConversationPageAsync(changed.Authority.SessionId, null, 25, f.Native.Token))
                .Messages.Should().BeEmpty();
            (await f.Native.Interactions.ReadConversationPageAsync(otherAuthority.SessionId, null, 25, f.Native.Token))
                .Messages.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRetirementPreservesOtherInputWhetherTargetIsSelectedOrBuffered(bool targetSelected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var model = f.Model;
            if (!targetSelected) { await model.SelectAsync(f.Other); }
            await Task.Run(() => f.Controller.RevokeSession(f.Target.Authority.SessionId));
            Dispatcher.UIThread.RunJobs();
            if (targetSelected)
            {
                model.SelectedSessionRecord.Should().BeNull();
                model.CanSubmitConversation.Should().BeFalse();
                model.ConversationMessages.Should().BeEmpty();
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == f.Other.Authority.SessionId));
            }
            model.SelectedSessionRecord!.Authority.Should().Be(f.Other.Authority);
            model.ComposerBody.Should().Be(OtherBody);
            model.CanSubmitConversation.Should().BeTrue();
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == f.Target.Authority.SessionId));
            model.ComposerBody.Should().BeEmpty();
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task LifecycleRetiresPendingAllBufferDiscardWithoutDiscardingOtherInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var decision = new TaskCompletionSource<ComposerDraftDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Model.DecideComposerDraft = () => { entered.TrySetResult(); return decision.Task; };
            var discard = f.Model.ConfirmUnsentDiscardAsync(all: true);
            try
            {
                await entered.Task.WaitAsync(f.Native.Token);
                await f.Native.Sessions.ChangeLifecycleAsync(f.Target.Authority.SessionId,
                    f.Target.Authority.Generation, false, RequestOrigin.LocalUi, f.Native.Token);
                decision.SetResult(ComposerDraftDecision.Discard);
                (await discard).Should().BeFalse();
                await f.Model.RefreshAsync();
                await f.Model.SelectAsync(f.Model.Sessions.Single(row => row.Authority.SessionId == f.Other.Authority.SessionId));
                f.Model.ComposerBody.Should().Be(OtherBody);
                f.Model.CanSubmitConversation.Should().BeTrue();
            }
            finally
            {
                decision.TrySetResult(ComposerDraftDecision.Cancel);
                await discard;
            }
        });
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("host-system")]
    [InlineData("private")]
    public async Task DeniedLifecycleNeverRetiresEitherBufferOrChangesTargetAuthority(string denial)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var current = true;
            var command = new SessionCommand(SessionCommandOperation.Done, f.Target.Authority.SessionId.Value,
                denial is "stale" ? f.Target.Authority.Generation.Value + 1 : f.Target.Authority.Generation.Value);
            if (denial is "private") { current = false; }
            var operation = () => f.Native.Sessions.ExecuteCommandAsync(command,
                denial is "host-system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => current, f.Native.Token);
            await operation.Should().ThrowAsync<InvalidOperationException>();
            (await f.Native.Interactions.ReadMetadataAsync(f.Target.Authority.SessionId, f.Native.Token))
                .Authority.Should().Be(f.Target.Authority);
            f.Model.ComposerBody.Should().Be(TargetBody);
            await f.Model.SelectAsync(f.Other);
            f.Model.ComposerBody.Should().Be(OtherBody);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivacyOrDeviceAuthorityRevisionStillRetiresEveryBuffer(bool privacyClosure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            f.Native.Access.SetOpen(false);
            if (privacyClosure)
            {
                f.Native.Main.CloseForObservedPrivacyEvent("Synthetic composer privacy closure", hidePresentation: true);
                await f.Native.Main.PrivacyClosureTask;
            }
            else { await f.Model.RefreshAsync(); }
            f.Model.ComposerBody.Should().BeEmpty();
            f.Model.HasUnsentMessages.Should().BeFalse();
            f.Model.CanSubmitConversation.Should().BeFalse();
            f.Native.Access.SetOpen(true);
            f.Controller.Open();
            var fresh = (SessionsViewModel)f.Workspace.DataContext!;
            await fresh.RefreshAsync();
            await fresh.SelectAsync(fresh.Sessions.Single(row => row.Authority.SessionId == f.Other.Authority.SessionId));
            fresh.ComposerBody.Should().BeEmpty();
            (await f.Native.Interactions.ReadConversationDraftAsync(f.Other.Authority.SessionId, f.Native.Token))
                .Draft.Revision.Should().Be(0);
        });
    }

    [Fact]
    public async Task RetiredSelectionCannotReattachFromPendingRenameAndOtherInputRemainsExact()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            f.Model.NameDraft = "Renamed exact target";
            f.Checkpoint.BeforeCommitAction = () => f.Controller.RevokeSession(f.Target.Authority.SessionId);
            await f.Model.RenameAsync();
            f.Model.SelectedSessionRecord.Should().BeNull();
            f.Model.ComposerBody.Should().BeEmpty();
            f.Model.CanCompose.Should().BeFalse();
            (await f.Native.Interactions.ReadMetadataAsync(f.Target.Authority.SessionId, f.Native.Token))
                .Metadata!.Name.Value.Should().Be("Renamed exact target", "retiring presentation cannot claim rollback of a committed operation");
            await f.Model.RefreshAsync();
            await f.Model.SelectAsync(f.Model.Sessions.Single(row => row.Authority.SessionId == f.Other.Authority.SessionId));
            f.Model.ComposerBody.Should().Be(OtherBody);
            f.Model.CanSubmitConversation.Should().BeTrue();
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal NativeUxFixtureSession Native { get; }
        internal NoAdmission Admission { get; } = new();
        internal SourceRetirementCheckpoint Checkpoint { get; } = new();
        internal SessionsWindowController Controller { get; private set; } = null!;
        internal SessionsWindow Workspace { get; private set; } = null!;
        internal CompactSessionWindow Compact { get; private set; } = null!;
        internal ResponseWindow Interaction { get; private set; } = null!;
        internal SessionsViewModel Model { get; private set; } = null!;
        internal SessionWorkspaceEntry Target { get; private set; } = null!;
        internal SessionWorkspaceEntry Other { get; private set; } = null!;
        private readonly IDisposable workspaceOpened;
        private readonly IDisposable compactOpened;
        private ResponseWindowController? responses;

        internal Fixture()
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            Native = new(parent, syntheticVoice: true, interactionCheckpoint: Checkpoint);
            workspaceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((window, _) =>
            { Workspace = window; Model = (SessionsViewModel)window.DataContext!; });
            compactOpened = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((window, _) => Compact = window);
        }

        internal async Task Initialize(bool resume = false)
        {
            await Native.InitializeAsync();
            Target = await Native.Sessions.CreateAsync(new("Target conversation"), RequestOrigin.LocalUi, Native.Token);
            Other = await Native.Sessions.CreateAsync(new("Other conversation"), RequestOrigin.LocalUi, Native.Token);
            if (resume)
            {
                var done = await Native.Sessions.ChangeLifecycleAsync(Target.Authority.SessionId,
                    Target.Authority.Generation, false, RequestOrigin.LocalUi, Native.Token);
                Target = new(done, Target.Metadata);
            }
            var conversation = new SessionConversationService(Native.Interactions, Native.Interactions, Native.Sessions,
                Native.Access, Admission, TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            Controller = new(Native.Main, Native.Sessions, Native.Evidence, Native.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            Native.Main.BindSessionCommands(Native.Sessions);
            Controller.Bind();
            Controller.Open();
            Controller.OpenCompact();
            responses = new(Native.Main, NullLogger<ResponseWindowController>.Instance);
            Controller.CreateInteraction = (model, owner) =>
                Interaction = responses.PrepareSessions(model, owner, Controller.Open, static () => { });
            await Controller.ShowInteractionAsync(true, static () => true);
            Model.SessionFilter = SessionListFilter.All;
            await Model.RefreshAsync();
            await Model.SelectAsync(Model.Sessions.Single(row => row.Authority.SessionId == Other.Authority.SessionId));
            Model.ComposerBody = OtherBody;
            await Model.SelectAsync(Model.Sessions.Single(row => row.Authority.SessionId == Target.Authority.SessionId));
            Model.ComposerBody = TargetBody;
        }

        public void Dispose()
        {
            Controller?.Dispose();
            responses?.Dispose();
            workspaceOpened.Dispose();
            compactOpened.Dispose();
            Native.Dispose();
        }
    }

    private sealed class SourceRetirementCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action? BeforeCommitAction { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            var action = BeforeCommitAction;
            BeforeCommitAction = null;
            action?.Invoke();
        }
    }

    private sealed class NoAdmission : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Lifecycle controls must never admit providers.");
        }
    }
}
