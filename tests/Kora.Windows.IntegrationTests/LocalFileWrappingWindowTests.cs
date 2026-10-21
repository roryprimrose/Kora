using System.Text;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileWrappingWindowTests
{
    private static readonly double[] Widths = [480, 1200, 640];
    private static readonly int[] Scales = [75, 200, 100];

    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("retained")]
    public async Task AdmittedProfilesPreserveActualNoWrapDefaultAndWrapOnlyOriginalWithNoReads(string profile)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var text = "\talpha  α\r\n🙂 alpha\n" + new string('x', 50_000) + "\t  \r\n";
            var folder = Folder(text, "second alpha");
            var file = folder.Files[0];
            var original = Encoding.UTF8.GetBytes(text);
            var reference = file.Reference;
            var reads = 0;
            var searches = 0;
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            Task<LocalFileSearchResult> Search(string query, LocalFileRetrievalMode mode, CancellationToken token)
            {
                searches++;
                return Task.FromResult(new LocalFileLexicalRetrieval().Search(folder, folder.Reference, query, mode,
                    DateTimeOffset.UnixEpoch, token));
            }
            switch (profile)
            {
                case "folder": window.ShowFolderRevision(folder, Search, () => true, () => Task.CompletedTask); break;
                case "retained":
                    window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file), Search, () => true,
                        () => Task.CompletedTask, resolveCurrent: _ => { reads++; return Task.FromResult(file); });
                    break;
                default: window.ShowRevision(file, Search, () => true, () => Task.CompletedTask); break;
            }
            window.Show();
            window.UpdateLayout();
            try
            {
                var source = Source(window);
                var viewport = source.GetVisualAncestors().OfType<ScrollViewer>().First();
                var scaleSource = NativeReaderTypography.Capture(source);
                var toggle = Toggle(window);
                toggle.IsChecked.Should().BeFalse();
                source.TextWrapping.Should().Be(TextWrapping.NoWrap);
                viewport.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                viewport.Extent.Width.Should().BeGreaterThan(viewport.Viewport.Width);
                viewport.Offset = new(100, 0);
                window.UpdateLayout();
                viewport.Offset.X.Should().BeGreaterThan(0);
                var find = FindQuery(window);
                find.Text = "alpha";
                await Press(window, "Find first");
                var match = Text(window, "Exact literal original source match", prefix: true);
                var location = Text(window, "Exact original source identity and end-exclusive location");
                var status = Text(window, "Literal Find outcome, not ranked retrieval");
                var before = (match.Text, location.Text, status.Text, match.FontSize);
                var notices = Text(window, "Local file preview privacy and unavailable capabilities").Text;
                var readsBefore = reads;
                var peer = ControlAutomationPeer.CreatePeerForElement(toggle)!;
                peer.GetName().Should().Be("Wrap admitted text");
                peer.GetAutomationControlType().Should().Be(AutomationControlType.CheckBox);
                var provider = peer.GetProvider<IToggleProvider>()!;
                provider.Should().NotBeNull();
                provider.Toggle();
                foreach (var width in Widths)
                {
                    window.Width = width;
                    window.Height = 720;
                    foreach (var scale in Scales)
                    {
                        scaleSource(scale);
                        window.UpdateLayout();
                        source.TextWrapping.Should().Be(TextWrapping.Wrap);
                        viewport.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
                        viewport.Extent.Width.Should().BeLessThanOrEqualTo(viewport.Viewport.Width + 1);
                        source.Bounds.Width.Should().BeLessThanOrEqualTo(viewport.Viewport.Width + 1);
                        viewport.Bounds.Height.Should().BeGreaterThan(0);
                    }
                }
                toggle.Focus().Should().BeTrue();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.UpdateLayout();
                toggle.IsChecked.Should().BeFalse();
                toggle.IsFocused.Should().BeTrue();
                source.TextWrapping.Should().Be(TextWrapping.NoWrap);
                viewport.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                viewport.Extent.Width.Should().BeGreaterThan(viewport.Viewport.Width);
                (match.Text, location.Text, status.Text, match.FontSize).Should().Be(before);
                find.Text.Should().Be("alpha");
                reads.Should().Be(readsBefore);
                searches.Should().Be(0);
                Text(window, "Local file preview privacy and unavailable capabilities").Text.Should().Be(notices);
                source.Text.Should().Be(text);
                file.Text.Should().Be(text);
                file.Reference.Should().Be(reference);
                Encoding.UTF8.GetBytes(file.Text).Should().Equal(original);
                file.Digest.Should().Be(LocalFilePolicy.Digest(original));
                await Press(window, "Find next");
                status.Text.Should().Contain("Literal match 2 of 2");
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task ReplacementReviewRevocationAndCloseRetireToggleWithoutHiddenPersistenceOrBodyRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("alpha alpha", "same window second");
            var failures = new List<string>();
            var current = true;
            var window = new LocalFilePreviewWindow(failures.Add);
            void Show() => window.ShowFolderRevision(folder, (_, _, _) => throw new InvalidOperationException("No search."),
                () => current, () => Task.CompletedTask);
            Show();
            window.Show();
            window.UpdateLayout();
            try
            {
                var first = Toggle(window);
                first.IsChecked = true;
                var selector = window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
                    AutomationProperties.GetName(box)?.StartsWith("Inspect each", StringComparison.Ordinal) == true);
                selector.SelectedItem = folder.Files[1];
                window.UpdateLayout();
                var second = Toggle(window);
                second.Should().NotBeSameAs(first);
                second.IsChecked.Should().BeFalse();
                first.IsChecked = false;
                first.IsChecked = true;
                Source(window).TextWrapping.Should().Be(TextWrapping.NoWrap);
                second.IsChecked = true;
                selector.SelectedItem = folder.Files[0];
                window.UpdateLayout();
                Toggle(window).IsChecked.Should().BeFalse();
                var oldReader = Source(window);
                var oldToggle = Toggle(window);
                window.ShowReview(folder.Files[0].Review, () => throw new InvalidOperationException("No admission."));
                window.UpdateLayout();
                window.GetVisualDescendants().OfType<CheckBox>().Should().BeEmpty();
                Source(window).Text.Should().BeNull();
                oldReader.Text.Should().BeNull();
                oldToggle.IsChecked = true;
                Source(window).Text.Should().BeNull();
                Show();
                window.UpdateLayout();
                Toggle(window).IsChecked.Should().BeFalse();
                var revokedReader = Source(window);
                var revokedToggle = Toggle(window);
                current = false;
                revokedToggle.IsChecked = true;
                revokedReader.Text.Should().BeNull();
                window.Content.Should().BeNull();
                failures.Should().ContainSingle().Which.Should().Contain("no longer current");
                current = true;
                Show();
                window.UpdateLayout();
                var closing = Toggle(window);
                var reader = Source(window);
                closing.IsChecked.Should().BeFalse();
                window.ClearAndClose();
                closing.IsChecked = true;
                reader.Text.Should().BeNull();
                window.Content.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("layout", false)]
    [InlineData("bring", false)]
    [InlineData("layout", true)]
    [InlineData("bring", true)]
    public async Task WrapDuringNativeFocusBoundaryPreservesLogicalMatchButSuppressesStaleFocus(string boundary, bool citation)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("alpha alpha");
            var file = folder.Files[0];
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            window.ShowRevision(file, (query, mode, token) => Task.FromResult(new LocalFileLexicalRetrieval()
                .Search(folder, folder.Reference, query, mode, DateTimeOffset.UnixEpoch, token)),
                () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                var query = FindQuery(window);
                if (citation)
                {
                    window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                        box.PlaceholderText?.StartsWith("Untrusted lexical query", StringComparison.Ordinal) == true).Text = "alpha";
                    await Press(window, "Search selected immutable revision locally");
                    var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
                    rows.SelectedIndex = 0;
                }
                else { query.Text = "alpha"; }
                query.Focus().Should().BeTrue();
                var toggled = false;
                void Wrap()
                {
                    if (toggled || !window.GetVisualDescendants().OfType<NamedTextBlock>().Any(block =>
                        AutomationProperties.GetName(block)?.StartsWith(citation ? "Exact cited original" : "Exact literal original",
                            StringComparison.Ordinal) == true)) { return; }
                    toggled = true;
                    Toggle(window).IsChecked = true;
                }
                window.LayoutUpdated += (_, _) => { if (string.Equals(boundary, "layout", StringComparison.Ordinal)) { Wrap(); } };
                window.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
                {
                    if (string.Equals(boundary, "bring", StringComparison.Ordinal)) { Wrap(); }
                });
                await Press(window, citation ? "Show exact cited source span" : "Find first");
                Dispatcher.UIThread.RunJobs();
                toggled.Should().BeTrue();
                query.IsFocused.Should().BeTrue();
                Source(window).Text.Should().Be(file.Text);
                Source(window).TextWrapping.Should().Be(TextWrapping.Wrap);
                if (!citation)
                {
                    Text(window, "Literal Find outcome, not ranked retrieval").Text.Should().Contain("Literal match 1 of 2");
                    query.Text.Should().Be("alpha");
                }
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("predicate")]
    [InlineData("predicate-false")]
    [InlineData("property")]
    public async Task ReentrantReplacementCannotLetRetiredToggleChangeNewSourceLayout(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("first", "second");
            var replace = false;
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("No failure expected."));
            void ShowSecond() => window.ShowRevision(folder.Files[1],
                (_, _, _) => throw new InvalidOperationException("No retrieval."), () => true, () => Task.CompletedTask);
            window.ShowRevision(folder.Files[0], (_, _, _) => throw new InvalidOperationException("No retrieval."),
                () =>
                {
                    if (replace && boundary.StartsWith("predicate", StringComparison.Ordinal))
                    {
                        ShowSecond();
                        return !string.Equals(boundary, "predicate-false", StringComparison.Ordinal);
                    }
                    return true;
                }, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                var reader = Source(window);
                var old = Toggle(window);
                reader.PropertyChanged += (_, args) =>
                {
                    if (replace && args.Property == TextBlock.TextWrappingProperty
                        && string.Equals(boundary, "property", StringComparison.Ordinal)) { ShowSecond(); }
                };
                replace = true;
                old.IsChecked = true;
                window.UpdateLayout();
                Source(window).Text.Should().Be("second");
                Source(window).TextWrapping.Should().Be(TextWrapping.NoWrap);
                Toggle(window).IsChecked.Should().BeFalse();
                reader.Text.Should().BeNull();
                old.IsChecked = false;
                old.IsChecked = true;
                Source(window).TextWrapping.Should().Be(TextWrapping.NoWrap);
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static CheckBox Toggle(Window window) => window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
        string.Equals(box.Content as string, "Wrap admitted text", StringComparison.Ordinal));
    private static NamedTextBlock Source(Window window) => Text(window, "Untrusted local file plain text content");
    private static NamedTextBlock Text(Window window, string name, bool prefix = false) =>
        window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block => prefix
            ? AutomationProperties.GetName(block)?.StartsWith(name, StringComparison.Ordinal) == true
            : string.Equals(AutomationProperties.GetName(block), name, StringComparison.Ordinal));
    private static TextBox FindQuery(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(box =>
        AutomationProperties.GetName(box)?.StartsWith("Case-sensitive literal Find", StringComparison.Ordinal) == true);
    private static Task Press(Window window, string caption)
    {
        window.UpdateLayout();
        var button = window.GetVisualDescendants().OfType<Button>().Single(button =>
            string.Equals(button.Content as string, caption, StringComparison.Ordinal));
        return ((AsyncCommand)button.Command!).ExecuteAsync();
    }
    private static LocalFolderRevision Folder(params string[] texts)
    {
        var source = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var metadata = texts.Select((text, index) => new LocalFileMetadata($@"C:\Synthetic\{index}.md", $"file-{index}",
            Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)).ToArray();
        var review = new LocalFolderReview(reviewId, source, request, new(@"C:\Synthetic", "folder", metadata));
        return new(review, texts.Select((text, index) => new LocalFileRevision(new(reviewId, source, request, metadata[index]),
            Encoding.UTF8.GetBytes(text), DateTimeOffset.UnixEpoch)));
    }
}
