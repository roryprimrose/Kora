using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CompactSessionControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControllerComposesClarificationAndAppearanceReviewTogether(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            var host = new NoAdmission();
            var conversation = new SessionConversationService(fixture.Interactions, fixture.Interactions, fixture.Sessions,
                fixture.Access, host, TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var questions = new NativeQuestionHost(fixture.Interactions, TimeProvider.System,
                NullLogger<NativeQuestionViewModel>.Instance);
            using var questionController = new QuestionWindowController(fixture.Main, questions, fixture.VersionQuery,
                host, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance),
                () => fixture.Access.Open, NullLogger<QuestionWindowController>.Instance);
            using var controller = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
                fixture.Access, NullLogger<SessionsViewModel>.Instance, conversation: conversation)
            { ConversationQuestions = questionController };
            SessionsViewModel? model = null;
            using var workspaceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) =>
                model = (SessionsViewModel)view.DataContext!);
            using var compactOpened = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((view, _) =>
                model = (SessionsViewModel)view.DataContext!);
            if (compact) { controller.OpenCompact(); }
            else { controller.Open(); }
            model.Should().NotBeNull();
            var state = model!;
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.Single(entry =>
                entry.Authority.SessionId == fixture.LifecycleTarget!.SessionId));
            await state.ReadConversationAsync();
            state.ComposerBody = "Explicit original request";
            state.QuestionPresenter.Should().NotBeNull();
            state.CanSubmitConversationWithClarification.Should().BeTrue();
            state.CanReviewAppearanceSuggestion.Should().BeTrue();
        });
    }

    [WindowsFact]
    public async Task ExplicitRoutesActivateOneCompactSurfaceShareReopenedWorkspaceAndRevokeBothAtLifecycleAndPrivacy()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            var conversation = new SessionConversationService(fixture.Interactions, fixture.Interactions, fixture.Sessions,
                fixture.Access, new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var controller = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
                fixture.Access, NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            controller.Bind();
            SessionsWindow? workspace = null;
            CompactSessionWindow? compact = null;
            var workspaceOpens = 0;
            var compactOpens = 0;
            using var workspaceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) =>
            { workspace = view; workspaceOpens++; });
            using var compactOpened = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((view, _) =>
            { compact = view; compactOpens++; });
            controller.OpenCompact();
            var model = (SessionsViewModel)compact!.DataContext!;
            model.SelectedSessionRecord.Should().BeNull();
            model.CanCompose.Should().BeFalse();
            controller.OpenCompact();
            compactOpens.Should().Be(1);
            compact.FindControl<Button>("Sessions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            workspaceOpens.Should().Be(1);
            workspace!.DataContext.Should().BeSameAs(model);
            await model.RefreshAsync();
            var original = model.Sessions.Single(entry => entry.Authority.SessionId == fixture.LifecycleTarget!.SessionId);
            await model.SelectAsync(original);
            await model.ReadConversationAsync();
            model.ComposerBody = "same native buffer, no duplicate state";
            workspace.FindControl<Button>("CompactConversation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            compactOpens.Should().Be(1);
            workspace.Close();
            model.ComposerBody.Should().Be("same native buffer, no duplicate state");
            controller.Open();
            workspaceOpens.Should().Be(2);
            workspace!.DataContext.Should().BeSameAs(model);
            model.SelectedSessionRecord!.Authority.SessionId.Should().Be(original.Authority.SessionId);
            workspace.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be("same native buffer, no duplicate state");
            compact.Close();
            model.CanRead.Should().BeTrue();
            model.ComposerBody.Should().Be("same native buffer, no duplicate state");
            controller.OpenCompact();
            compactOpens.Should().Be(2);
            compact!.DataContext.Should().BeSameAs(model);
            var newRequests = 0;
            controller.OpenNewRequest = () => newRequests++;
            compact.FindControl<Button>("NewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            newRequests.Should().Be(1);
            model.ComposerBody.Should().Be("same native buffer, no duplicate state");
            await Task.Run(fixture.AdvanceLifecycleTargetAsync);
            workspace.IsVisible.Should().BeFalse();
            compact.IsVisible.Should().BeFalse();
            model.ComposerBody.Should().BeEmpty();
            model.CanRead.Should().BeFalse();
            controller.OpenCompact();
            compactOpens.Should().Be(3);
            var fresh = (SessionsViewModel)compact!.DataContext!;
            fresh.Should().NotBeSameAs(model);
            fresh.SelectedSessionRecord.Should().BeNull();
            controller.Open();
            var workspaceClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var compactClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            workspace!.Closed += (_, _) => workspaceClosed.TrySetResult();
            compact.Closed += (_, _) => compactClosed.TrySetResult();
            fixture.Access.SetOpen(false);
            fixture.Main.CloseForObservedPrivacyEvent("Synthetic compact privacy closure", hidePresentation: true);
            await Task.WhenAll(workspaceClosed.Task, compactClosed.Task).WaitAsync(fixture.Token);
            await fixture.Main.PrivacyClosureTask;
            workspace!.IsVisible.Should().BeFalse();
            compact.IsVisible.Should().BeFalse();
            controller.OpenCompact();
            compactOpens.Should().Be(3);
            controller.Dispose();
            fixture.Access.SetOpen(true);
            controller.OpenCompact();
            compactOpens.Should().Be(3);
        });
    }

    [Fact]
    public void TrayCompactEntryUsesOnlyExplicitNativeCallbackAndPreservesLegacyShowRoute()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var tray = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Kora", "SystemTrayController.cs"));
        tray.Should().Contain("Compact selected-session conversation").And.Contain("RunAfterNativeMenuCloses(compactConversation)")
            .And.Contain("showItem.Click += (_, _) => ShowWindow()");
        var app = File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", "App.axaml.cs"));
        app.Should().Contain("() => sessionsWindow?.OpenCompact()");
    }

    private sealed class NoAdmission : IModelTurnAdmission, IApplicationInfo
    {
        public string Version => "1.0.0";
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) => throw new InvalidOperationException("Passive compact tests never admit providers.");
    }
}
