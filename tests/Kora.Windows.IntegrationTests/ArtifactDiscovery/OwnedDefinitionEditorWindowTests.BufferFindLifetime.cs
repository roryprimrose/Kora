using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Fact]
    public async Task ReentrantNativeFocusSelectionIsNotAcquiredOrCollapsedByBufferFindFailure()
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "PRIVATE buffer";
            editor.BufferFindQuery.Text = "PRIVATE";
            editor.BufferFindQuery.Focus().Should().BeTrue();
            editor.Source.GotFocus += (_, _) =>
            {
                editor.Source.SelectionStart = 1;
                editor.Source.SelectionEnd = 3;
            };
            ClickBuffer(editor.BufferFindFirst);
            editor.Source.SelectedText.Should().Be("RI");
            editor.BufferFindOutcome.Text.Should().Contain("selection unavailable").And.Contain("no match or count claimed");
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public async Task BufferFindQueryAndEscapeFocusFailuresExposeNativeRecoveryWithoutClaimingFocus()
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "PRIVATE buffer";
            editor.BufferFindQuery.Focusable = false;
            editor.Source.RaiseEvent(FindKey(Key.F, KeyModifiers.Control));
            editor.BufferFindOutcome.Text.Should().Contain("query focus unavailable").And.Contain("Tab");
            editor.BufferFindQuery.Focusable = true;
            editor.Source.Focusable = false;
            editor.BufferFindQuery.RaiseEvent(FindKey(Key.Escape));
            editor.BufferFindOutcome.Text.Should().Contain("Source focus unavailable").And.Contain("Tab");
            editor.Source.Text.Should().Be("PRIVATE buffer");
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("button")]
    [InlineData("source")]
    public async Task DetachedBufferFindControlsCannotSelectOrRepublishIntoTheStillOpenEditor(string target)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "PRIVATE buffer";
            var raw = editor.Source.Text;
            editor.BufferFindQuery.Text = "PRIVATE";
            ClickBuffer(editor.BufferFindFirst);
            Control control = target switch
            {
                "query" => editor.BufferFindQuery,
                "button" => editor.BufferFindFirst,
                _ => editor.Source,
            };
            ((Panel)control.Parent!).Children.Remove(control);
            editor.BufferFindQuery.Text.Should().BeEmpty();
            editor.BufferFindOutcome.Text.Should().BeNull();
            editor.Source.SelectedText.Should().BeEmpty();
            ClickBuffer(editor.BufferFindFirst);
            ClickBuffer(editor.BufferFindNext);
            editor.Source.RaiseEvent(FindKey(Key.F3));
            editor.BufferFindOutcome.Text.Should().BeNull();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.Source.Text.Should().Be(raw);
        });
    }

    [Fact]
    public async Task BufferFindNeverPublishesPrivateQueryOrDirtyBodyToEvidenceTranscriptOrSpeech()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, true);
            var editor = f.Editor!;
            const string literal = "PRIVATE_EDITOR_QUERY_7346";
            const string body = "PRIVATE_EDITOR_BODY_9815";
            var transcript = f.Session.Main.Transcript;
            var speaking = f.Session.Main.IsSpeaking;
            editor.Source.Text = body + " " + literal;
            editor.BufferFindQuery.Text = literal;
            ClickBuffer(editor.BufferFindFirst);
            editor.Source.SelectedText.Should().Be(literal);
            ClickBuffer(editor.BufferFindNext);
            ClickBuffer(editor.BufferFindPrevious);
            ClickBuffer(editor.BufferFindClear);
            f.Session.Main.Transcript.Should().Be(transcript);
            f.Session.Main.IsSpeaking.Should().Be(speaking);
            using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Evidence);
            string? cursor = null;
            var pages = 0;
            do
            {
                var page = await f.Session.Evidence.QueryAsync(new() { Limit = 50 }, cursor, f.Session.Token);
                System.Text.Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(page))
                    .Should().NotContain(literal).And.NotContain(body);
                cursor = page.Cursor;
                (++pages).Should().BeLessThan(20);
            } while (cursor is not null);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Fact]
    public async Task BufferFindRetiresBeforeLoadingAndDoesNotReadSourcesWhileLoadIsPending()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, false);
            var editor = f.Editor!;
            editor.BufferFindQuery.Text = "PRIVATE";
            ClickBuffer(editor.BufferFindFirst);
            f.Sessions.PauseNext = true;
            var loading = f.Run("prompts reload");
            await f.Sessions.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            editor.BufferFindQuery.Text.Should().BeEmpty();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            var reads = f.Store.Reads;
            var lists = f.Store.Lists;
            ClickBuffer(editor.BufferFindFirst);
            editor.BufferFindOutcome.Text.Should().Contain("Find unavailable");
            f.Store.Reads.Should().Be(reads);
            f.Store.Lists.Should().Be(lists);
            f.Sessions.Release.TrySetResult();
            await loading;
            editor.BufferFindQuery.Text = "PRIVATE";
            ClickBuffer(editor.BufferFindFirst);
            editor.Source.SelectedText.Should().Be("PRIVATE");
        });
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("close")]
    public async Task BufferFindNeverDiscardsDirtyDraftAndCancelledNativeReviewRetainsKeyboardRecovery(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, true);
            var editor = f.Editor!;
            editor.Source.Text = "dirty literal draft";
            editor.BufferFindQuery.Text = "literal";
            ClickBuffer(editor.BufferFindFirst);
            Window? review = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((candidate, _) =>
            { if (!ReferenceEquals(editor, candidate)) { review = candidate; } });
            Task? switching = null;
            if (action is "kind") { switching = f.Run("instructions create different"); }
            else { editor.Close(); }
            review.Should().NotBeNull();
            review!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (switching is not null) { await switching; }
            editor.IsVisible.Should().BeTrue();
            editor.KindInput.SelectedItem.Should().Be(ArtifactKind.Prompt);
            editor.Source.Text.Should().Be("dirty literal draft");
            editor.BufferFindOutcome.Text.Should().BeNull();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.Source.RaiseEvent(FindKey(Key.F, KeyModifiers.Control));
            editor.BufferFindQuery.IsFocused.Should().BeTrue();
            editor.BufferFindQuery.Text = "literal";
            editor.BufferFindQuery.RaiseEvent(FindKey(Key.Enter));
            editor.Source.SelectedText.Should().Be("literal");
            f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("file")]
    [InlineData("owner")]
    [InlineData("host")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("hide")]
    public async Task BufferFindRetiresOnExactBufferIdentityAndPrivateLifetimeChanges(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var editor = f.Editor!;
            editor.BufferFindQuery.Text = "PRIVATE";
            ClickBuffer(editor.BufferFindFirst);
            editor.BufferFindNext.IsEnabled.Should().BeTrue();
            var raw = editor.Source.Text;
            f.Store.DenyAccess = true;
            switch (change)
            {
                case "edit": editor.Source.Text += " dirty"; break;
                case "name": editor.NameInput.Text = "other"; break;
                case "kind": editor.SelectKind(ArtifactKind.Prompt); break;
                case "file": editor.Files.SelectedItem = null; break;
                case "owner": f.Ownership++; break;
                case "host": f.Owned = false; break;
                case "private": editor.ClearPrivateText(); break;
                case "close": editor.Close(); break;
                case "hide": editor.Hide(); break;
            }
            if (change is "owner" or "host")
            { ClickBuffer(editor.BufferFindNext); }
            editor.BufferFindOutcome.Text.Should().NotContain("Match ").And.NotContain(" of ");
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.BufferFindPrevious.IsEnabled.Should().BeFalse();
            if (change is "edit" or "file")
            {
                editor.BufferFindQuery.Text = "PRIVATE";
                ClickBuffer(editor.BufferFindNext);
                editor.BufferFindOutcome.Text.Should().Contain("Find first");
                editor.Source.Text.Should().Be(change is "edit" ? raw + " dirty" : raw);
            }
            if (change is "name" or "kind")
            {
                ClickBuffer(editor.BufferFindFirst);
                editor.BufferFindOutcome.Text.Should().Contain("Find unavailable");
                editor.Source.Text.Should().Be(raw);
            }
            if (change is "private" or "close")
            {
                editor.Source.Text.Should().BeEmpty();
                editor.BufferFindQuery.Text.Should().BeEmpty();
                ClickBuffer(editor.BufferFindFirst);
                if (change is "close") { editor.BufferFindOutcome.Text.Should().BeNull(); }
                else { editor.BufferFindOutcome.Text.Should().Contain("Find unavailable"); }
            }
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("same-query")]
    [InlineData("clear")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("selection")]
    [InlineData("focus")]
    public async Task BufferFindCannotPublishThroughReentrantOutcomeSelectionOrFocusChanges(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "PRIVATE buffer";
            editor.BufferFindQuery.Text = "PRIVATE";
            editor.BufferFindQuery.Focus().Should().BeTrue();
            var changed = false;
            void Retire()
            {
                changed = true;
                switch (change)
                {
                    case "query": editor.BufferFindQuery.Text = "NEW"; break;
                    case "same-query": editor.BufferFindQuery.Text = "NEW"; editor.BufferFindQuery.Text = "PRIVATE"; break;
                    case "clear": ClickBuffer(editor.BufferFindClear); break;
                    case "source": editor.Source.Text = "replacement"; break;
                    case "private": editor.ClearPrivateText(); break;
                    default: f.Owned = false; break;
                }
            }
            editor.BufferFindOutcome.PropertyChanged += (_, args) =>
            {
                if (!changed && change is not ("selection" or "focus") && args.Property == TextBlock.TextProperty
                    && editor.BufferFindOutcome.Text?.StartsWith("Match ", StringComparison.Ordinal) == true) { Retire(); }
            };
            editor.Source.PropertyChanged += (_, args) =>
            {
                if (!changed && change is "selection" && args.Property == TextBox.SelectionEndProperty) { Retire(); }
            };
            editor.Source.GotFocus += (_, _) => { if (!changed && change is "focus") { Retire(); } };
            ClickBuffer(editor.BufferFindFirst);
            changed.Should().BeTrue();
            editor.BufferFindOutcome.Text.Should().BeNull();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.BufferFindPrevious.IsEnabled.Should().BeFalse();
            editor.Source.SelectedText.Should().BeEmpty();
            if (change is "source") { editor.Source.Text.Should().Be("replacement"); }
            if (change is "same-query") { editor.BufferFindQuery.Text.Should().Be("PRIVATE"); }
        });
    }

    [Fact]
    public async Task BufferFindQueryAddsNoClipboardOrContextTransferAndRetiredCallbacksCannotRetarget()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, false);
            var old = f.Editor!;
            old.BufferFindQuery.Text = "PRIVATE";
            ClickBuffer(old.BufferFindFirst);
            old.Close();
            await f.Run("prompts inspect example");
            var replacement = f.Editor!;
            replacement.Should().NotBeSameAs(old);
            for (var phase = 0; phase < 2; phase++)
            {
                var query = phase == 0 ? old.BufferFindQuery : replacement.BufferFindQuery;
                foreach (var modifier in new[] { KeyModifiers.Control, KeyModifiers.Meta })
                {
                    foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                    {
                        var args = FindKey(key, modifier);
                        query.RaiseEvent(args);
                        args.Handled.Should().BeTrue();
                    }
                }
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                var context = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                query.RaiseEvent(context);
                context.Handled.Should().BeTrue();
                query.ContextMenu.Should().BeNull();
                query.IsUndoEnabled.Should().BeFalse();
            }
            ClickBuffer(old.BufferFindFirst);
            ClickBuffer(old.BufferFindNext);
            ClickBuffer(old.BufferFindClear);
            old.Source.RaiseEvent(FindKey(Key.F3));
            old.Source.Text.Should().BeEmpty();
            old.BufferFindQuery.Text.Should().BeEmpty();
            old.BufferFindOutcome.Text.Should().BeNull();
            replacement.BufferFindQuery.Text.Should().BeNullOrEmpty();
            replacement.BufferFindOutcome.Text.Should().BeNull();
            replacement.Source.Text.Should().Contain("PRIVATE");
        });
    }

    [Fact]
    public async Task BufferFindFocusFailureAndReadonlyOwnedSourceNeverClaimEditingOrUseAuthority()
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "PRIVATE readonly buffer";
            editor.Source.IsReadOnly = true;
            editor.BufferFindQuery.Text = "PRIVATE";
            ClickBuffer(editor.BufferFindFirst);
            editor.Source.SelectedText.Should().Be("PRIVATE");
            editor.Source.IsReadOnly.Should().BeTrue();
            var source = editor.Source.Text;
            editor.Source.Focusable = false;
            ClickBuffer(editor.BufferFindFirst);
            editor.BufferFindOutcome.Text.Should().Contain("focus/selection unavailable").And.Contain("no match or count claimed");
            editor.Source.SelectedText.Should().BeEmpty();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.Source.Text.Should().Be(source);
            editor.Source.IsReadOnly.Should().BeTrue();
        });
    }

    [Fact]
    public async Task UnboundSourceCannotAcquireOwnedBufferFindAuthority()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new OwnedInstructionEditorWindow(_ => throw new InvalidOperationException("Find must not dispatch editor actions."));
            window.Show();
            try
            {
                window.Source.Text = "profile or bundled private body";
                window.Source.IsReadOnly = true;
                window.BufferFindQuery.Text = "private";
                ClickBuffer(window.BufferFindFirst);
                window.BufferFindOutcome.Text.Should().Contain("Find unavailable");
                window.Source.SelectedText.Should().BeEmpty();
                window.Source.IsReadOnly.Should().BeTrue();
            }
            finally { window.Close(); }
        });
    }
}
