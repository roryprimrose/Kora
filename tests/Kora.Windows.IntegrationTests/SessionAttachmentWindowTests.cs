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
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

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
            await using var service = new SessionFileAttachmentService(sessions, access, action, new LocalFileLexicalRetrieval(),
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
                window.ShowAttachment(retained, query => service.Search(retained, query, fixture.Token), () => true, () => Task.CompletedTask);
                window.UpdateLayout();
                var displayed = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
                displayed.Should().Contain(Capture.Text).And.Contain(retained.File.Digest)
                    .And.Contain(retained.File.RevisionId.ToString("D"));
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
