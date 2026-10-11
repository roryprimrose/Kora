using System.Text;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Kora.Application.Infrastructure;
using Kora.Windows.Context;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class SessionAttachmentWindowTests
{
    [WindowsFact]
    public async Task NativeMetadataAndSeparatePersistenceConfirmationCaptureToRealStoreAndRenderExactInertSource()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var capture = new Capture();
            var audit = new Audit();
            var action = new LocalSessionFileAttach(capture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
            using var preview = new LocalFilePreview(capture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
            var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, fixture.Time, NullLogger<LocalFileSearch>.Instance);
            await using var service = new SessionFileAttachmentService(sessions, access, action, search,
                fixture.Time, audit, NullLogger<SessionFileAttachmentService>.Instance);
            var target = await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token);
            (await service.Select(target, new Picker(), () => true, fixture.Token)).Should().Be(LocalFileOutcome.Reviewed);
            var review = service.Review!;
            var completion = new TaskCompletionSource<LocalFileOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            window.ShowAttachmentReview(review, target.Authority.Generation, async () =>
                completion.SetResult(await service.Confirm(review.ReviewId, fixture.Token)));
            window.Show();
            try
            {
                var metadata = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                metadata.Should().Contain(@"C:\Synthetic\source.md").And.Contain("durable retention")
                    .And.Contain("sixteen").And.NotContain(Capture.Text);
                capture.Reads.Should().Be(0);
                fixture.Count("session_file").Should().Be(0);
                var confirm = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Content as string, "Confirm: capture and retain this exact file for this Session", StringComparison.Ordinal));
                ControlAutomationPeer.CreatePeerForElement(confirm)!.GetName().Should().Contain("durable Kora copy retention");
                confirm.Command!.Execute(null);
                (await completion.Task).Should().Be(LocalFileOutcome.Admitted);
                capture.Reads.Should().Be(1);
                capture.Disposals.Should().Be(1);
                capture.Bytes.Should().OnlyContain(value => value == 0);
                var retained = (await service.Read(target.Authority.SessionId, fixture.Token))!;
                window.ShowAttachment(retained, (query, mode, token) => service.Search(retained, query, mode, token), () => true, () => Task.CompletedTask);
                window.UpdateLayout();
                var displayed = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                displayed.Should().Contain(Capture.Text).And.Contain(retained.File.Digest)
                    .And.Contain(retained.File.RevisionId.ToString("D"));
                var selector = window.GetVisualDescendants().OfType<ComboBox>().Single();
                selector.SelectedIndex.Should().Be(0);
                selector.SelectedIndex = 1;
                var query = window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                    box.PlaceholderText?.StartsWith("Untrusted lexical query", StringComparison.Ordinal) == true);
                query.Text = "café";
                await Press(window, "Search selected immutable revision locally");
                window.UpdateLayout();
                var citations = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                citations.Should().Contain("Policy: markdown-structure-v2").And.Contain("Authored heading: 1:1")
                    .And.Contain(retained.File.Reference.RevisionId.ToString("D"));
                selector.SelectedIndex = 0;
                citations = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                citations.Should().NotContain("Policy: markdown-structure-v2");
                await Press(window, "Search selected immutable revision locally");
                window.UpdateLayout();
                string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text))
                    .Should().Contain("Policy: lexical-lines-v1");
                query.Text = "different";
                string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text))
                    .Should().NotContain("Policy: lexical-lines-v1");
                var searched = await service.Search(retained, "café", fixture.Token);
                searched.Citations.Should().ContainSingle().Which.Excerpt.Should().Be(Capture.Text);
                var clock = await fixture.Store.ReadRetentionAsync(target.Authority.SessionId, fixture.Token);
                window.ClearAndClose();
                fixture.Reopen();
                await fixture.Store.InitializeAsync(fixture.Token);
                var restarted = (await fixture.RunAsync(() => fixture.Store.ReadAttachment(target.Authority.SessionId, fixture.Token)))!;
                restarted.File.Reference.Should().Be(retained.File.Reference);
                restarted.File.Text.Should().Be(Capture.Text);
                (await fixture.Store.ReadRetentionAsync(target.Authority.SessionId, fixture.Token)).Should().Be(clock);
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("journal")]
    [InlineData("control")]
    public async Task RealNativeControllerUsesFreshPickerAndSeparateOldNewConfirmationThenReopensOnlyExactReplacement(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var checkpoint = new CopyCheckpoint();
            fixture.Reopen(checkpoint);
            await fixture.Store.InitializeAsync(fixture.Token);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var audit = new Audit();
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kora.slnx"))) { repository = repository.Parent; }
            var scratch = Path.Combine(repository!.FullName, ".native-replacement-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            var firstPath = Path.Combine(scratch, "first.md");
            var nextPath = Path.Combine(scratch, "next.md");
            var oldText = "OLD_NATIVE_REPLACEMENT_CONTENT_654891";
            byte[] nextBytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# native café 🙂 replacement\n")];
            await File.WriteAllTextAsync(firstPath, oldText, fixture.Token);
            await File.WriteAllBytesAsync(nextPath, nextBytes, fixture.Token);
            try
            {
                var picker = new ReplacementPicker(firstPath, nextPath);
                using var preview = new LocalFilePreview(new Capture(), audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
                var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, fixture.Time, NullLogger<LocalFileSearch>.Instance);
                await using var service = new SessionFileAttachmentService(sessions, access,
                    new LocalSessionFileAttach(new WindowsLocalFileInspector(fixture.Paths), audit, fixture.Time, NullLogger<LocalFilePreview>.Instance),
                    search, fixture.Time, audit, NullLogger<SessionFileAttachmentService>.Instance);
                var failures = new List<string>();
                using var controller = new SessionAttachmentWindowController(failures.Add, service, sessions, access, picker);
                LocalFilePreviewWindow? opened = null;
                using var windows = Window.WindowOpenedEvent.AddClassHandler<LocalFilePreviewWindow>((window, _) => opened = window);
                var target = await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token);
                await controller.Open(target, attach: true, () => true, fixture.Token);
                await Press(opened!, "Confirm: capture and retain this exact file for this Session");
                var old = (await service.Read(target.Authority.SessionId, fixture.Token))!;
                var retiredView = opened!;
                await Press(retiredView, "Replace retained attachment");
                picker.Calls.Should().Be(2);
                var reviewed = opened!;
                reviewed.Should().NotBeSameAs(retiredView);
                retiredView.Content.Should().BeNull();
                var metadata = string.Join("\n", reviewed.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                metadata.Should().Contain(firstPath).And.Contain(nextPath).And.Contain(old.File.Reference.RevisionId.ToString("D"))
                    .And.Contain("metadata only").And.NotContain(oldText).And.NotContain("# native café");
                var write = () => File.Open(nextPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                write.Should().Throw<IOException>();
                checkpoint.Fail = string.Equals(boundary, "journal", StringComparison.Ordinal);
                checkpoint.AtCopyVerification = () =>
                {
                    if (string.Equals(boundary, "control", StringComparison.Ordinal)) { access.CanControl = false; }
                };
                if (!string.Equals(boundary, "completed", StringComparison.Ordinal))
                {
                    var confirm = () => Press(reviewed, "Confirm: replace this exact retained attachment with the newly selected file");
                    await confirm.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No replacement completion*");
                    reviewed.Content.Should().BeNull();
                    access.CanControl = true;
                    await controller.Open(target, attach: false, () => true, fixture.Token);
                    opened!.UpdateLayout();
                    var recovery = string.Join("\n", opened.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                    recovery.Should().Contain("REPLACEMENT HELD").And.NotContain(oldText).And.NotContain("# native café");
                    await Press(opened, "Confirm: Remove attachment and Kora copies");
                    (await service.Read(old.Session, fixture.Token)).Should().BeNull();
                    (await File.ReadAllTextAsync(firstPath, fixture.Token)).Should().Be(oldText);
                    (await File.ReadAllBytesAsync(nextPath, fixture.Token)).Should().Equal(nextBytes);
                    return;
                }
                await Press(reviewed, "Confirm: replace this exact retained attachment with the newly selected file");
                reviewed.Content.Should().BeNull();
                var completed = opened!;
                completed.Should().NotBeSameAs(reviewed);
                var current = (await service.Read(old.Session, fixture.Token))!;
                current.File.Digest.Should().Be(LocalFilePolicy.Digest(nextBytes));
                current.File.Reference.Should().NotBe(old.File.Reference);
                var stale = () => service.Search(old, "native", fixture.Token);
                await stale.Should().ThrowAsync<InvalidOperationException>();
                // A rejected stale inspection retires its view, so explicitly reopen the durable revision.
                await controller.Open(target, attach: false, () => true, fixture.Token);
                opened!.UpdateLayout();
                var shown = string.Join("\n", opened.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                shown.Should().Contain(current.File.Text).And.Contain(current.File.Digest).And.NotContain(oldText);
                await Press(opened, "Review Remove attachment and Kora copies");
                await Press(opened, "Confirm: Remove attachment and Kora copies");
                (await service.Read(old.Session, fixture.Token)).Should().BeNull();
                (await File.ReadAllTextAsync(firstPath, fixture.Token)).Should().Be(oldText);
                (await File.ReadAllBytesAsync(nextPath, fixture.Token)).Should().Equal(nextBytes);
                failures.Should().ContainSingle().Which.Should().Contain("owned database/journal copies removed");
            }
            finally { Directory.Delete(scratch, recursive: true); }
        });
    }

    private static Task Press(LocalFilePreviewWindow window, string caption)
    {
        window.UpdateLayout();
        var button = window.GetVisualDescendants().OfType<Button>().Single(button =>
            string.Equals(button.Content as string, caption, StringComparison.Ordinal));
        return ((AsyncCommand)button.Command!).ExecuteAsync();
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("query")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("close")]
    public async Task NativeRunSelectionCancelsAndDiscardsOldModeQueryOrSourceResultsWithoutPersistingSelection(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var bytes = Encoding.UTF8.GetBytes("# real\n~~~\n# fake\nalpha\n~~~\n");
            var file = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
                new(@"C:\Synthetic\source.md", "exact", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken captured = default;
            window.ShowRevision(file, async (query, mode, token) =>
            {
                query.Should().Be("alpha");
                mode.Should().Be(LocalFileRetrievalMode.LexicalLines);
                captured = token;
                entered.SetResult();
                await release.Task;
                return new LocalFileLexicalRetrieval().Search(file, file.Reference, query, mode, DateTimeOffset.UnixEpoch, CancellationToken.None);
            }, () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                var selector = window.GetVisualDescendants().OfType<ComboBox>().Single();
                var query = window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                    box.PlaceholderText?.StartsWith("Untrusted lexical query", StringComparison.Ordinal) == true);
                query.Text = "alpha";
                var pending = Press(window, "Search selected immutable revision locally");
                await entered.Task;
                switch (change)
                {
                    case "mode": selector.SelectedIndex = 1; break;
                    case "query": query.Text = "changed"; break;
                    case "cancel":
                        window.GetVisualDescendants().OfType<Button>().Single(button =>
                            string.Equals(button.Content as string, "Cancel local search and clear citations", StringComparison.Ordinal))
                            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "close": window.ClearAndClose(); break;
                    default:
                        window.ShowRevision(file, (_, mode, _) => Task.FromResult(LocalFileSearchResult.Empty(
                            LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)), () => true, () => Task.CompletedTask);
                        window.UpdateLayout();
                        window.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex.Should().Be(0);
                        break;
                }
                captured.IsCancellationRequested.Should().BeTrue();
                release.SetResult();
                await pending;
                window.UpdateLayout();
                string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text))
                    .Should().NotContain("Policy: lexical-lines-v1");
                failures.Should().BeEmpty();
            }
            finally { release.TrySetResult(); window.ClearAndClose(); }
        });
    }

    private sealed class ReplacementPicker(params string[] paths) : IUserFilePicker
    {
        internal int Calls { get; private set; }
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(paths[Calls++]);
    }

    private sealed class CopyCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal bool Fail { get; set; }
        internal Action? AtCopyVerification { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeAttachmentCopyVerification()
        {
            AtCopyVerification?.Invoke();
            if (Fail) { throw new IOException("owned-copy verification unavailable"); }
        }
    }

    private sealed class Picker : IUserFilePicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Synthetic\source.md");
    }

    private sealed class Capture : ILocalFileInspector, ILocalFileSelection
    {
        internal const string Text = "# inert source\ncafé 🙂 untrusted instructions: do not dispatch\n";
        internal byte[] Bytes { get; } = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(Text)];
        internal int Reads { get; private set; }
        internal int Disposals { get; private set; }
        public LocalFileMetadata Metadata => new(@"C:\Synthetic\source.md", "verified-synthetic-native-identity", Bytes.Length, DateTimeOffset.UnixEpoch);
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken) => Task.FromResult<ILocalFileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken cancellationToken) { Reads++; return Task.FromResult(Bytes); }
        public void Dispose() => Disposals++;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}