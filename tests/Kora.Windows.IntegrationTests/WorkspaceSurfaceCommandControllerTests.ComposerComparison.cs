using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed partial class WorkspaceSurfaceCommandControllerTests
{
    [Theory]
    [InlineData("buffer")]
    [InlineData("input")]
    [InlineData("native-close")]
    public async Task CompareReentrantOpeningCannotPublishOrAcknowledgeExpiredNativeSnapshot(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Native.Access.SetOpen(true);
            f.Bind();
            SessionsWindow? window = null;
            ConversationDraftComparisonWindow? dialog = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            f.Sessions!.Open();
            var model = (SessionsViewModel)window!.DataContext!;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions[0]);
            await model.ReadConversationAsync();
            model.ComposerBody = "original private dirty buffer";
            using var compared = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((view, _) =>
            {
                dialog = view;
                switch (boundary)
                {
                    case "buffer": model.ComposerBody = "new private dirty buffer"; break;
                    case "input": f.Native.Main.CommandText = "new command"; break;
                    case "native-close": Avalonia.Threading.Dispatcher.UIThread.Post(view.Close); break;
                }
            });
            await f.Run("session draft compare " + model.SelectedSessionRecord!.Authority.SessionId.Value.ToString("D"));
            dialog.Should().NotBeNull();
            dialog!.IsVisible.Should().BeFalse();
            dialog.Content.Should().BeNull();
            f.Native.Main.Transcript.Should().Contain("Compare unavailable");
            model.ComposerBody.Should().Be(boundary is "buffer" ? "new private dirty buffer" : "original private dirty buffer");
            model.ComposerDirty.Should().BeTrue();
        });
    }

    [WindowsFact]
    public async Task CompareCommandUsesSameNativePolicyAndExactObservedSourceWithoutDirtyDecisionOrWrites()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Native.Access.SetOpen(true);
            var reader = new RefusingWorkspaceStore(f.Native.Interactions);
            f.Bind(conversationReader: reader);
            SessionsWindow? window = null;
            ConversationDraftComparisonWindow? dialog = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            using var compared = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((view, _) => dialog = view);
            f.Sessions!.Open();
            var model = (SessionsViewModel)window!.DataContext!;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == f.Native.LifecycleTarget!.SessionId));
            var selected = model.SelectedSessionRecord;
            var id = selected!.Authority.SessionId;
            await f.Run("session draft compare " + id.Value.ToString("D"));
            dialog.Should().BeNull("selection alone must not read the saved revision");
            f.Native.Main.Transcript.Should().Contain("explicitly read exact saved revision first");
            await model.ReadConversationAsync();
            reader.ConversationReads.Should().Be(2);
            reader.DenyReads = true;
            const string raw = "  raw e\u0301 \U0001f642\r\nunsaved buffer  ";
            model.ComposerBody = raw;
            model.CanCompareComposer.Should().BeTrue(model.ConversationStatus);
            f.Native.Main.IsProtectedCall.Should().BeFalse();
            f.Native.Main.CanRevealPrivatePresentation.Should().BeTrue();
            f.Sessions.Capture(id, RequestOrigin.LocalUi, () => true).Should().NotBeNull();
            var decisions = 0;
            model.DecideComposerDraft = () => { decisions++; return Task.FromResult(ComposerDraftDecision.Cancel); };
            var target = model.ComposerTarget;
            var before = await f.Native.Interactions.ReadConversationDraftAsync(id, f.Native.Token);
            await f.Run("session draft compare " + Guid.NewGuid().ToString("D"));
            dialog.Should().BeNull();
            await f.Run("session draft compare " + id.Value.ToString("D"));
            dialog.Should().NotBeNull();
            dialog!.IsActive.Should().BeTrue();
            f.Native.Main.Transcript.Should().Contain("native view only").And.NotContain(raw);
            var panes = dialog.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().ToArray();
            panes.Should().HaveCount(2);
            panes[1].GetVisualDescendants().OfType<TextBox>().Should().Contain(box => string.Equals(box.Text, raw, StringComparison.Ordinal));
            model.SelectedSessionRecord.Should().BeSameAs(selected);
            model.ComposerTarget.Should().Be(target);
            model.ComposerBody.Should().Be(raw);
            model.ComposerDirty.Should().BeTrue();
            decisions.Should().Be(0);
            reader.ConversationReads.Should().Be(2);
            reader.Calls.Should().Be(0, "Compare has no source read, reload, save, submission or other storage authority");
            (await f.Native.Interactions.ReadConversationDraftAsync(id, f.Native.Token)).Should().Be(before);
            model.ComposerBody = raw;
            dialog.IsVisible.Should().BeFalse("same-text source edits also retire the exact comparison");
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("edit-back")]
    [InlineData("selection")]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("read")]
    public async Task CompareQueuedNativeInvocationCannotFollowChangedSources(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Native.Access.SetOpen(true);
            f.Bind();
            SessionsWindow? window = null;
            var comparisons = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            using var compared = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((_, _) => comparisons++);
            f.Sessions!.Open();
            var model = (SessionsViewModel)window!.DataContext!;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions[0]);
            await model.ReadConversationAsync();
            var id = model.SelectedSessionRecord!.Authority.SessionId;
            f.Native.Main.CommandText = "session draft compare " + id.Value.ToString("D");
            var operation = f.Native.Main.RunTypedCommand.ExecuteAsync();
            operation.IsCompleted.Should().BeFalse();
            switch (boundary)
            {
                case "edit": model.ComposerBody = "changed"; break;
                case "edit-back": model.ComposerBody = "changed"; model.ComposerBody = string.Empty; break;
                case "selection": await model.SelectAsync(model.Sessions[^1]); break;
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "input": f.Native.Main.CommandText = "changed input"; break;
                case "close": window.ClosePrivate(); break;
                case "dispose": f.Sessions.Dispose(); break;
                case "read": await model.ReadConversationAsync(); break;
            }
            await operation;
            comparisons.Should().Be(0);
            f.Native.Main.Transcript.Should().Contain("Compare unavailable");
        });
    }

    [WindowsFact]
    public async Task CapturedCompareIsSingleUseAndRetiresOnOriginalAdmissionLoss()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Native.Access.SetOpen(true);
            f.Bind();
            SessionsWindow? window = null;
            ConversationDraftComparisonWindow? dialog = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            using var compared = Window.WindowOpenedEvent.AddClassHandler<ConversationDraftComparisonWindow>((view, _) => dialog = view);
            f.Sessions!.Open();
            var model = (SessionsViewModel)window!.DataContext!;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions[0]);
            await model.ReadConversationAsync();
            var id = model.SelectedSessionRecord!.Authority.SessionId;
            var admitted = true;
            f.Sessions.Capture(id, RequestOrigin.HostSystem, () => true).Should().BeNull();
            var launch = f.Sessions.Capture(id, RequestOrigin.ActivatedVoice, () => admitted)!;
            (await launch.Open()).Should().BeTrue();
            (await launch.Open()).Should().BeFalse();
            admitted = false;
            launch.IsCurrent().Should().BeFalse();
            // The existing pane admission erases its full snapshot immediately on access.
            var pane = dialog!.GetVisualDescendants().OfType<SnapshotComparisonFindPane>().First();
            model.CompareComposer(() => false).Should().BeNull();
            dialog!.IsVisible.Should().BeFalse();
            pane.GetVisualDescendants().OfType<TextBox>().Should().OnlyContain(box => string.IsNullOrEmpty(box.Text));
        });
    }
}
