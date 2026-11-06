using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Controls;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileReviewWindowTests
{
    [Theory]
    [InlineData(false, 480)]
    [InlineData(false, 800)]
    [InlineData(true, 480)]
    [InlineData(true, 800)]
    public async Task KeyboardReadsCompleteMetadataThenSeparatelyConfirmsExactBoundedCapture(bool folder, int width)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            var inspector = new Inspector();
            var audit = new Audit();
            using var broker = new LocalFilePreview(inspector, audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
            var token = TestContext.Current.CancellationToken;
            var selected = folder
                ? await broker.SelectFolderAsync(new Picker(), () => true, token)
                : await broker.SelectAsync(new Picker(), () => true, token);
            selected.Should().Be(LocalFileOutcome.Reviewed);
            var completion = new TaskCompletionSource<LocalFileOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add);
            if (folder)
            {
                var exact = broker.FolderReview!;
                window.ShowFolderReview(exact, async () =>
                    completion.SetResult(await broker.ConfirmFolderAsync(exact.ReviewId, () => true, token)));
            }
            else
            {
                var exact = broker.Review!;
                window.ShowReview(exact, async () =>
                    completion.SetResult(await broker.ConfirmAsync(exact.ReviewId, () => true, token)));
            }
            window.Width = width;
            window.Height = 480;
            window.Show();
            window.UpdateLayout();
            try
            {
                var identity = Identity(window);
                var privacy = Privacy(window);
                var viewport = identity.GetVisualAncestors().OfType<ScrollViewer>().First();
                var confirm = Confirm(window);
                var peer = ControlAutomationPeer.CreatePeerForElement(identity)!;
                var metadata = identity.Text;
                peer.GetName().Should().Contain(inspector.Metadata.Files[folder ? 31 : 0].CanonicalPath);
                peer.GetName().Should().NotContain(Inspector.Body);
                viewport.Focusable.Should().BeTrue();
                viewport.IsTabStop.Should().BeTrue();
                confirm.Focus().Should().BeTrue();
                PressKey(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
                viewport.IsFocused.Should().BeTrue("the complete review must precede separate confirmation in the Tab sequence");
                PressKey(window, Key.Home, PhysicalKey.Home);
                window.UpdateLayout();
                viewport.Extent.Height.Should().BeGreaterThan(viewport.Viewport.Height);
                viewport.Offset.Y.Should().Be(0);
                PressKey(window, Key.PageDown, PhysicalKey.PageDown);
                window.UpdateLayout();
                viewport.Offset.Y.Should().BeGreaterThan(0);
                PressKey(window, Key.End, PhysicalKey.End);
                window.UpdateLayout();
                viewport.Offset.Y.Should().BeApproximately(viewport.Extent.Height - viewport.Viewport.Height, 1);
                PressKey(window, Key.Space, PhysicalKey.Space);
                PressKey(window, Key.Enter, PhysicalKey.Enter);
                var location = viewport.TranslatePoint(default, window)!.Value;
                location.Y.Should().BeGreaterThanOrEqualTo(0);
                (location.Y + viewport.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                identity.Text.Should().Be(metadata);
                inspector.Reads.Should().Be(0);
                completion.Task.IsCompleted.Should().BeFalse();
                broker.Current.Should().BeNull();
                broker.CurrentFolder.Should().BeNull();
                window.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
                PressKey(window, Key.Tab, PhysicalKey.Tab);
                confirm.IsFocused.Should().BeTrue();
                PressKey(window, Key.Space, PhysicalKey.Space);
                (await completion.Task).Should().Be(LocalFileOutcome.Admitted);
                inspector.Reads.Should().Be(folder ? 32 : 1);
                inspector.Disposals.Should().Be(1);
                inspector.Buffers.SelectMany(bytes => bytes).Should().OnlyContain(value => value == 0);
                if (folder)
                {
                    broker.CurrentFolder!.Files.Should().HaveCount(32)
                        .And.OnlyContain(file => string.Equals(file.Text, Inspector.Body, StringComparison.Ordinal));
                }
                else { broker.Current!.Text.Should().Be(Inspector.Body); }
                broker.IsQuiescent.Should().BeTrue();
                audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
                    SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                string.Join(" ", audit.Events.Select(item => item.ActionId + item.TargetId + item.ReasonCode))
                    .Should().NotContain(Inspector.Body).And.NotContain(@"C:\Team");
                window.ClearAndClose();
                identity.Text.Should().BeNull();
                privacy.Text.Should().BeNull();
                peer.GetName().Should().NotContain(inspector.Metadata.CanonicalPath);
                confirm.IsEnabled.Should().BeFalse();
                confirm.Command.Should().BeNull();
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("attachment")]
    [InlineData("replacement")]
    [InlineData("removal")]
    public async Task RebindingAndClosingRetireOnlyOriginalPrivateReviewWithoutConfirmingOrReading(string surface)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var inspector = new Inspector();
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            var review = new LocalFolderReview(Guid.NewGuid(), Guid.NewGuid(), request, inspector.Metadata);
            var fileReview = new LocalFileReview(Guid.NewGuid(), Guid.NewGuid(), request, inspector.Metadata.Files[0]);
            var file = new LocalFileRevision(fileReview, Encoding.UTF8.GetBytes(Inspector.Body), DateTimeOffset.UnixEpoch);
            var removal = new SessionFileRemoval(Guid.NewGuid(), request.SessionId, new(1), new(1), file.Reference, "inventory");
            var calls = 0;
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("No failure expected."));
            Task ConfirmReview() { calls++; return Task.CompletedTask; }
            void Show()
            {
                switch (surface)
                {
                    case "file": window.ShowReview(fileReview, ConfirmReview); break;
                    case "folder": window.ShowFolderReview(review, ConfirmReview); break;
                    case "attachment": window.ShowAttachmentReview(fileReview, new(1), ConfirmReview); break;
                    case "replacement":
                        window.ShowAttachmentReplacement(new(removal, fileReview.Metadata, DateTimeOffset.UnixEpoch, fileReview),
                            ConfirmReview);
                        break;
                    case "removal": window.ShowAttachmentRemoval(removal, ConfirmReview); break;
                }
            }
            Show();
            window.Show();
            window.UpdateLayout();
            try
            {
                var oldIdentity = Identity(window);
                var oldPrivacy = Privacy(window);
                var peer = ControlAutomationPeer.CreatePeerForElement(oldIdentity)!;
                var oldConfirm = Confirm(window);
                Show();
                window.UpdateLayout();
                var next = Identity(window);
                oldIdentity.Text.Should().BeNull();
                oldPrivacy.Text.Should().BeNull();
                peer.GetName().Should().NotContain(inspector.Metadata.CanonicalPath);
                oldConfirm.IsEnabled.Should().BeFalse();
                oldConfirm.Command.Should().BeNull();
                next.Text.Should().NotBeNullOrEmpty().And.NotContain(Inspector.Body);
                window.Close();
                next.Text.Should().BeNull();
                calls.Should().Be(0);
                inspector.Reads.Should().Be(0);
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static NamedTextBlock Identity(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(block => string.Equals(AutomationProperties.GetName(block), "Local file review identity and bounds", StringComparison.Ordinal));

    private static NamedTextBlock Privacy(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(block => string.Equals(AutomationProperties.GetName(block), "Local file preview privacy and unavailable capabilities", StringComparison.Ordinal));

    private static Button Confirm(Window window) => window.GetVisualDescendants().OfType<Button>()
        .Single(button => button.Content is string text && text.StartsWith("Confirm:", StringComparison.Ordinal));

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private sealed class Picker : IUserFilePicker, IUserFolderPicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(Inspector.FilePath(0));
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Team\Guides");
    }

    private sealed class Inspector : ILocalFileInspector
    {
        internal const string Body = "private inert source body";
        internal static string FilePath(int index) => $@"C:\Team\Guides\{index:D2}-{new string('a', 160)}.md";
        internal LocalFolderMetadata Metadata { get; } = new(@"C:\Team\Guides", "directory",
            Enumerable.Range(0, 32).Select(index => new LocalFileMetadata(FilePath(index),
                "identity-" + index, Encoding.UTF8.GetByteCount(Body), DateTimeOffset.UnixEpoch)));
        internal int Reads { get; set; }
        internal int Disposals { get; set; }
        internal List<byte[]> Buffers { get; } = [];
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken) =>
            Task.FromResult<ILocalFileSelection>(new FileSelection(this));
        public Task<ILocalFolderSelection> InspectFolderAsync(string selectedPath, CancellationToken cancellationToken) =>
            Task.FromResult<ILocalFolderSelection>(new FolderSelection(this));
        internal Task<byte[]> Read()
        {
            Reads++;
            var bytes = Encoding.UTF8.GetBytes(Body);
            Buffers.Add(bytes);
            return Task.FromResult(bytes);
        }
    }

    private sealed class FileSelection(Inspector inspector) : ILocalFileSelection
    {
        public LocalFileMetadata Metadata => inspector.Metadata.Files[0];
        public Task<byte[]> ReadAsync(CancellationToken cancellationToken) => inspector.Read();
        public void Dispose() => inspector.Disposals++;
    }

    private sealed class FolderSelection(Inspector inspector) : ILocalFolderSelection
    {
        public LocalFolderMetadata Metadata => inspector.Metadata;
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
        {
            Metadata.Files.Should().Contain(exactItem);
            return inspector.Read();
        }
        public void Dispose() => inspector.Disposals++;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
