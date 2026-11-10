using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AdaptiveInteractionTests
{
    [Fact]
    public async Task SessionManagementUsesTheSameOwnerAndPreservesExactSelectionAndUnsentInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            var window = f.Window!;
            f.Model.ComposerBody = "keep this exact unsent input";
            var extraWindows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((_, _) => extraWindows++);
            window.SessionView.FindControl<Button>("ManageSessions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            window.ActiveActivity.Should().Be(WorkbenchActivity.SessionManagement);
            var view = f.Sessions.ManagementView!;
            view.Owner.Should().BeSameAs(window);
            view.DataContext.Should().BeSameAs(f.Model);
            f.Model.SelectedSessionRecord!.Authority.Should().Be(f.Conversation.Authority);
            view.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be("keep this exact unsent input");
            window.FindControl<Button>("SessionsActivity")!.IsEnabled.Should().BeTrue();
            view.FindControl<Button>("CloseView")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Sessions.InteractionOperation;
            Dispatcher.UIThread.RunJobs();
            window.ActiveActivity.Should().Be(WorkbenchActivity.Sessions);
            window.IsVisible.Should().BeTrue();
            f.Model.ComposerBody.Should().Be("keep this exact unsent input");
            extraWindows.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task HostedSessionManagementCompletesCreateAndRenameWithoutOpeningAnotherWindow()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            f.Sessions.Open();
            f.Model.NameDraft = "New hosted topic";
            await f.Model.CreateAsync();
            var created = f.Model.SelectedSessionRecord!;
            created.Metadata!.Name.Value.Should().Be("New hosted topic");
            f.Model.ComposerBody = "unsent created topic";
            f.Model.NameDraft = "Renamed hosted topic";
            await f.Model.RenameAsync();
            f.Model.SelectedSessionRecord!.Authority.SessionId.Should().Be(created.Authority.SessionId);
            f.Model.SelectedSessionRecord.Metadata!.Name.Value.Should().Be("Renamed hosted topic");
            f.Model.ComposerBody.Should().Be("unsent created topic");
            var saved = await f.Native.Interactions.ReadMetadataAsync(created.Authority.SessionId, f.Native.Token);
            saved.Metadata!.Name.Value.Should().Be("Renamed hosted topic");
            f.Window!.ActiveActivity.Should().Be(WorkbenchActivity.SessionManagement);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("minimize")]
    [InlineData("hide")]
    public async Task LeavingHostedManagementRetiresItsExactReviewWithoutChangingRetainedSession(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Sessions.Open();
            var view = f.Sessions.ManagementView!;
            var admitted = view.CapturePresentation();
            await f.Model.PreviewDispositionAsync();
            f.Model.CanConfirmDisposition.Should().BeTrue(f.Model.Status);
            var session = f.Model.SelectedSessionRecord!;
            var window = f.Window!;
            if (string.Equals(boundary, "chat", StringComparison.Ordinal)) { await f.Run("show sessions"); }
            else if (string.Equals(boundary, "minimize", StringComparison.Ordinal)) { window.WindowState = WindowState.Minimized; }
            else { window.Hide(); }
            admitted().Should().BeFalse();
            f.Model.CanConfirmDisposition.Should().BeFalse();
            var saved = await f.Native.Interactions.ReadMetadataAsync(session.Authority.SessionId, f.Native.Token);
            saved.Authority.Should().Be(session.Authority);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task HostedManagementNarrowLayoutStacksListAndSelectedWork()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            f.Sessions.Open();
            var window = f.Window!;
            window.Width = 640;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var view = f.Sessions.ManagementView!;
            view.FindControl<Grid>("ManagementPanes")!.ColumnDefinitions.Should().HaveCount(1);
            Grid.GetRow(view.FindControl<ScrollViewer>("SelectedSessionWorkPane")!).Should().Be(1);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task CollapseKeepsNamedChatAndExplicitCurrentInteractionDoesNotRetargetItsComposer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            var window = f.Window!;
            f.Model.ComposerBody = "compact named topic input";
            window.FindControl<Button>("CollapseWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            window.IsExpanded.Should().BeFalse();
            window.IsSessionWorkspacePresented.Should().BeTrue();
            window.IsSessionListPresented.Should().BeFalse();
            window.SessionView.FindControl<TextBox>("SessionComposer")!.IsEffectivelyVisible.Should().BeTrue();
            window.SessionView.FindControl<TextBox>("SessionComposer")!.Text.Should().Be("compact named topic input");
            window.SessionView.FindControl<TabItem>("ActivityTab")!.IsVisible.Should().BeFalse();
            window.FindControl<Button>("ShowCurrentInteraction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IsSessionWorkspacePresented.Should().BeFalse();
            f.Model.ComposerBody.Should().Be("compact named topic input");
            await f.Run("show sessions");
            window.IsExpanded.Should().BeTrue();
            window.SessionView.FindControl<TabItem>("ActivityTab")!.IsVisible.Should().BeTrue();
            f.Model.ComposerBody.Should().Be("compact named topic input");
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task CompactNamedComposerCompletesSubmissionToTheSameSelectedSession()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            await f.Run("show sessions");
            await f.SelectConversation();
            f.Sessions.OpenCompact();
            await f.Sessions.InteractionOperation;
            f.Admission.AllowUnavailable = true;
            var composer = f.Window!.SessionView.FindControl<TextBox>("SessionComposer")!;
            composer.Text = "submit from compact named chat";
            f.Window.SessionView.FindControl<Button>("SubmitMessage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.Window.SessionView.Operation;
            f.Model.ConversationMessages.Should().ContainSingle().Which.Request.SessionId.Should().Be(f.Conversation.Authority.SessionId);
            f.Model.ConversationMessages[0].Body.Should().Be("submit from compact named chat");
            f.Model.ComposerBody.Should().BeEmpty();
            f.Admission.Calls.Should().Be(1);
            f.Window.IsExpanded.Should().BeFalse();
        });
    }
}
