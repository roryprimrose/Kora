using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftWrappingIsViewLocalAndPreservesExactSharedBufferBaselineReviewAndAuthority(bool compactFirst)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var saved = await WindowsSqliteConversationTests.Save(f,
                await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "saved exact baseline");
            var reader = new SearchReader(f.Store.ReadConversationPageAsync, f.Store.ReadConversationDraftAsync, f.Store.ReadMetadataAsync);
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                await model.RecoverLegacyUnsentAsync();
                model.ComposerDirty.Should().BeTrue();
                var target = model.ComposerTarget;
                var before = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var reads = (reader.Reads, reader.DraftReads);
                reader.DenyReads = true;
                Window first = compactFirst ? compact : workspace;
                Window other = compactFirst ? workspace : compact;
                var editor = first.FindControl<TextBox>("ConversationComposer")!;
                var choice = first.FindControl<CheckBox>("WrapDraftText")!;
                var otherChoice = other.FindControl<CheckBox>("WrapDraftText")!;
                var otherEditor = other.FindControl<TextBox>("ConversationComposer")!;
                var original = model.ComposerBody;
                choice.IsChecked.Should().BeTrue();
                editor.TextWrapping.Should().Be(TextWrapping.Wrap);
                AssertDraftLayout(first, editor, choice);
                model.ComposerBody.Should().BeSameAs(original);
                model.ComposerDirty.Should().BeTrue();

                var raw = "  \U0001f642e\u0301\t" + new string('x', 1800) + "\r\n exact end  ";
                editor.Text = raw;
                var exact = model.ComposerBody;
                otherEditor.Text.Should().BeSameAs(exact);
                choice.IsChecked = false;
                first.UpdateLayout();
                editor.TextWrapping.Should().Be(TextWrapping.NoWrap);
                otherChoice.IsChecked.Should().BeTrue();
                otherEditor.TextWrapping.Should().Be(TextWrapping.Wrap);
                AssertDraftLayout(first, editor, choice);
                model.ComposerBody.Should().BeSameAs(exact).And.Be(raw);
                model.ComposerDirty.Should().BeTrue();
                model.ComposerTarget.Should().Be(target);
                (reader.Reads, reader.DraftReads).Should().Be(reads);
                composed.Audit.Events.Should().BeEmpty();
                WindowsSqliteConversationTests.AuditText(f).Should().Be(before);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Should().Be(saved);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("host_questions").Should().Be(0);

                AssertDraftEditing(first, editor, choice);
                otherEditor.Text.Should().Be(model.ComposerBody, "normal edits still use the authoritative shared buffer");
                editor.Text = new string('z', 4097);
                choice.IsChecked = false;
                choice.IsChecked = true;
                editor.Text.Length.Should().Be(4097);
                editor.IsEnabled.Should().BeTrue();
                editor.MaxLength.Should().Be(0);
                editor.Text = "corrected ordinary draft";
                model.ComposerBody.Should().Be("corrected ordinary draft");

                choice.IsChecked = false;
                first.Close();
                choice.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                otherEditor.TextWrapping.Should().Be(TextWrapping.Wrap);
                model.CanRead.Should().BeTrue();
                Window reopened = compactFirst ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
                reopened.Show();
                reopened.FindControl<CheckBox>("WrapDraftText")!.IsChecked.Should().BeTrue();
                reopened.Close();
            }
            finally { owner.Revoke(); }
        });
    }

    [Fact]
    public async Task NewRequestDraftWrappingPreservesFrozenSaveAndSubmitReviewsAndNeverChangesReadonlyBodies()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var model = new NewRequestViewModel(composer);
            var window = new NewRequestWindow(model, () => throw new InvalidOperationException("No navigation."),
                _ => throw new InvalidOperationException("No native operation should fail."));
            window.Show();
            try
            {
                var editor = window.FindControl<TextBox>("NewRequestBody")!;
                var choice = window.FindControl<CheckBox>("WrapDraftText")!;
                choice.IsChecked.Should().BeTrue();
                editor.TextWrapping.Should().Be(TextWrapping.Wrap);
                await composer.Create();
                editor.Text = "  \U0001f642e\u0301\t" + new string('x', 1800) + "\r\n exact end  ";
                AssertDraftLayout(window, editor, choice);
                composer.Review();
                var original = composer.Body;
                var reviewed = composer.ReviewedBody;
                var target = composer.Target;
                var creation = composer.Creation;
                var status = composer.Status;
                var readonlyBodies = window.GetVisualDescendants().OfType<TextBlock>()
                    .Select(block => (Block: block, block.Text, block.TextWrapping)).ToArray();
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var saved = await f.Store.ReadConversationDraftAsync(target!.Authority.SessionId, f.Token);
                var retention = await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token);
                AssertDraftLayout(window, editor, choice);
                composer.Body.Should().BeSameAs(original);
                composer.ReviewedBody.Should().BeSameAs(reviewed);
                composer.Target.Should().BeSameAs(target);
                composer.Creation.Should().BeSameAs(creation);
                composer.Submission.Should().BeNull();
                composer.CanSubmit.Should().BeTrue();
                composer.Dirty.Should().BeTrue();
                composer.Status.Should().Be(status);
                foreach (var snapshot in readonlyBodies)
                {
                    snapshot.Block.Text.Should().Be(snapshot.Text);
                    snapshot.Block.TextWrapping.Should().Be(snapshot.TextWrapping);
                }
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadConversationDraftAsync(target.Authority.SessionId, f.Token)).Should().Be(saved);
                (await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token)).Should().Be(retention);
                composed.Audit.Events.Should().BeEmpty();
                f.Count("conversation_messages").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                AssertDraftEditing(window, editor, choice);
                composer.CanSubmit.Should().BeFalse("only a real body edit retires review");
                editor.Text = new string('x', 4097);
                choice.IsChecked = false;
                choice.IsChecked = true;
                composer.Body.Length.Should().Be(4097);
                editor.IsEnabled.Should().BeTrue();
                editor.MaxLength.Should().Be(0);
                editor.Text = "corrected native draft";
                composer.Body.Should().Be("corrected native draft");
                window.ClosePrivate();
                var wrapping = editor.TextWrapping;
                choice.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                editor.TextWrapping.Should().Be(wrapping);
                composer.Body.Should().BeEmpty();
            }
            finally { window.ClosePrivate(); }
        });
    }

    private static void AssertDraftLayout(Window window, TextBox editor, CheckBox choice)
    {
        ControlAutomationPeer.CreatePeerForElement(choice)!.GetName().Should().Be("Wrap draft text");
        choice.Focusable.Should().BeTrue();
        var exact = editor.Text;
        editor.CaretIndex = 3;
        editor.SelectionStart = 8;
        editor.SelectionEnd = 2;
        var caret = editor.CaretIndex;
        var start = editor.SelectionStart;
        var end = editor.SelectionEnd;
        var undo = editor.CanUndo;
        var redo = editor.CanRedo;
        var scale = NativeReaderTypography.Capture(editor);
        foreach (var wrap in new[] { false, true, false, true })
        {
            choice.IsChecked = wrap;
            foreach (var width in new[] { 720d, 980d, 760d })
            {
                window.Width = width;
                foreach (var percent in new[] { 75, 200, 100 })
                {
                    scale(percent);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    editor.Text.Should().BeSameAs(exact);
                    editor.CaretIndex.Should().Be(caret);
                    editor.SelectionStart.Should().Be(start);
                    editor.SelectionEnd.Should().Be(end);
                    editor.CanUndo.Should().Be(undo);
                    editor.CanRedo.Should().Be(redo);
                    editor.TextWrapping.Should().Be(wrap ? TextWrapping.Wrap : TextWrapping.NoWrap);
                    var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();
                    if (!wrap && exact!.Length > 1000)
                    {
                        scroll.Extent.Width.Should().BeGreaterThan(scroll.Viewport.Width);
                        scroll.Offset = new(80, scroll.Offset.Y);
                        scroll.Offset.X.Should().BeGreaterThan(0, "long lines must actually scroll");
                    }
                    if (wrap) { scroll.Extent.Width.Should().BeLessThanOrEqualTo(scroll.Viewport.Width); }
                }
            }
        }
        choice.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        choice.IsChecked.Should().BeFalse("native keyboard activation must change only presentation");
        editor.TextWrapping.Should().Be(TextWrapping.NoWrap);
        editor.Text.Should().BeSameAs(exact);
        editor.CaretIndex.Should().Be(caret);
        editor.SelectionStart.Should().Be(start);
        editor.SelectionEnd.Should().Be(end);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("metadata")]
    [InlineData("lifecycle")]
    [InlineData("privacy")]
    [InlineData("context")]
    [InlineData("detach")]
    [InlineData("close")]
    [InlineData("reentrant")]
    public async Task DraftWrappingRetiresStaleNativeControlsWithoutLateLayoutOrBufferWrites(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            var model = composed.Model;
            using var fresh = composed.NewComposer();
            await fresh.Create();
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == f.Request.SessionId));
                await model.ReadConversationAsync();
                var editor = window.FindControl<TextBox>("ConversationComposer")!;
                var choice = window.FindControl<CheckBox>("WrapDraftText")!;
                var raw = "  private \U0001f642e\u0301\r\n exact body ";
                editor.Text = raw;
                choice.IsChecked = false;
                editor.TextWrapping.Should().Be(TextWrapping.NoWrap);
                model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
                switch (retirement)
                {
                    case "source":
                        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == fresh.Target!.Authority.SessionId));
                        choice.IsChecked.Should().BeTrue();
                        model.ComposerBody.Should().BeEmpty();
                        editor.Text = "new source draft";
                        break;
                    case "control":
                        composed.Access.ControlRevision++;
                        choice.IsChecked = true;
                        choice.IsChecked = false;
                        editor.TextWrapping.Should().Be(TextWrapping.Wrap);
                        break;
                    case "metadata":
                        await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 0,
                            new("renamed source"), Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                        await model.RefreshWorkAsync();
                        choice.IsChecked.Should().BeTrue();
                        break;
                    case "lifecycle":
                        await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false,
                            Kora.Core.Hosting.RequestOrigin.LocalUi, f.Token);
                        await model.RefreshWorkAsync();
                        choice.IsChecked.Should().BeTrue();
                        break;
                    case "privacy":
                        composed.Access.CanInspect = false;
                        owner.Revoke();
                        model.ComposerBody.Should().BeEmpty();
                        break;
                    case "context":
                        editor.DataContext = null;
                        break;
                    case "detach":
                        ((Panel)editor.Parent!).Children.Remove(editor);
                        break;
                    case "close":
                        owner.Revoke();
                        model.ComposerBody.Should().BeEmpty();
                        break;
                    case "reentrant":
                        editor.PropertyChanged += (_, args) =>
                        {
                            if (args.Property == TextBox.TextWrappingProperty && editor.TextWrapping == TextWrapping.Wrap)
                            { owner.Revoke(); }
                        };
                        choice.IsChecked = true;
                        window.IsVisible.Should().BeFalse();
                        model.ComposerBody.Should().BeEmpty();
                        break;
                }
                var exact = model.ComposerBody;
                var wrapping = editor.TextWrapping;
                if (string.Equals(retirement, "source", StringComparison.Ordinal))
                {
                    // A fresh gesture affects only this new source; no old layout work is queued.
                    choice.IsChecked = false;
                    wrapping = TextWrapping.NoWrap;
                }
                else if (retirement is not ("control" or "metadata" or "lifecycle"))
                {
                    choice.IsChecked = false;
                    choice.IsChecked = true;
                    choice.IsChecked = false;
                }
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                editor.TextWrapping.Should().Be(wrapping);
                model.ComposerBody.Should().BeSameAs(exact);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                composed.Audit.Events.Should().BeEmpty();
            }
            finally { owner.Revoke(); }
        });
    }

    private static void AssertDraftEditing(Window window, TextBox editor, CheckBox choice)
    {
        var before = editor.Text!;
        editor.Focus();
        editor.CaretIndex = before.Length;
        editor.SelectionStart = before.Length;
        editor.SelectionEnd = before.Length;
        window.KeyTextInput("Q");
        editor.Text.Should().Be(before + "Q");
        editor.CanUndo.Should().BeTrue();
        choice.IsChecked = true;
        choice.IsChecked = false;
        editor.Undo();
        editor.Text.Should().Be(before);
        editor.CanRedo.Should().BeTrue();
        choice.IsChecked = true;
        window.Width += 50;
        window.UpdateLayout();
        editor.CanRedo.Should().BeTrue();
        editor.Redo();
        editor.Text.Should().Be(before + "Q");
        var copying = new Avalonia.Interactivity.RoutedEventArgs(TextBox.CopyingToClipboardEvent);
        editor.RaiseEvent(copying);
        copying.Handled.Should().BeFalse("draft clipboard authority is unchanged; no copy is performed by this test");
    }
}
