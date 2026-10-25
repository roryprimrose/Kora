using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed partial class NewRequestControllerTests
{
    [WindowsFact]
    public async Task TrayAndWorkspaceShareOneIsolatedWindowAndExactNavigationProtectsDirtyNamedBufferBeforePrivacyAndLifecycleRevocation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            var conversation = new SessionConversationService(fixture.Interactions, fixture.Interactions, fixture.Sessions,
                fixture.Access, new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            using var sessions = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
                fixture.Access, NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            using var controller = new NewRequestWindowController(fixture.Main, fixture.Sessions, fixture.Access,
                () => new(fixture.Sessions, conversation, fixture.Access, NullLogger<NewRequestComposer>.Instance), sessions,
                new AvaloniaUiDispatcher());
            controller.Bind();
            sessions.Bind();
            sessions.OpenNewRequest = controller.Open;
            NewRequestWindow? window = null;
            SessionsWindow? sessionsWindow = null;
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<NewRequestWindow>((view, _) => { window = view; opens++; });
            using var sessionsOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => sessionsWindow = view);
            sessions.Open();
            var named = (SessionsViewModel)sessionsWindow!.DataContext!;
            await named.RefreshAsync();
            var original = named.Sessions.First(entry => entry.Authority.IsActive);
            await named.SelectAsync(original);
            named.ComposerBody = "dirty named-session memory";
            sessionsWindow.FindControl<Button>("NewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            opens.Should().Be(1);
            var state = ((NewRequestViewModel)window!.DataContext!).Composer;
            state.Body = "isolated native memory";
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name is "DraftFindQuery");
            query.Text = "native";
            window.GetVisualDescendants().OfType<Button>().Single(control => control.Name is "DraftFindFirst")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<TextBox>("NewRequestBody")!.SelectedText.Should().Be("native");
            query.Text.Should().Be("native");
            controller.Open();
            opens.Should().Be(1);
            ((NewRequestViewModel)window.DataContext!).Composer.Should().BeSameAs(state);
            state.Body.Should().Be("isolated native memory");
            window.FindControl<Button>("CreateTarget")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Operation;
            query.Text.Should().BeEmpty("target creation retires current draft Find");
            var exact = state.Target!.Authority.SessionId;
            named.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Cancel);
            window.FindControl<Button>("OpenNewRequestSession")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Operation;
            named.SelectedSessionRecord!.Authority.SessionId.Should().Be(exact);
            named.ComposerBody.Should().BeEmpty();
            await sessions.NavigateAsync(original.Authority.SessionId, () => true, fixture.Token);
            named.ComposerBody.Should().Be("dirty named-session memory", "navigation preserves the original topic buffer without asking to discard");
            named.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
            window.FindControl<Button>("OpenNewRequestSession")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Operation;
            named.SelectedSessionRecord!.Authority.SessionId.Should().Be(exact);
            named.ComposerBody.Should().BeEmpty();
            state.Body.Should().Be("isolated native memory", "explicit navigation never transfers or saves this body");
            (await fixture.Interactions.ReadConversationDraftAsync(exact, fixture.Token)).Draft.Body.Should().BeNull();
            var before = await fixture.Interactions.ReadRetentionAsync(exact, fixture.Token);
            await state.Inspect();
            (await fixture.Interactions.ReadRetentionAsync(exact, fixture.Token)).Should().Be(before);
            await fixture.Sessions.ChangeLifecycleAsync(exact, new(1), false, RequestOrigin.LocalUi, fixture.Token);
            window.IsVisible.Should().BeFalse();
            state.Body.Should().BeEmpty();
            state.CanSubmit.Should().BeFalse();
            controller.Open();
            opens.Should().Be(2);
            state = ((NewRequestViewModel)window!.DataContext!).Composer;
            state.Body = "private closing memory";
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            fixture.Access.SetOpen(false);
            fixture.Main.CloseForObservedPrivacyEvent("Synthetic New request privacy closure", hidePresentation: true);
            await closed.Task.WaitAsync(fixture.Token);
            await fixture.Main.PrivacyClosureTask;
            window.IsVisible.Should().BeFalse();
            state.Body.Should().BeEmpty();
            window.FindControl<TextBox>("NewRequestBody")!.Text.Should().BeNullOrEmpty();
            controller.Open();
            opens.Should().Be(2);
            controller.Dispose();
            fixture.Access.SetOpen(true);
            controller.Open();
            opens.Should().Be(2);
        });
    }

    [Fact]
    public void TrayEntryAndWorkspaceKeepTheSameNativeNavigationCallback()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var tray = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Kora", "SystemTrayController.cs"));
        tray.Should().Contain("New request").And.Contain("RunAfterNativeMenuCloses(newRequest)");
        var app = File.ReadAllText(Path.Combine(directory.FullName, "src", "Kora", "App.axaml.cs"));
        app.Should().Contain("() => newRequestWindow?.Open()").And.Contain("sessionsWindow.OpenNewRequest = newRequestWindow.Open")
            .And.Contain("newRequestWindow?.Dispose()");
    }

    private sealed class NoAdmission : IModelTurnAdmission
    {
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token) => throw new InvalidOperationException("Navigation tests never submit or admit a provider.");
    }
}
