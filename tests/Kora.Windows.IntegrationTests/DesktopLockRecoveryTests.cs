using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Controls;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DesktopLockRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PrivateLossRetiresAllComposedSessionSurfacesAndExplicitReopenReadsFreshSources(bool ownerLoss) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true);
            await native.InitializeAsync();
            var target = await native.Sessions.CreateAsync(new("Private result topic"), RequestOrigin.LocalUi, native.Token);
            var request = WindowsSqliteConversationTests.Fresh(target.Authority.SessionId);
            using (var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
            {
                await native.Interactions.RetainConversationMessageAsync(request, target.Authority.Generation, target.Metadata!.Revision.Value,
                    "Exact retained private original", static () => true, native.Token);
                await native.Interactions.CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Unavailable,
                    ModelTurnReason.UnregisteredModel, static () => true, native.Token);
            }
            var admission = new NoAdmission();
            var conversation = new SessionConversationService(native.Interactions, native.Interactions,
                native.Sessions, native.Access, admission, native.Time, NullLogger<SessionConversationService>.Instance);
            using var details = new DetailWindowController(native.Documents, native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance, details);
            using var sessions = new SessionsWindowController(native.Main, native.Sessions, native.Evidence, native.Access,
                NullLogger<SessionsViewModel>.Instance, details: details, conversation: conversation);
            SessionsWindow? workspace = null;
            CompactSessionWindow? compact = null;
            ResponseWindow? interaction = null;
            using var observeWorkspace = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((window, _) => workspace = window);
            using var observeCompact = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((window, _) => compact = window);
            sessions.Bind();
            sessions.Open();
            sessions.OpenCompact();
            sessions.CreateInteraction = (model, owner) =>
                interaction = responses.PrepareSessions(model, owner, sessions.Open, static () => { });
            await sessions.ShowInteractionAsync(true, static () => true);
            var original = (SessionsViewModel)workspace!.DataContext!;
            await original.SelectAsync(original.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
            await original.ReadConversationAsync();
            original.ConversationMessages.Should().Contain(message => message.Body == "Exact retained private original");
            var source = original.ConversationMessages[0];
            await original.OpenConversationDetailAsync(source);
            interaction!.UpdateLayout();
            interaction.SessionView.DetailsVisible.Should().BeTrue(original.ConversationStatus);
            var reader = interaction.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(source.Body);
            original.ComposerBody = "Never auto-submit this input";
            var eligibility = original.CaptureDraftPresentationEligibility();
            var oldInteraction = interaction!;
            var oldWorkspace = workspace;
            var oldCompact = compact!;
            var oldReader = reader;
            var responseLifetime = responses.CaptureLifetime();
            var metadata = await native.Interactions.ReadMetadataAsync(target.Authority.SessionId, native.Token);
            var retention = await native.Interactions.ReadRetentionObservationAsync(target.Authority.SessionId, native.Token);
            var page = await native.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, native.Token);
            var savedDraft = await native.Interactions.ReadConversationDraftAsync(target.Authority.SessionId, native.Token);
            native.Voice.PendingLiteralStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ownerLoss)
            {
                native.Main.BindCallOwnershipGate(static () => false);
                native.Main.CloseForObservedPrivacyEvent("Synthetic desktop owner loss", hidePresentation: true);
            }
            else { native.Access.SetLocked(); }
            Dispatcher.UIThread.RunJobs();
            oldWorkspace.IsVisible.Should().BeFalse();
            oldCompact.IsVisible.Should().BeFalse();
            oldInteraction.IsVisible.Should().BeFalse();
            oldReader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            original.ComposerBody.Should().BeEmpty();
            original.ConversationMessages.Should().BeEmpty();
            original.CanRead.Should().BeFalse();
            eligibility().Should().BeFalse();
            responseLifetime().Should().BeFalse();
            oldInteraction.SessionView.FindControl<TextBox>("SessionComposer")!.Text.Should().BeNullOrEmpty();
            native.Main.ShowSessions();
            interaction.Should().BeSameAs(oldInteraction);
            native.Voice.PendingLiteralStop.SetResult();
            await native.Main.PrivacyClosureTask;
            if (ownerLoss) { native.Main.BindCallOwnershipGate(static () => true); }
            else { native.Access.SetOpen(true); }
            Dispatcher.UIThread.RunJobs();
            native.Main.IsPrivacyPresentationHeld.Should().BeTrue();
            oldInteraction.IsVisible.Should().BeFalse();
            oldReader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            native.Main.ShowSessions();
            await sessions.InteractionOperation;
            interaction.Should().NotBeSameAs(oldInteraction);
            var fresh = (SessionsViewModel)interaction!.SessionView.DataContext!;
            fresh.Should().NotBeSameAs(original);
            fresh.SelectedSessionRecord.Should().BeNull();
            fresh.ConversationMessages.Should().BeEmpty();
            fresh.ComposerBody.Should().BeEmpty();
            oldInteraction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)
            {
                Source = new Button { Name = "OpenConversationDetail", DataContext = source },
            });
            oldReader.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
            await fresh.SelectAsync(fresh.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
            fresh.ConversationMessages.Should().BeEmpty("selection cannot resurrect old private page content");
            await fresh.ReadConversationAsync();
            fresh.ConversationMessages.Should().Equal(page.Messages);
            await fresh.OpenConversationDetailAsync(fresh.ConversationMessages[0]);
            interaction.UpdateLayout();
            var freshReader = interaction.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            freshReader.Should().NotBeSameAs(oldReader);
            freshReader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(source.Body);
            admission.Calls.Should().Be(0);
            native.Voice.StartCalls.Should().Be(0);
            (await native.Interactions.ReadMetadataAsync(target.Authority.SessionId, native.Token)).Should().Be(metadata);
            (await native.Interactions.ReadRetentionObservationAsync(target.Authority.SessionId, native.Token))
                .MatchesRecordedState(retention).Should().BeTrue();
            (await native.Interactions.ReadConversationDraftAsync(target.Authority.SessionId, native.Token)).Should().Be(savedDraft);
            (await native.Interactions.ReadConversationPageAsync(target.Authority.SessionId, null, 25, native.Token))
                .Messages.Should().Equal(page.Messages);
        });

    private sealed class NoAdmission : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Private recovery must not dispatch a provider.");
        }
    }
}
