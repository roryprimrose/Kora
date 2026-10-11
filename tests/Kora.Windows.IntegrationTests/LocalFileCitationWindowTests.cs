using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileCitationWindowTests
{
    [Theory]
    [InlineData(LocalFileRetrievalMode.LexicalLines, false)]
    [InlineData(LocalFileRetrievalMode.MarkdownStructure, false)]
    [InlineData(LocalFileRetrievalMode.LexicalLines, true)]
    [InlineData(LocalFileRetrievalMode.MarkdownStructure, true)]
    public async Task ExplicitNativeCitationActionRevealsExactOriginalSpanAndFolderItemWithoutPassiveFocusOrCopy(
        LocalFileRetrievalMode mode, bool folder)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var prefix = string.Join("\r\n", Enumerable.Repeat("inert preamble", 250)) + "\r\n\r\n";
            var source = "# Real e\u0301 🙂\r\nalpha inert <script>never execute</script>\r\n\r\n"
                + "Setext title\r\n===\r\n~~~\r\n# code\r\nalpha https://invalid/source ![alt](https://invalid/image)\r\n~~~\r\n";
            var files = Folder(prefix + source);
            var file = files.Files[1];
            LocalFileSearchResult? result = null;
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            Task<LocalFileSearchResult> Search(string query, LocalFileRetrievalMode actual, CancellationToken token)
            {
                result = folder
                    ? new LocalFileLexicalRetrieval().Search(files, files.Reference, query, actual, DateTimeOffset.UnixEpoch, token)
                    : new LocalFileLexicalRetrieval().Search(file, file.Reference, query, actual, DateTimeOffset.UnixEpoch, token);
                // Display identity is untrusted: duplicate labels cannot choose a different admitted item.
                result = result with { Citations = result.Citations.Select(row => row with { DisplayIdentity = "duplicate.md" }).ToArray() };
                return Task.FromResult(result);
            }
            if (folder) { window.ShowFolderRevision(files, Search, () => true, () => Task.CompletedTask); }
            else { window.ShowRevision(file, Search, () => true, () => Task.CompletedTask); }
            window.Show();
            window.UpdateLayout();
            try
            {
                var modes = window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
                    AutomationProperties.GetName(box)?.StartsWith("Run-only", StringComparison.Ordinal) == true);
                modes.SelectedIndex = mode == LocalFileRetrievalMode.MarkdownStructure ? 1 : 0;
                var query = Query(window);
                query.Text = "alpha";
                query.Focus().Should().BeTrue();
                await Press(window, "Search selected immutable revision locally");
                window.UpdateLayout();
                query.IsFocused.Should().BeTrue();
                var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
                var citation = result!.Citations.Last(row => row.Source == file.Reference);
                rows.SelectedItem = citation;
                query.IsFocused.Should().BeTrue();
                var button = FindButton(window, "Show exact cited source span");
                button.IsEnabled.Should().BeTrue();
                await Press(window, "Show exact cited source span");
                window.UpdateLayout();
                var span = ExactSpan(window);
                span.Text.Should().Be(file.Text.Substring(citation.Start, citation.Length)).And.Be(citation.Excerpt);
                span.IsFocused.Should().BeTrue();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(span);
                var peer = ControlAutomationPeer.CreatePeerForElement(span)!;
                peer.GetName().Should().Contain(file.ItemId.ToString("D")).And.Contain(span.Text!)
                    .And.Contain($"lines {citation.StartLine}:{citation.StartColumn}–{citation.EndLine}:{citation.EndColumn}");
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
                span.Bounds.Height.Should().BeGreaterThan(0);
                var scroll = span.GetVisualAncestors().OfType<ScrollViewer>().First();
                scroll.Bounds.Height.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(140);
                scroll.Offset.Y.Should().Be(0);
                var location = scroll.TranslatePoint(default, window)!.Value;
                location.Y.Should().BeGreaterThanOrEqualTo(0);
                (location.Y + scroll.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    string.Equals(AutomationProperties.GetName(block), "Untrusted local file plain text content", StringComparison.Ordinal))
                    .Text.Should().Be(file.Text);
                if (folder)
                {
                    window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
                        AutomationProperties.GetName(box)?.StartsWith("Inspect each", StringComparison.Ordinal) == true)
                        .SelectedItem.Should().BeSameAs(file);
                }
                window.GetVisualDescendants().OfType<TextBox>().Should().NotContain(box =>
                    string.Equals(box.Text, citation.Excerpt, StringComparison.Ordinal));
                span.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
                span.Text.Should().Be(citation.Excerpt);
                query.Text = "new query";
                span.Text.Should().BeNull();
                rows.ItemsSource.Should().BeNull();
                button.IsEnabled.Should().BeFalse();
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("mode")]
    [InlineData("cancel")]
    [InlineData("row")]
    [InlineData("source")]
    [InlineData("close")]
    [InlineData("replacement")]
    [InlineData("late-failure")]
    public async Task AwaitedSourceRevalidationCannotPublishOrFocusAfterNativeGenerationRetires(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("# actual\r\nalpha\r\n\r\n# second\r\nalpha\r\n").Files[1];
            var request = file.Review.Request;
            var retained = new Kora.Core.Storage.SessionFileAttachment(request.SessionId, new(1), new(1), file);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = true;
            CancellationToken captured = default;
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            var result = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha", DateTimeOffset.UnixEpoch, CancellationToken.None);
            window.ShowAttachment(retained, (_, _, _) => Task.FromResult(result), () => current, () => Task.CompletedTask,
                resolveCurrent: async token =>
                {
                    captured = token;
                    entered.SetResult();
                    await release.Task;
                    if (string.Equals(change, "late-failure", StringComparison.Ordinal)) { throw new IOException("retired source read failed"); }
                    return file;
                });
            window.Show();
            window.UpdateLayout();
            try
            {
                var query = Query(window);
                query.Text = "alpha";
                await Press(window, "Search selected immutable revision locally");
                var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
                rows.SelectedItem = result.Citations[0];
                query.Focus();
                var navigation = Press(window, "Show exact cited source span");
                await entered.Task;
                switch (change)
                {
                    case "query": query.Text = "changed"; break;
                    case "late-failure": query.Text = "changed"; break;
                    case "mode": window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 1; break;
                    case "cancel": FindButton(window, "Cancel local search and clear citations").RaiseEvent(new(Button.ClickEvent)); break;
                    case "row": rows.SelectedItem = result.Citations[1]; break;
                    case "source": current = false; break;
                    case "close": window.Close(); break;
                    default: window.ShowRevision(file, (_, mode, _) => Task.FromResult(
                        LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)),
                        () => true, () => Task.CompletedTask); break;
                }
                captured.IsCancellationRequested.Should().Be(!string.Equals(change, "source", StringComparison.Ordinal));
                release.SetResult();
                await navigation;
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().NotContain(block =>
                    (AutomationProperties.GetName(block) ?? string.Empty).StartsWith("Exact cited original source span", StringComparison.Ordinal));
                failures.Should().BeEmpty();
            }
            finally { release.TrySetResult(); window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("excerpt")]
    [InlineData("offset")]
    [InlineData("no-match")]
    public async Task NativeActionRefusesForeignOrForgedCurrentRowsAndNonSuccessfulResults(string invalid)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("alpha");
            var file = folder.Files[1];
            var result = new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha",
                DateTimeOffset.UnixEpoch, CancellationToken.None);
            var citation = result.Citations.Single();
            citation = invalid switch
            {
                "foreign" => citation with { Source = folder.Files[0].Reference },
                "excerpt" => citation with { Excerpt = "not source" },
                "offset" => citation with { Start = int.MaxValue, Length = int.MaxValue },
                _ => citation,
            };
            result = result with { Citations = [citation] };
            if (string.Equals(invalid, "no-match", StringComparison.Ordinal)) { result = result with { Outcome = LocalFileSearchOutcome.NoMatch }; }
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowRevision(file, (_, _, _) => Task.FromResult(result), () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                Query(window).Text = "alpha";
                await Press(window, "Search selected immutable revision locally");
                var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
                if (string.Equals(invalid, "no-match", StringComparison.Ordinal))
                {
                    rows.ItemsSource.Should().BeNull();
                    FindButton(window, "Show exact cited source span").IsEnabled.Should().BeFalse();
                    return;
                }
                rows.SelectedItem = citation;
                var navigate = () => Press(window, "Show exact cited source span");
                await navigate.Should().ThrowAsync<Exception>();
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().NotContain(block =>
                    (AutomationProperties.GetName(block) ?? string.Empty).StartsWith("Exact cited original source span", StringComparison.Ordinal));
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static NamedTextBlock ExactSpan(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        AutomationProperties.GetName(block)?.StartsWith("Exact cited original source span", StringComparison.Ordinal) == true);

    private static TextBox Query(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(box =>
        box.PlaceholderText?.StartsWith("Untrusted lexical query", StringComparison.Ordinal) == true);

    private static Button FindButton(Window window, string caption) => window.GetVisualDescendants().OfType<Button>().Single(button =>
        string.Equals(button.Content as string, caption, StringComparison.Ordinal));

    private static Task Press(Window window, string caption)
    {
        window.UpdateLayout();
        return ((AsyncCommand)FindButton(window, caption).Command!).ExecuteAsync();
    }

    private static LocalFolderRevision Folder(string second)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var reviewId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var contents = new[] { "# foreign\r\nalpha identical query, different exact source\r\n", second };
        var metadata = contents.Select((text, index) => new LocalFileMetadata(
            $@"C:\Synthetic\{index}.md", $"exact-{index}", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)).ToArray();
        var review = new LocalFolderReview(reviewId, sourceId, request, new(@"C:\Synthetic", "folder", metadata));
        return new(review, metadata.Select((item, index) => new LocalFileRevision(
            new(reviewId, sourceId, request, item), Encoding.UTF8.GetBytes(contents[index]), DateTimeOffset.UnixEpoch)));
    }
}
