using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class EvidencePageExportControllerTests
{
    [Fact]
    public async Task VoluntaryShutdownJoinsAnAlreadyClosedEvidenceWindowPhysicalWriter()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var fixture = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await fixture.InitializeAsync();
            var writer = new Writer();
            writer.Destination.Hold = true;
            var query = new DurableEvidenceQuery(new Reader(), fixture.Access, TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
            var model = new EvidenceViewModel(query, () => true, NullLogger<EvidenceViewModel>.Instance)
            { Source = EvidenceSource.Log };
            using var controller = new EvidenceWindowController(fixture.Main, query, fixture.Access,
                NullLogger<EvidenceViewModel>.Instance, fixture.Access, exports: Controller(writer, new Audit()));
            controller.BindExports(model);
            await model.SearchAsync();
            model.CanExportPage.Should().BeTrue();
            var owner = new EvidenceWindow(model);
            owner.Show();
            try
            {
                var operation = model.ExportPageAsync(owner);
                var view = owner.OwnedWindows.OfType<EvidencePageExportWindow>().Single();
                FindButton(view, "ConfirmEvidencePageExport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await writer.Destination.Started.Task;
                owner.Close();
                view.Close();
                model.ResultText.Should().BeEmpty();
                var shutdown = controller.PrepareShutdownAsync();
                shutdown.IsCompleted.Should().BeFalse();
                writer.Destination.Disposals.Should().Be(0);
                writer.Destination.Release.SetResult();
                await shutdown;
                await operation;
                controller.CaptureShutdownEligibility()().Should().BeTrue();
                writer.Destination.Disposals.Should().Be(1);
                model.CanExportPage.Should().BeFalse();
            }
            finally
            {
                writer.Destination.Release.TrySetResult();
                owner.Close();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReviewShowsAllBytesAndRequiresIndependentConfirmation(bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Storage);
            var source = Source();
            var writer = new Writer();
            var audit = new Audit();
            var controller = Controller(writer, audit);
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = controller.ShowAsync(owner, source, () => true, storage.Token);
                var view = owner.OwnedWindows.OfType<EvidencePageExportWindow>().Single();
                view.Width = 480; view.Height = 400; view.FontSize = 24; view.UpdateLayout();
                var text = view.GetVisualDescendants().OfType<TextBox>().Single();
                text.Text.Should().Be(source.Snapshot.Text);
                view.GetVisualDescendants().OfType<TextBlock>().Should().Contain(label =>
                    label.Text != null && label.Text.Contains(EvidencePageExportSnapshot.ExclusionDisclosure, StringComparison.Ordinal));
                writer.Destination.Calls.Should().Be(0);
                audit.Events.Should().BeEmpty();
                if (!cancel)
                {
                    var confirm = FindButton(view, "ConfirmEvidencePageExport");
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await view.Operation;
                    view.Outcome.Should().Be(EvidencePageExportOutcome.Saved);
                    confirm.IsEnabled.Should().BeFalse();
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    writer.Destination.Calls.Should().Be(1);
                    text.Text.Should().BeEmpty();
                }
                view.Close();
                (await operation).Message.Should().Contain(cancel ? "cancelled" : "verified");
                text.Text.Should().BeEmpty();
                writer.Destination.Disposals.Should().Be(1);
                audit.Events.Count.Should().Be(cancel ? 0 : 2);
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public async Task RetirementClearsAndCancelsButJoinsCancellationIgnoringPhysicalWriter()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Storage);
            using var cancellation = new CancellationTokenSource();
            var writer = new Writer();
            writer.Destination.Hold = true;
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = Controller(writer, new Audit()).ShowAsync(owner, Source(), () => true, cancellation.Token);
                var view = owner.OwnedWindows.OfType<EvidencePageExportWindow>().Single();
                FindButton(view, "ConfirmEvidencePageExport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await writer.Destination.Started.Task;
                cancellation.Cancel();
                view.Close();
                view.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().BeEmpty();
                operation.IsCompleted.Should().BeFalse("cancellation is not physical resource retirement");
                writer.Destination.Disposals.Should().Be(0);
                writer.Destination.Release.SetResult();
                (await operation).Message.Should().Contain("cancelled");
                writer.Destination.Disposals.Should().Be(1);
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public async Task PickerSourceDriftDeniesDisclosureAndDisposesDestination()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Storage);
            var current = true;
            var source = new EvidencePageExportSource(Source().Snapshot, _ => current
                ? Task.CompletedTask : Task.FromException(new InvalidOperationException("source drift")));
            var writer = new Writer();
            var controller = new EvidencePageExportWindowController(writer, new Audit(), NullLogger<EvidencePageExportReview>.Instance,
                (_, _) => { current = false; return Task.FromResult<string?>(writer.Destination.Path); });
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = () => controller.ShowAsync(owner, source, () => true, storage.Token);
                await operation.Should().ThrowAsync<InvalidOperationException>();
                owner.OwnedWindows.Should().BeEmpty();
                writer.Destination.Calls.Should().Be(0);
                writer.Destination.Disposals.Should().Be(1);
            }
            finally { owner.Close(); }
        });
    }

    private static Button FindButton(Window view, string name) =>
        view.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Name, name, StringComparison.Ordinal));
    private static EvidencePageExportSource Source() => new(new(new(EvidencePageStatus.Partial, [], "excluded-live-cursor",
        [EvidenceSource.Session, EvidenceSource.Conversation], EvidencePage.StorageDisclosure)), _ => Task.CompletedTask);
    private static EvidencePageExportWindowController Controller(Writer writer, Audit audit) =>
        new(writer, audit, NullLogger<EvidencePageExportReview>.Instance, (_, _) => Task.FromResult<string?>(writer.Destination.Path));
    private sealed class Writer : IEvidencePageExportWriter
    {
        internal Destination Destination { get; } = new();
        public Task<IEvidencePageExportDestination> SelectAsync(string path, CancellationToken token) =>
            Task.FromResult<IEvidencePageExportDestination>(Destination);
    }
    private sealed class Destination : IEvidencePageExportDestination
    {
        public string Path => "Q:\\synthetic\\evidence.json";
        internal bool Hold { get; set; }
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<EvidencePageExportOutcome> PublishAsync(EvidencePageExportSnapshot source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            await revalidate(token);
            Started.SetResult();
            if (Hold)
            {
                await Release.Task;
                token.IsCancellationRequested.Should().BeTrue();
                return EvidencePageExportOutcome.Cancelled;
            }
            return EvidencePageExportOutcome.Saved;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
    private sealed class Reader : IEvidenceReader
    {
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new EvidenceReadBatch(new(0, 0, 0, 0), [], null, false, false));
    }
}
