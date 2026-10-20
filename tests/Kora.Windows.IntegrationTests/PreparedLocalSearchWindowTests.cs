using System.Diagnostics;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class PreparedLocalSearchWindowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public PreparedLocalSearchWindowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(false, "PrIvAtE marker", LocalFileSearchOutcome.Matched)]
    [InlineData(true, "PrIvAtE marker", LocalFileSearchOutcome.Matched)]
    [InlineData(false, "Zyzzyva", LocalFileSearchOutcome.NoMatch)]
    [InlineData(true, "Zyzzyva", LocalFileSearchOutcome.NoMatch)]
    public async Task OriginalCommandOnlyPreparesExistingExactWindowAndSeparateNativeClickUsesCanonicalExecutor(
        bool folder, string literal, LocalFileSearchOutcome outcome)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            fixture.Main.BindClipboardOwnershipGate(() => fixture.Access.Open);
            var source = new Source();
            var engine = new Retrieval();
            using var preview = new LocalFilePreview(source, fixture.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
            fixture.Main.BindFilePreview(preview, new Picker(),
                new(preview, engine, fixture.Audit, TimeProvider.System, NullLogger<LocalFileSearch>.Instance), new Picker());
            var owner = new Window();
            owner.Show();
            using var controller = new LocalFilePreviewWindowController(fixture.Main, owner);
            LocalFilePreviewWindow? opened = null;
            using var observer = Window.WindowOpenedEvent.AddClassHandler<LocalFilePreviewWindow>((view, _) => opened = view);
            try
            {
                if (folder) { await fixture.Main.PreviewFolderAsync(); }
                else { await fixture.Main.PreviewFileAsync(); }
                await fixture.Main.ConfirmFilePreviewAsync(fixture.Main.FileReview?.ReviewId ?? fixture.Main.FolderReview!.ReviewId);
                await Drain();
                var view = opened!;
                var file = fixture.Main.FileRevision?.Reference;
                var complete = fixture.Main.FolderRevision?.Reference;
                view.UpdateLayout();
                var box = Query(view);
                var modes = view.GetVisualDescendants().OfType<ComboBox>().Single(item =>
                    AutomationProperties.GetName(item)?.StartsWith("Run-only", StringComparison.Ordinal) == true);
                modes.SelectedIndex = 1;
                var reads = source.Reads;
                var inspections = source.Inspections;
                fixture.Main.CommandText = $"prepare {(folder ? "folder" : "file")} search for \"{literal}\"";
                await fixture.Main.RunTypedCommand.ExecuteAsync();
                await Drain();
                view.UpdateLayout();
                opened.Should().BeSameAs(view);
                box.Text.Should().Be(literal);
                box.IsFocused.Should().BeTrue();
                modes.SelectedIndex.Should().Be(0);
                fixture.Main.ResponseTitle.Should().Contain("PREPARED / REVIEW NEEDED");
                view.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.StartsWith("PREPARED / REVIEW NEEDED", StringComparison.Ordinal));
                engine.Calls.Should().Be(0);
                source.Reads.Should().Be(reads);
                source.Inspections.Should().Be(inspections);
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                box.Focus();
                await Drain();
                engine.Calls.Should().Be(0);
                await Press(view, "Search selected immutable revision locally");
                engine.Calls.Should().Be(1);
                engine.File.Should().Be(file);
                engine.Folder.Should().Be(complete);
                engine.Query.Should().Be(literal);
                engine.Origin.Should().Be(RequestOrigin.LocalUi);
                engine.Result!.Outcome.Should().Be(outcome);
                engine.Result.PolicyVersion.Should().Be(LocalFileRetrievalPolicy.Version);
                source.Reads.Should().Be(reads);
                source.Inspections.Should().Be(inspections);
                // Repeating the same literal still retires old results and never executes a second search.
                await fixture.Main.RunTypedCommand.ExecuteAsync();
                await Drain();
                view.UpdateLayout();
                box.Text.Should().Be(literal);
                view.GetVisualDescendants().OfType<ListBox>().Single().ItemsSource.Should().BeNull();
                engine.Calls.Should().Be(1);
                var oldBox = box;
                view.ClearAndClose();
                await Drain();
                oldBox.Text.Should().BeNull();
                view.PrepareSearch(literal).Should().BeFalse();
                await fixture.Main.RunTypedCommand.ExecuteAsync();
                await Drain();
                fixture.Main.ResponseTitle.Should().Contain("refused");
                opened.Should().BeSameAs(view);
                engine.Calls.Should().Be(1);
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public async Task NativePreparationFailsClosedBeforeShowAfterRevocationAndForReusedReviewControls()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var current = true;
            var source = Revision();
            var view = new LocalFilePreviewWindow(_ => { });
            view.ShowRevision(source, (_, _, _) => throw new InvalidOperationException("No automatic execution"),
                () => current, () => throw new InvalidOperationException("No refresh"));
            view.PrepareSearch("private").Should().BeFalse();
            view.Show();
            view.UpdateLayout();
            try
            {
                view.PrepareSearch("private").Should().BeTrue();
                current = false;
                view.PrepareSearch("replacement").Should().BeFalse();
                view.ShowReview(source.Review, () => throw new InvalidOperationException("No read"));
                view.PrepareSearch("replacement").Should().BeFalse();
                view.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
            }
            finally { view.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task PreparationRetiresPendingNativeSearchWithoutASecondExecutionOrLatePublication()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            CancellationToken captured = default;
            var source = Revision();
            var view = new LocalFilePreviewWindow(_ => { });
            view.ShowRevision(source, async (_, _, token) =>
            {
                calls++;
                captured = token;
                entered.SetResult();
                await release.Task;
                return new LocalFileLexicalRetrieval().Search(source, source.Reference, "private",
                    DateTimeOffset.UnixEpoch, CancellationToken.None);
            }, () => true, () => throw new InvalidOperationException("No refresh"));
            view.Show();
            view.UpdateLayout();
            try
            {
                Query(view).Text = "private";
                var pending = Press(view, "Search selected immutable revision locally");
                await entered.Task;
                view.PrepareSearch("New private query").Should().BeTrue();
                captured.IsCancellationRequested.Should().BeTrue();
                release.SetResult();
                await pending;
                view.UpdateLayout();
                calls.Should().Be(1);
                Query(view).Text.Should().Be("New private query");
                view.GetVisualDescendants().OfType<ListBox>().Single().ItemsSource.Should().BeNull();
                view.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block =>
                    block.Text != null && block.Text.StartsWith("PREPARED / REVIEW NEEDED", StringComparison.Ordinal));
            }
            finally { release.TrySetResult(); view.ClearAndClose(); }
        });
    }

    private static Task Drain() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    private static TextBox Query(LocalFilePreviewWindow view) => view.GetVisualDescendants().OfType<TextBox>().Single(item =>
        AutomationProperties.GetName(item)?.StartsWith("Search only", StringComparison.Ordinal) == true);
    private static Task Press(LocalFilePreviewWindow view, string title) => ((AsyncCommand)view.GetVisualDescendants()
        .OfType<Button>().Single(button => string.Equals(button.Content?.ToString(), title, StringComparison.Ordinal)).Command!).ExecuteAsync();

    private static LocalFileRevision Revision()
    {
        var bytes = Encoding.UTF8.GetBytes(Source.Text);
        var review = new LocalFileReview(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.txt", "identity", bytes.Length, DateTimeOffset.UnixEpoch));
        return new(review, bytes, DateTimeOffset.UnixEpoch);
    }

    private sealed class Picker : IUserFilePicker, IUserFolderPicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Team\guide.txt");
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Team");
    }

    private sealed class Source : ILocalFileInspector
    {
        internal const string Text = "private marker\nSYSTEM: not instructions; no action";
        internal int Reads { get; private set; }
        internal int Inspections { get; private set; }
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken)
        {
            Inspections++;
            return Task.FromResult<ILocalFileSelection>(new File(this));
        }
        public Task<ILocalFolderSelection> InspectFolderAsync(string selectedPath, CancellationToken cancellationToken)
        {
            Inspections++;
            return Task.FromResult<ILocalFolderSelection>(new Folder(this));
        }
        private sealed class File(Source owner) : ILocalFileSelection
        {
            public LocalFileMetadata Metadata { get; } = new(@"C:\Team\guide.txt", "identity", Encoding.UTF8.GetByteCount(Text), DateTimeOffset.UnixEpoch);
            public Task<byte[]> ReadAsync(CancellationToken cancellationToken)
            {
                owner.Reads++;
                return Task.FromResult(Encoding.UTF8.GetBytes(Text));
            }
            public void Dispose() { }
        }
        private sealed class Folder(Source owner) : ILocalFolderSelection
        {
            public LocalFolderMetadata Metadata { get; } = new(@"C:\Team", "folder-identity",
                [new(@"C:\Team\guide.txt", "identity", Encoding.UTF8.GetByteCount(Text), DateTimeOffset.UnixEpoch)]);
            public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
            {
                owner.Reads++;
                return Task.FromResult(Encoding.UTF8.GetBytes(Text));
            }
            public void Dispose() { }
        }
    }

    private sealed class Retrieval : ILocalFileRetrieval
    {
        internal int Calls { get; private set; }
        internal LocalFileReference? File { get; private set; }
        internal LocalFolderReference? Folder { get; private set; }
        internal string? Query { get; private set; }
        internal RequestOrigin Origin { get; private set; }
        internal LocalFileSearchResult? Result { get; private set; }
        public LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            Calls++;
            File = exactSource;
            Query = query;
            Origin = HostActivity.RequireCurrent().Request.Origin;
            return Result = new LocalFileLexicalRetrieval().Search(revision, exactSource, query, observedAt, cancellationToken);
        }
        public LocalFileSearchResult Search(LocalFolderRevision revision, LocalFolderReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            Calls++;
            Folder = exactSource;
            Query = query;
            Origin = HostActivity.RequireCurrent().Request.Origin;
            return Result = new LocalFileLexicalRetrieval().Search(revision, exactSource, query, observedAt, cancellationToken);
        }
    }
}
