using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Artifacts;
using Kora.Core.Context;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class OwnedDefinitionEditorWindowTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CompareFindIsIndependentPrivateInertAndAccessibleWithoutStoreOrEditorMutation(ArtifactKind kind)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(kind, false);
            f.Editor!.Source.Text = "ALPHA alpha alpha .*? \r\n\U0001f642e\u0301 \u00e9 aaaa";
            var raw = f.Editor.Source.Text;
            var catalogue = f.Catalogue.Candidates;
            var items = f.Editor.Files.ItemsSource;
            f.Store.DenyAccess = true;
            var dialog = await OpenFindComparison(f);
            var panes = dialog.GetVisualDescendants().OfType<OwnedComparisonFindPane>().ToArray();
            var baseline = panes[0];
            var draft = panes[1];
            var original = FindSource(baseline).Text;
            FindQuery(baseline).Text = "PRIVATE_ORIGINAL_BODY";
            FindClick(baseline, "Find first");
            FindSpan(baseline).Text.Should().Be("PRIVATE_ORIGINAL_BODY");
            var baselineOutcome = FindOutcome(baseline).Text;
            var query = FindQuery(draft);
            query.Text = "alpha";
            FindButton(draft, "Find next").IsEnabled.Should().BeFalse();
            FindClick(draft, "Find first");
            FindOutcome(draft).Text.Should().Contain("UTF-16 [6, 11)");
            FindSpan(draft).Text.Should().Be("alpha");
            FindSpan(draft).IsFocused.Should().BeTrue();
            var peer = ControlAutomationPeer.CreatePeerForElement(FindSpan(draft))!;
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
            peer.GetName().Should().Contain("Exact raw memory draft").And.Contain("exact inert literal match").And.Contain("alpha");
            ControlAutomationPeer.CreatePeerForElement(FindOutcome(draft))!.GetName().Should().Contain("UTF-16 [6, 11)");
            AutomationProperties.GetName(query).Should().Contain("case-sensitive ordinal").And.Contain("256").And.Contain("512");
            foreach (var button in draft.GetVisualDescendants().OfType<Button>().Where(button => button.Content is string))
            { AutomationProperties.GetName(button).Should().Contain("Exact raw memory draft"); }
            FindClick(draft, "Find next");
            FindOutcome(draft).Text.Should().Contain("UTF-16 [12, 17)");
            FindClick(draft, "Find next");
            FindOutcome(draft).Text.Should().Contain("End of forward direction").And.Contain("no wrap").And.NotContain("No literal match");
            FindSpan(draft).IsVisible.Should().BeFalse();
            FindClick(draft, "Find previous");
            FindOutcome(draft).Text.Should().Contain("UTF-16 [6, 11)");
            FindClick(draft, "Find previous");
            FindOutcome(draft).Text.Should().Contain("End of backward direction");
            query.Text = "ALPHA";
            FindSpan(draft).Text.Should().BeNull();
            FindOutcome(draft).Text.Should().BeNull();
            FindButton(draft, "Find previous").IsEnabled.Should().BeFalse();
            FindClick(draft, "Find next");
            FindOutcome(draft).Text.Should().Contain("Find first after editing");
            FindClick(draft, "Find first");
            FindOutcome(draft).Text.Should().Contain("UTF-16 [0, 5)");
            query.Text = " .*? ";
            FindClick(draft, "Find first");
            FindSpan(draft).Text.Should().Be(" .*? ");
            query.Text = "missing";
            FindClick(draft, "Find first");
            FindOutcome(draft).Text.Should().Be("No literal match in this exact pane snapshot.");
            FindButton(draft, "Find next").IsEnabled.Should().BeFalse();
            FindClick(draft, "Clear Find");
            query.Text.Should().BeEmpty();
            FindOutcome(draft).Text.Should().BeNull();
            FindSpan(draft).Text.Should().BeNull();
            FindOutcome(baseline).Text.Should().Be(baselineOutcome);
            FindSource(baseline).Text.Should().Be(original);
            FindSource(draft).Text.Should().Be(raw);
            f.Editor.Source.Text.Should().Be(raw);
            f.Editor.Files.ItemsSource.Should().BeSameAs(items);
            f.Catalogue.Candidates.Should().BeSameAs(catalogue);
            f.Store.Reads.Should().Be(0); f.Store.Lists.Should().Be(0); f.Store.Writes.Should().Be(0);
            dialog.Close();
            foreach (var pane in panes)
            {
                FindQuery(pane).Text.Should().BeEmpty();
                FindSource(pane).Text.Should().BeEmpty();
                FindSpan(pane).Text.Should().BeNull();
            }
        });
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("characters")]
    [InlineData("bytes")]
    [InlineData("surrogate")]
    [InlineData("newline")]
    [InlineData("control")]
    [InlineData("format")]
    [InlineData("separator")]
    public async Task CompareFindRejectsInvalidQueryVisiblyWithoutTruncationOrOldMatch(string invalid)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, false);
            var dialog = await OpenFindComparison(f);
            var pane = dialog.GetVisualDescendants().OfType<OwnedComparisonFindPane>().Last();
            FindQuery(pane).Text = "PRIVATE";
            FindClick(pane, "Find first");
            FindSpan(pane).Text.Should().Be("PRIVATE");
            var literal = invalid switch
            {
                "empty" => string.Empty,
                "characters" => new string('a', LocalFileRetrievalPolicy.MaximumQueryCharacters + 1),
                "bytes" => new string('\u754c', LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes / 3 + 1),
                "surrogate" => "\ud800",
                "newline" => "PRIVATE\r\n",
                "control" => "PRIVATE\t",
                "format" => "\u202ePRIVATE",
                _ => "PRIVATE\u2028",
            };
            FindQuery(pane).Text = literal;
            FindSpan(pane).Text.Should().BeNull();
            FindClick(pane, "Find first");
            FindOutcome(pane).Text.Should().Contain("Invalid literal Find query").And.Contain("Nothing was truncated");
            FindQuery(pane).Text.Should().Be(literal);
            FindSpan(pane).IsVisible.Should().BeFalse();
            FindButton(pane, "Find next").IsEnabled.Should().BeFalse();
            FindButton(pane, "Find previous").IsEnabled.Should().BeFalse();
            dialog.Close();
        });
    }

    [Theory]
    [InlineData("\U0001f642", 0, 2)]
    [InlineData("e\u0301", 2, 2)]
    [InlineData("\u00e9", 5, 1)]
    [InlineData(" ", 4, 1)]
    [InlineData("aa", 7, 2)]
    public async Task CompareFindKeepsExactUtf16UnicodeNewlinesAndNonoverlappingNavigation(string literal, int start, int length)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, true);
            f.Editor!.Source.Text = "\U0001f642e\u0301 \u00e9 aaaa\r\nfinal\rline\n";
            var raw = f.Editor.Source.Text;
            var dialog = await OpenFindComparison(f);
            var pane = dialog.GetVisualDescendants().OfType<OwnedComparisonFindPane>().Last();
            FindQuery(pane).Text = literal;
            FindClick(pane, "Find first");
            FindOutcome(pane).Text.Should().Contain($"UTF-16 [{start}, {start + length})");
            FindSpan(pane).Text.Should().Be(literal);
            if (literal is "aa")
            {
                FindClick(pane, "Find next");
                FindOutcome(pane).Text.Should().Contain("UTF-16 [9, 11)");
                FindClick(pane, "Find next");
                FindOutcome(pane).Text.Should().Contain("End of forward");
                FindClick(pane, "Find previous");
                FindOutcome(pane).Text.Should().Contain("UTF-16 [7, 9)");
            }
            FindSource(pane).Text.Should().Be(raw);
            f.Editor.Source.Text.Should().Be(raw);
            dialog.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompareFindAcceptsExactQueryBoundsInCompleteMaximumRawPane(bool utf8Bound)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Instruction, true);
            var literal = utf8Bound ? new string('\u00e9', 256) : new string('a', 256);
            f.Editor!.Source.Text = new string('x', 65536 - System.Text.Encoding.UTF8.GetByteCount(literal)) + literal;
            var raw = f.Editor.Source.Text;
            var dialog = await OpenFindComparison(f);
            dialog.Width = 640; dialog.Height = 500;
            var pane = dialog.GetVisualDescendants().OfType<OwnedComparisonFindPane>().Last();
            FindQuery(pane).Text = literal;
            FindClick(pane, "Find first");
            FindOutcome(pane).Text.Should().Contain($"UTF-16 [{raw.Length - literal.Length}, {raw.Length})");
            FindSpan(pane).Text.Should().Be(literal);
            var viewport = pane.GetVisualDescendants().OfType<ScrollViewer>().Single(view => ReferenceEquals(view.Content, FindSpan(pane)));
            viewport.MaxHeight.Should().Be(100);
            viewport.Bounds.Height.Should().BeLessThanOrEqualTo(100);
            FindSpan(pane).IsFocused.Should().BeTrue();
            FindSource(pane).Bounds.Height.Should().BeGreaterThan(0);
            FindSource(pane).Text.Should().Be(raw);
            dialog.Close();
        });
    }

    [Fact]
    public async Task CompareFindMissingBaselineIsLabelledAbsenceNotSearchablePlaceholderAndKeyboardStaysPaneLocal()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = await DraftValidationFixture.Create(ArtifactKind.Prompt, true);
            f.Editor!.Source.Text = "alpha alpha";
            var dialog = await OpenFindComparison(f);
            var panes = dialog.GetVisualDescendants().OfType<OwnedComparisonFindPane>().ToArray();
            FindSource(panes[0]).Text.Should().BeEmpty();
            FindQuery(panes[0]).IsEnabled.Should().BeFalse();
            FindButton(panes[0], "Find first").IsEnabled.Should().BeFalse();
            panes[0].GetVisualDescendants().OfType<TextBlock>().Should().Contain(block => block.Text != null && block.Text.Contains("missing/new baseline; no searchable source", StringComparison.Ordinal));
            FindQuery(panes[0]).Text = "missing";
            FindClick(panes[0], "Find first");
            FindSpan(panes[0]).Text.Should().BeNull();
            var draft = panes[1];
            FindSource(draft).RaiseEvent(FindKey(Key.F, KeyModifiers.Control));
            FindQuery(draft).IsFocused.Should().BeTrue();
            FindQuery(draft).Text = "alpha";
            FindQuery(draft).RaiseEvent(FindKey(Key.Enter));
            FindOutcome(draft).Text.Should().Contain("UTF-16 [0, 5)");
            FindSpan(draft).RaiseEvent(FindKey(Key.F3));
            FindOutcome(draft).Text.Should().Contain("UTF-16 [6, 11)");
            FindSpan(draft).RaiseEvent(FindKey(Key.F3, KeyModifiers.Shift));
            FindOutcome(draft).Text.Should().Contain("UTF-16 [0, 5)");
            FindSpan(draft).RaiseEvent(FindKey(Key.Escape));
            dialog.IsVisible.Should().BeFalse();
            FindSource(draft).Text.Should().BeEmpty();
        });
    }

    private static async Task<Window> OpenFindComparison(DraftValidationFixture f)
    {
        Window? dialog = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        { if (window.Title?.StartsWith("Passive owned", StringComparison.Ordinal) == true) { dialog = window; } });
        await f.Run(f.Prefix + " compare");
        return dialog ?? throw new InvalidOperationException("Expected passive comparison.");
    }

    private static TextBox FindQuery(OwnedComparisonFindPane pane) => pane.GetVisualDescendants().OfType<TextBox>().Single(box => !box.IsReadOnly);
    private static TextBox FindSource(OwnedComparisonFindPane pane) => pane.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsReadOnly);
    private static NamedTextBlock FindSpan(OwnedComparisonFindPane pane) => pane.GetVisualDescendants().OfType<NamedTextBlock>().Single(block => block.Focusable);
    private static NamedTextBlock FindOutcome(OwnedComparisonFindPane pane) => pane.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(block => AutomationProperties.GetName(block)?.Contains("Find outcome", StringComparison.Ordinal) == true);
    private static Button FindButton(OwnedComparisonFindPane pane, string caption) => pane.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption));
    private static void FindClick(OwnedComparisonFindPane pane, string caption) => FindButton(pane, caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static KeyEventArgs FindKey(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
}
