using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Core.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task BufferFindSelectsOnlyExactDisplayedDirtyTextWithCompleteCountAndNoSourceAuthority(ArtifactKind kind, bool newDraft)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, newDraft);
            var editor = f.Editor!;
            editor.Source.Text = "ALPHA alpha alpha .*? \r\n\U0001f642e\u0301 \u00e9 aaaaa";
            var raw = editor.Source.Text;
            var items = editor.Files.ItemsSource;
            var selected = editor.Files.SelectedItem;
            var candidates = f.Catalogue.Candidates;
            var command = f.Session.Main.CommandText;
            var canReview = editor.ReviewUse.IsEnabled;
            var canDisable = editor.DisableReviewedUse.IsEnabled;
            f.Store.DenyAccess = true;
            editor.BufferFindQuery.Text = "alpha";
            ClickBuffer(editor.BufferFindFirst);
            AssertBufferMatch(editor, 6, "alpha", 1, 2);
            ClickBuffer(editor.BufferFindNext);
            AssertBufferMatch(editor, 12, "alpha", 2, 2);
            ClickBuffer(editor.BufferFindNext);
            editor.BufferFindOutcome.Text.Should().Contain("No wrap").And.Contain("2 of 2").And.Contain("No match selected");
            editor.Source.SelectedText.Should().BeEmpty();
            ClickBuffer(editor.BufferFindPrevious);
            AssertBufferMatch(editor, 6, "alpha", 1, 2);
            ClickBuffer(editor.BufferFindPrevious);
            editor.BufferFindOutcome.Text.Should().Contain("backward").And.Contain("1 of 2");
            editor.Source.SelectedText.Should().BeEmpty();
            editor.BufferFindQuery.Text = "aa";
            editor.BufferFindOutcome.Text.Should().BeNull();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            ClickBuffer(editor.BufferFindNext);
            editor.BufferFindOutcome.Text.Should().Contain("Find first");
            ClickBuffer(editor.BufferFindFirst);
            AssertBufferMatch(editor, raw!.IndexOf("aaaaa", StringComparison.Ordinal), "aa", 1, 2);
            ClickBuffer(editor.BufferFindNext);
            AssertBufferMatch(editor, raw.IndexOf("aaaaa", StringComparison.Ordinal) + 2, "aa", 2, 2);
            ClickBuffer(editor.BufferFindPrevious);
            AssertBufferMatch(editor, raw.IndexOf("aaaaa", StringComparison.Ordinal), "aa", 1, 2);
            editor.BufferFindQuery.Text = " .*? ";
            ClickBuffer(editor.BufferFindFirst);
            editor.Source.SelectedText.Should().Be(" .*? ");
            editor.BufferFindQuery.Text = "missing";
            ClickBuffer(editor.BufferFindFirst);
            editor.BufferFindOutcome.Text.Should().Be("0 matches in this exact displayed owned buffer.");
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.Source.Text.Should().Be(raw);
            editor.Files.ItemsSource.Should().BeSameAs(items);
            editor.Files.SelectedItem.Should().BeSameAs(selected);
            f.Catalogue.Candidates.Should().BeSameAs(candidates);
            editor.ReviewUse.IsEnabled.Should().Be(canReview);
            editor.DisableReviewedUse.IsEnabled.Should().Be(canDisable);
            f.Session.Main.CommandText.Should().Be(command);
            editor.Source.IsReadOnly.Should().BeFalse();
            editor.Source.IsUndoEnabled.Should().BeTrue();
            AutomationProperties.GetName(editor.Source).Should().Contain("source editor");
            AutomationProperties.GetName(editor.BufferFindQuery).Should().Contain("256").And.Contain("512");
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("characters")]
    [InlineData("bytes")]
    [InlineData("high-surrogate")]
    [InlineData("low-surrogate")]
    [InlineData("a\r")]
    [InlineData("a\nb")]
    [InlineData("a\t")]
    [InlineData("a\0")]
    [InlineData("a\u202e")]
    [InlineData("a\u200d")]
    [InlineData("a\u2028")]
    [InlineData("a\u2029")]
    public async Task BufferFindRejectsMalformedOrOverBudgetLiteralWithoutTruncationOrStaleCount(string value)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "alpha alpha";
            editor.BufferFindQuery.Text = "alpha";
            ClickBuffer(editor.BufferFindFirst);
            var literal = value switch
            {
                "characters" => new string('x', 257),
                "bytes" => new string('\u754c', 171),
                "high-surrogate" => "\ud800",
                "low-surrogate" => "\udc00",
                _ => value,
            };
            editor.BufferFindQuery.Text = literal;
            editor.Source.SelectedText.Should().BeEmpty();
            editor.BufferFindOutcome.Text.Should().BeNull();
            ClickBuffer(editor.BufferFindFirst);
            editor.BufferFindOutcome.Text.Should().Contain("Invalid literal Find query").And.Contain("Nothing was truncated");
            editor.BufferFindQuery.Text.Should().Be(literal);
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
            editor.BufferFindPrevious.IsEnabled.Should().BeFalse();
            editor.Source.Text.Should().Be("alpha alpha");
        });
    }

    [Theory]
    [InlineData("\U0001f642", 0, 2)]
    [InlineData("e\u0301", 2, 2)]
    [InlineData("\u00e9", 5, 1)]
    [InlineData(" ", 4, 1)]
    [InlineData("line", 8, 4)]
    public async Task BufferFindUsesExactUtf16WithoutNormalizationOrNewlineRewriting(string query, int start, int length)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "\U0001f642e\u0301 \u00e9\r\nline\rfinal\n";
            var raw = editor.Source.Text;
            editor.BufferFindQuery.Text = query;
            ClickBuffer(editor.BufferFindFirst);
            editor.Source.SelectionStart.Should().Be(start);
            editor.Source.SelectionEnd.Should().Be(start + length);
            editor.Source.SelectedText.Should().Be(query);
            editor.Source.Text.Should().Be(raw);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BufferFindAcceptsExactQueryBudgetsAtEndOfCompleteLargeDirtyBuffer(bool utf8)
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            var query = new string(utf8 ? '\u00e9' : 'a', 256);
            editor.Source.Text = new string('x', 65536) + query;
            editor.BufferFindQuery.Text = query;
            ClickBuffer(editor.BufferFindFirst);
            AssertBufferMatch(editor, 65536, query, 1, 1);
            editor.Source.Bounds.Height.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public async Task BufferFindKeyboardAndClearPreserveNativeEditingAndUserSelection()
    {
        await HeadlessSession.RunAsync(() =>
        {
            using var f = new BufferFindControlFixture();
            var editor = f.Editor;
            editor.Source.Text = "alpha alpha";
            var enter = FindKey(Key.Enter);
            editor.Source.RaiseEvent(enter);
            editor.Source.Text.Should().Contain("\n", "Enter in the source must keep native newline editing semantics");
            editor.BufferFindOutcome.Text.Should().BeNull();
            editor.Source.Text = "alpha alpha";
            editor.Source.RaiseEvent(FindKey(Key.F, KeyModifiers.Control));
            editor.BufferFindQuery.IsFocused.Should().BeTrue();
            editor.BufferFindQuery.Text = "alpha";
            editor.BufferFindQuery.RaiseEvent(FindKey(Key.Enter));
            AssertBufferMatch(editor, 0, "alpha", 1, 2);
            editor.Source.RaiseEvent(FindKey(Key.F3));
            AssertBufferMatch(editor, 6, "alpha", 2, 2);
            editor.Source.RaiseEvent(FindKey(Key.F3, KeyModifiers.Shift));
            AssertBufferMatch(editor, 0, "alpha", 1, 2);
            editor.Source.SelectionStart = 1;
            editor.Source.SelectionEnd = 3;
            editor.BufferFindOutcome.Text.Should().BeNull();
            ClickBuffer(editor.BufferFindClear);
            editor.Source.SelectedText.Should().Be("lp", "Clear must not acquire or collapse a user's independent selection");
            editor.BufferFindQuery.Text.Should().BeEmpty();
            editor.BufferFindQuery.RaiseEvent(FindKey(Key.Escape));
            editor.Source.IsFocused.Should().BeTrue();
            editor.IsVisible.Should().BeTrue();
            editor.BufferFindNext.IsEnabled.Should().BeFalse();
        });
    }

    private static void ClickBuffer(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void AssertBufferMatch(OwnedInstructionEditorWindow editor, int start, string literal, int ordinal, int count)
    {
        editor.BufferFindOutcome.Text.Should().Be($"Match {ordinal} of {count}; UTF-16 [{start}, {start + literal.Length}); end exclusive.");
        editor.Source.SelectedText.Should().Be(literal);
        editor.Source.SelectionStart.Should().Be(start);
        editor.Source.SelectionEnd.Should().Be(start + literal.Length);
        editor.Source.IsFocused.Should().BeTrue();
    }

    private sealed class BufferFindControlFixture : IDisposable
    {
        internal readonly OwnedInstructionEditorWindow Editor;
        internal readonly List<string> Actions = [];
        internal bool Owned = true;

        internal BufferFindControlFixture()
        {
            Editor = new(Actions.Add);
            Editor.BindBufferFind(() => Owned && Editor.IsVisible ? () => Owned && Editor.IsVisible : null);
            Editor.Show();
        }

        public void Dispose()
        {
            Editor.ClearPrivateText();
            Editor.Close();
        }
    }
}
