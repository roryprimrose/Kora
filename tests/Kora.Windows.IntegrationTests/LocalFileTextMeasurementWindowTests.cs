using System.Text;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Communication;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileTextMeasurementWindowTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("retained")]
    public async Task AccessiblePassiveMetricsUseOnlyExactDisplayedAdmittedTextWithoutAnyActionOrRetainedRead(string surface)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var folder = Folder("\ufeff\u00e9\ud83d\ude42e\u0301\r\n", "OTHER_HIDDEN_ITEM");
            var file = folder.Files[0];
            var original = file.Reference;
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("No failure expected."));
            switch (surface)
            {
                case "folder": window.ShowFolderRevision(folder, NoSearch, () => true, NoAction); break;
                case "retained":
                    window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file),
                        NoSearch, () => true, NoAction, resolveCurrent: _ =>
                            throw new InvalidOperationException("Passive metrics must not read retained storage."));
                    break;
                default: window.ShowRevision(file, NoSearch, () => true, NoAction,
                    (_, _, _, _) => throw new InvalidOperationException("Passive metrics must not call a model.")); break;
            }
            window.Show();
            window.UpdateLayout();
            try
            {
                var metrics = Metrics(window);
                AssertMetrics(metrics, 8, 7, 14, 17);
                var peer = ControlAutomationPeer.CreatePeerForElement(metrics)!;
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
                peer.GetName().Should().Contain("Decoded/admitted display text metrics for this exact item")
                    .And.Contain("UTF-16 code units: 8").And.Contain("Unicode scalars: 7")
                    .And.Contain("strict UTF-8 re-encoding bytes: 14");
                metrics.Focusable.Should().BeFalse();
                window.GetVisualDescendants().OfType<TextBox>().Should().NotContain(box =>
                    string.Equals(box.Text, file.Text, StringComparison.Ordinal));
                var result = metrics.Text;
                foreach (var input in window.GetVisualDescendants().OfType<TextBox>()) { input.Text = "not an action"; }
                for (var index = 0; index < 5; index++)
                {
                    window.UpdateLayout();
                    peer.GetName().Should().Contain("UTF-16 code units: 8");
                    metrics.Text.Should().BeSameAs(result);
                }
                Source(window).Text.Should().Be(file.Text);
                file.Reference.Should().Be(original);
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().NotContain(block =>
                    string.Equals(AutomationProperties.GetName(block), "Literal Find outcome, not ranked retrieval",
                        StringComparison.Ordinal) && block.Text != null);
                if (!string.Equals(surface, "folder", StringComparison.Ordinal))
                {
                    window.GetVisualDescendants().OfType<NamedTextBlock>().Should().NotContain(block =>
                        block.Text != null && block.Text.Contains("OTHER_HIDDEN_ITEM", StringComparison.Ordinal));
                }
            }
            finally { window.ClearAndClose(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EmptyAdmittedTextIsZeroButMissingSelectionAndStaleAdmissionAreUnavailableNotEmptyFiles()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var folder = Folder("", "abc");
            var current = true;
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowFolderRevision(folder, NoSearch, () => current, NoAction);
            window.Show();
            window.UpdateLayout();
            try
            {
                var metrics = Metrics(window);
                AssertMetrics(metrics, 0, 0, 0, 3);
                var files = Files(window);
                files.SelectedItem = null;
                metrics.Text.Should().Contain("unavailable").And.NotContain("code units: 0").And.NotContain("Original source bytes");
                Source(window).Text.Should().BeNull();
                files.SelectedItem = folder.Files[1];
                AssertMetrics(metrics, 3, 3, 3, 6);
                current = false;
                files.SelectedItem = folder.Files[0];
                metrics.Text.Should().Contain("unavailable").And.NotContain("Original source bytes");
                Source(window).Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
            window.Content.Should().BeNull();
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationReentrancyRetiresOldMetricsWithoutLeakingIntoReplacementOrDisposedPresentation(bool close)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var folder = Folder("first", "second-longer");
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowFolderRevision(folder, NoSearch, () => true, NoAction);
            window.Show();
            window.UpdateLayout();
            var old = Metrics(window);
            var changed = false;
            old.PropertyChanged += (_, args) =>
            {
                if (args.Property != TextBlock.TextProperty || changed
                    || old.Text?.Contains("code units: 13", StringComparison.Ordinal) != true) { return; }
                changed = true;
                if (close) { window.ClearAndClose(); }
                else { window.ShowRevision(Folder("new").Files[0], NoSearch, () => true, NoAction); }
            };
            Files(window).SelectedItem = folder.Files[1];
            changed.Should().BeTrue();
            old.Text.Should().Contain("unavailable").And.NotContain("13");
            if (!close)
            {
                window.UpdateLayout();
                AssertMetrics(Metrics(window), 3, 3, 3, 6);
                Source(window).Text.Should().Be("new");
            }
            window.ClearAndClose();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ReentrantAdmissionCheckCannotPublishOldCountsOrRestoreRetiredSource()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new LocalFilePreviewWindow(_ => { });
            var checks = 0;
            window.ShowRevision(Folder("old-secret").Files[0], NoSearch, () =>
            {
                if (++checks == 2) { window.ShowRevision(Folder("new").Files[0], NoSearch, () => true, NoAction); }
                return true;
            }, NoAction);
            window.Show();
            window.UpdateLayout();
            try
            {
                AssertMetrics(Metrics(window), 3, 3, 3, 6);
                Source(window).Text.Should().Be("new");
            }
            finally { window.ClearAndClose(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RetiredFolderSelectorCannotRebindANewPresentationToAnOldItemEvenWithTheSameSourcePath()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var folder = Folder("old", "old-private-longer");
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowFolderRevision(folder, NoSearch, () => true, NoAction);
            window.Show();
            window.UpdateLayout();
            var oldSelector = Files(window);
            var oldMetrics = Metrics(window);
            var replacement = Folder("new").Files[0];
            window.ShowRevision(replacement, NoSearch, () => true, NoAction);
            window.UpdateLayout();
            try
            {
                oldSelector.SelectedItem = folder.Files[1];
                AssertMetrics(Metrics(window), 3, 3, 3, 6);
                Source(window).Text.Should().Be(replacement.Text);
                oldMetrics.Text.Should().Contain("unavailable").And.NotContain("Original source bytes");
            }
            finally { window.ClearAndClose(); }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("content")]
    public async Task HostOrDisplayRetirementClearsPrivateMetricsAndNeverRestoresThemOnLaterNotifications(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath(), silentCaption: true);
            var calls = new ObservedCalls();
            await fixture.InitializeAsync(null, callState: calls);
            fixture.Main.BindClipboardOwnershipGate(() => true);
            var window = new LocalFilePreviewWindow(_ => { }, fixture.Main);
            window.ShowRevision(Folder("private").Files[0], NoSearch, () => true, NoAction);
            window.Show();
            window.UpdateLayout();
            try
            {
                var metrics = Metrics(window);
                AssertMetrics(metrics, 7, 7, 7, 10);
                switch (change)
                {
                    case "input": fixture.Main.CommandText = "new input"; break;
                    case "owner":
                        fixture.Main.BindCallOwnershipGate(() => false);
                        fixture.Main.CommandText = "owner retired";
                        break;
                    case "privacy": fixture.Access.SetOpen(false); break;
                    case "call": calls.Set(CallState.Active); break;
                    case "dispose": fixture.Main.Dispose(); break;
                    case "content": Source(window).Text = null; break;
                }
                Dispatcher.UIThread.RunJobs();
                metrics.Text.Should().Contain("unavailable").And.NotContain("7").And.NotContain("Original source bytes");
                fixture.Main.CommandText = string.Empty;
                metrics.Text.Should().Contain("unavailable");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task StaleAdmissionBeforeMeasurementIsExplicitlyUnavailableAndDoesNotRetainAZeroCount()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new LocalFilePreviewWindow(_ => { });
            window.ShowRevision(Folder("").Files[0], NoSearch, () => false, NoAction);
            window.Show();
            window.UpdateLayout();
            try
            {
                Metrics(window).Text.Should().Contain("unavailable").And.NotContain("code units: 0")
                    .And.NotContain("Original source bytes");
            }
            finally { window.ClearAndClose(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task QueuedHostRetirementOwnsOnlyItsOldControlAndCannotClearReplacementMetrics()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath(), silentCaption: true);
            await fixture.InitializeAsync();
            fixture.Main.BindClipboardOwnershipGate(() => true);
            var window = new LocalFilePreviewWindow(_ => { }, fixture.Main);
            window.ShowRevision(Folder("old").Files[0], NoSearch, () => true, NoAction);
            window.Show();
            window.UpdateLayout();
            var old = Metrics(window);
            // Property notification delivery is intentionally off the native dispatcher.
            await Task.Run(() => fixture.Main.CommandText = "new input");
            window.ShowRevision(Folder("replacement").Files[0], NoSearch, () => true, NoAction);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            try
            {
                old.Text.Should().Contain("unavailable");
                AssertMetrics(Metrics(window), 11, 11, 11, 14);
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static void AssertMetrics(NamedTextBlock text, int units, int scalars, int bytes, int original) =>
        text.Text.Should().Contain($"UTF-16 code units: {units};").And.Contain($"Unicode scalars: {scalars};")
            .And.Contain($"strict UTF-8 re-encoding bytes: {bytes}.")
            .And.Contain($"Original source bytes at admission (including any stripped leading BOM): {original}.")
            .And.Contain("not original/on-disk encoding or bytes, retained storage bytes, folder totals or model tokens")
            .And.NotContain("4096").And.NotContain("16384");

    private static NamedTextBlock Metrics(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), "Decoded/admitted display text metrics for this exact item", StringComparison.Ordinal));
    private static NamedTextBlock Source(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), "Untrusted local file plain text content", StringComparison.Ordinal));
    private static ComboBox Files(Window window) => window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
        AutomationProperties.GetName(box)?.StartsWith("Inspect each", StringComparison.Ordinal) == true);
    private static Task NoAction() => throw new InvalidOperationException("Passive measurement must not invoke an action.");
    private static Task<LocalFileSearchResult> NoSearch(string query, LocalFileRetrievalMode mode, CancellationToken token) =>
        throw new InvalidOperationException("Passive measurement must not invoke Find/retrieval.");

    private sealed class ObservedCalls : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new(state));
        }
    }

    private static LocalFolderRevision Folder(params string[] texts)
    {
        var source = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var bytes = texts.Select(text => (byte[])[0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)]).ToArray();
        var metadata = bytes.Select((item, index) => new LocalFileMetadata($@"C:\Synthetic\{index}.txt", $"file-{index}",
            item.Length, DateTimeOffset.UnixEpoch)).ToArray();
        var review = new LocalFolderReview(reviewId, source, request, new(@"C:\Synthetic", "folder", metadata));
        return new(review, bytes.Select((item, index) => new LocalFileRevision(new(reviewId, source, request, metadata[index]),
            item, DateTimeOffset.UnixEpoch)));
    }
}
