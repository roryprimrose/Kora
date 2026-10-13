using System.Text;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileLiteralFindWindowTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("retained")]
    public async Task NativeFindUsesOnlyCurrentOriginalFileAndOneInertBoundedSpanWithoutRankedSearch(string surface)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("irrelevant alpha", "α\r\n🙂 alpha alpha ALPHA .*?  ");
            var file = string.Equals(surface, "folder", StringComparison.Ordinal) ? folder.Files[0] : folder.Files[1];
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            var reads = 0;
            static Task<LocalFileSearchResult> Search(string _, LocalFileRetrievalMode mode, CancellationToken token) =>
                throw new InvalidOperationException("Literal navigation must not invoke ranked retrieval.");
            switch (surface)
            {
                case "folder": window.ShowFolderRevision(folder, Search, () => true, () => Task.CompletedTask); break;
                case "retained":
                    window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file),
                        Search, () => true, () => Task.CompletedTask, resolveCurrent: _ =>
                        {
                            reads++;
                            return Task.FromResult(file);
                        });
                    break;
                default: window.ShowRevision(file, Search, () => true, () => Task.CompletedTask); break;
            }
            window.Show();
            window.UpdateLayout();
            try
            {
                var query = Query(window);
                query.Focus().Should().BeTrue();
                query.Text = "alpha";
                Match(window).Should().BeNull();
                query.IsFocused.Should().BeTrue();
                if (string.Equals(surface, "folder", StringComparison.Ordinal))
                {
                    query.Text = "ALPHA";
                    await Press(window, "Find first");
                    Status(window).Text.Should().Be("No literal match in this exact original file.");
                    FileSelector(window).SelectedItem.Should().BeSameAs(file);
                    FileSelector(window).SelectedItem = folder.Files[1];
                    file = folder.Files[1];
                    query.Text.Should().BeNull();
                    query.Text = "alpha";
                }
                await Press(window, "Find first");
                var match = Match(window)!;
                match.Text.Should().Be("alpha");
                match.IsFocused.Should().BeTrue();
                var start = file.Text.IndexOf("alpha", StringComparison.Ordinal);
                Location(window).Text.Should().Contain($"UTF-16 [{start}, {start + 5})")
                    .And.Contain(file.Reference.SourceId.ToString("D")).And.Contain(file.RevisionId.ToString("D"))
                    .And.Contain(file.ItemId.ToString("D")).And.Contain(file.Digest).And.Contain("not a current filesystem");
                var peer = ControlAutomationPeer.CreatePeerForElement(match)!;
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
                peer.GetName().Should().Contain("Exact literal").And.Contain("alpha");
                match.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
                match.Text.Should().Be("alpha");
                window.GetVisualDescendants().OfType<TextBox>().Should().NotContain(box =>
                    string.Equals(box.Text, file.Text, StringComparison.Ordinal));
                Source(window).Text.Should().Be(file.Text);
                await Press(window, "Find next");
                Location(window).Text.Should().Contain($"UTF-16 [{start + 6}, {start + 11})");
                await Press(window, "Find next");
                Status(window).Text.Should().Contain("End of forward direction").And.NotContain("No literal match");
                Match(window).Should().BeNull();
                await Press(window, "Find previous");
                Location(window).Text.Should().Contain($"UTF-16 [{start}, {start + 5})");
                await Press(window, "Find previous");
                Status(window).Text.Should().Contain("End of backward direction");
                await Press(window, "Find first");
                query.Text = " .*? ";
                Match(window).Should().BeNull();
                await Press(window, "Find first");
                Match(window)!.Text.Should().Be(" .*? ");
                Click(window, "Clear literal Find");
                query.Text.Should().BeNull();
                Status(window).Text.Should().BeNull();
                Match(window).Should().BeNull();
                Button(window, "Find next").IsEnabled.Should().BeFalse();
                failures.Should().BeEmpty();
                reads.Should().Be(string.Equals(surface, "retained", StringComparison.Ordinal) ? 7 : 0);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("query")]
    [InlineData("mode")]
    [InlineData("ranked-query")]
    [InlineData("ranked-search")]
    [InlineData("cancel")]
    [InlineData("clear")]
    [InlineData("source")]
    [InlineData("review")]
    [InlineData("close")]
    [InlineData("revoked")]
    [InlineData("late-failure")]
    public async Task PendingValidatedReadCannotRevealOrFocusAfterExactNativeContextRetires(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("alpha", "alpha alpha").Files[1];
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = true;
            CancellationToken captured = default;
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file),
                (_, mode, _) => Task.FromResult(LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)),
                () => current, () => Task.CompletedTask, resolveCurrent: async token =>
                {
                    captured = token;
                    entered.SetResult();
                    await release.Task;
                    if (string.Equals(change, "late-failure", StringComparison.Ordinal)) { throw new IOException("retired storage callback"); }
                    return file;
                });
            window.Show();
            window.UpdateLayout();
            try
            {
                var query = Query(window);
                query.Text = "alpha";
                query.Focus().Should().BeTrue();
                var pending = Press(window, "Find first");
                await entered.Task;
                switch (change)
                {
                    case "query": query.Text = "alpha "; query.Text = "alpha"; break;
                    case "mode": Mode(window).SelectedIndex = 1; break;
                    case "ranked-query": RankedQuery(window).Text = "alpha"; break;
                    case "ranked-search": await Press(window, "Search selected immutable revision locally"); break;
                    case "cancel": Click(window, "Cancel local search and clear citations"); break;
                    case "clear": Click(window, "Clear literal Find"); break;
                    case "close": window.ClearAndClose(); break;
                    case "review": window.ShowReview(file.Review, () => Task.CompletedTask); break;
                    case "revoked": current = false; break;
                    default:
                        // Even the identical instance/body in a new composition cannot inherit an old cursor or query.
                        window.ShowRevision(file, (_, mode, _) => Task.FromResult(LocalFileSearchResult.Empty(
                            LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)), () => true, () => Task.CompletedTask);
                        break;
                }
                captured.IsCancellationRequested.Should().Be(!string.Equals(change, "revoked", StringComparison.Ordinal));
                release.SetResult();
                await pending;
                window.UpdateLayout();
                Match(window).Should().BeNull();
                query.IsFocused.Should().Be(change is "query" or "mode" or "ranked-query" or "ranked-search" or "cancel" or "clear" or "revoked");
                failures.Should().BeEmpty();
            }
            finally { release.TrySetResult(); window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing")]
    [InlineData("body")]
    [InlineData("failure")]
    [InlineData("no-reader")]
    [InlineData("invalid-query")]
    public async Task CurrentInvalidOrUnavailableSourceFailsExplicitlyWithoutMatchOrFallback(string failure)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("alpha", "alpha alpha").Files[1];
            var failures = new List<string>();
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var window = new LocalFilePreviewWindow(message => { failures.Add(message); failed.SetResult(); });
            window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file),
                (_, mode, _) => Task.FromResult(LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)),
                () => true, () => Task.CompletedTask, resolveCurrent: string.Equals(failure, "no-reader", StringComparison.Ordinal) ? null : _ =>
                {
                    reads++;
                    return failure switch
                    {
                        "foreign" => Task.FromResult(Folder("alpha", file.Text).Files[1]),
                        "missing" => Task.FromException<LocalFileRevision>(new InvalidOperationException("source removed or held")),
                        "body" => Task.FromResult(LocalFileRevision.Restore(file.Review,
                            Encoding.UTF8.GetBytes("other other"), DateTimeOffset.UnixEpoch, file.Reference)),
                        "failure" => Task.FromException<LocalFileRevision>(new IOException("private read or mandatory audit failed")),
                        _ => Task.FromResult(file),
                    };
                });
            window.Show();
            window.UpdateLayout();
            try
            {
                Query(window).Text = string.Equals(failure, "invalid-query", StringComparison.Ordinal) ? "SECRET_QUERY\n" : "alpha";
                Button(window, "Find first").Command!.Execute(null);
                await failed.Task;
                Match(window).Should().BeNull();
                Status(window).Text.Should().Contain("unavailable").And.NotContain("No literal match");
                failures.Should().ContainSingle().Which.Should().NotContain("SECRET_QUERY").And.NotContain(file.Text)
                    .And.NotContain(file.Review.Metadata.CanonicalPath);
                reads.Should().Be(failure is "no-reader" or "invalid-query" ? 0 : 1);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("layout")]
    [InlineData("bring")]
    public async Task ExactSourceRevocationBeforeFocusClearsLiteralDisclosure(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var file = Folder("alpha").Files[0];
            var current = true;
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            window.ShowRevision(file, (_, mode, _) => Task.FromResult(LocalFileSearchResult.Empty(
                LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)), () => current, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                Query(window).Text = "alpha";
                Query(window).Focus().Should().BeTrue();
                window.LayoutUpdated += (_, _) =>
                {
                    if (string.Equals(boundary, "layout", StringComparison.Ordinal) && Match(window) is not null) { current = false; }
                };
                window.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
                {
                    if (string.Equals(boundary, "bring", StringComparison.Ordinal) && Match(window) is not null) { current = false; }
                });
                await Press(window, "Find first");
                current.Should().BeFalse();
                Match(window).Should().BeNull();
                Status(window).Text.Should().Contain("stale");
                Query(window).IsFocused.Should().BeTrue();
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task CitationNavigationSwitchesFindSubjectToExactSelectedFolderItemWithoutQueryRetargeting()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("alpha", "alpha alpha");
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowFolderRevision(folder, (query, mode, token) => Task.FromResult(new LocalFileLexicalRetrieval()
                .Search(folder, folder.Reference, query, mode, DateTimeOffset.UnixEpoch, token)), () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                Query(window).Text = "alpha";
                await Press(window, "Find first");
                RankedQuery(window).Text = "alpha";
                Query(window).Text.Should().Be("alpha");
                Match(window).Should().BeNull();
                await Press(window, "Search selected immutable revision locally");
                var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
                rows.SelectedItem = rows.ItemsSource!.Cast<LocalFileCitation>().First(row => row.Source == folder.Files[1].Reference);
                await Press(window, "Show exact cited source span");
                FileSelector(window).SelectedItem.Should().BeSameAs(folder.Files[1]);
                Query(window).Text.Should().BeNull();
                Query(window).Text = "alpha";
                await Press(window, "Find first");
                Location(window).Text.Should().Contain(folder.Files[1].ItemId.ToString("D"));
                rows.ItemsSource.Should().NotBeNull();
                FileSelector(window).SelectedItem = folder.Files[0];
                Query(window).Text.Should().BeNull();
                Match(window).Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static TextBox Query(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(box =>
        AutomationProperties.GetName(box)?.StartsWith("Case-sensitive literal Find", StringComparison.Ordinal) == true);
    private static TextBox RankedQuery(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(box =>
        box.PlaceholderText?.StartsWith("Untrusted lexical query", StringComparison.Ordinal) == true);
    private static ComboBox FileSelector(Window window) => window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
        AutomationProperties.GetName(box)?.StartsWith("Inspect each", StringComparison.Ordinal) == true);
    private static ComboBox Mode(Window window) => window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
        AutomationProperties.GetName(box)?.StartsWith("Run-only", StringComparison.Ordinal) == true);
    private static NamedTextBlock? Match(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().SingleOrDefault(block =>
        AutomationProperties.GetName(block)?.StartsWith("Exact literal original", StringComparison.Ordinal) == true);
    private static NamedTextBlock Location(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), "Exact original source identity and end-exclusive location", StringComparison.Ordinal));
    private static NamedTextBlock Source(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), "Untrusted local file plain text content", StringComparison.Ordinal));
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), "Literal Find outcome, not ranked retrieval", StringComparison.Ordinal));
    private static Button Button(Window window, string caption) => window.GetVisualDescendants().OfType<Button>().Single(button =>
        string.Equals(button.Content as string, caption, StringComparison.Ordinal));
    private static Task Press(Window window, string caption)
    {
        window.UpdateLayout();
        return ((AsyncCommand)Button(window, caption).Command!).ExecuteAsync();
    }
    private static void Click(Window window, string caption) =>
        Button(window, caption).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
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