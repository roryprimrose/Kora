using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Tools.Files;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionAttachmentWindowTests
{
    [Theory]
    [InlineData("system")]
    [InlineData("invocation")]
    [InlineData("foreign")]
    [InlineData("completed")]
    [InlineData("private")]
    [InlineData("cancelled")]
    [InlineData("missing")]
    public async Task ExactMetadataInspectionEnforcesOriginalSessionAuthorityAndNeverReadsAnOriginalFile(string boundary)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var origin = boundary is "system" ? RequestOrigin.HostSystem : RequestOrigin.ActivatedVoice;
        var request = new HostRequest(new(Guid.NewGuid()),
            boundary is "foreign" ? new(Guid.NewGuid()) : fixture.Request.SessionId, new(Guid.NewGuid()), origin,
            boundary is "invocation" ? new(Guid.NewGuid()) : null);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (boundary is "completed") { activity.Complete(HostOperationOutcome.Completed); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        if (boundary is "cancelled") { cancellation.Cancel(); }
        var read = async () => await fixture.Store.ReadAttachmentInspection(fixture.Request.SessionId,
            () => boundary is not "private", cancellation.Token);
        if (boundary is "missing") { (await read()).Should().BeNull(); }
        else { await read.Should().ThrowAsync<Exception>(); }
    }

    [Theory]
    [InlineData(false, "open")]
    [InlineData(true, "open")]
    [InlineData(false, "pending")]
    [InlineData(false, "cancelled")]
    [InlineData(false, "late-input")]
    [InlineData(false, "late-cancel")]
    [InlineData(false, "opening-cancel")]
    [InlineData(false, "opening-privacy")]
    public async Task ExactCommandUsesSameInertNativeControllerWithoutRecaptureOrDiscardingReview(bool done, string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var checkpoint = new InspectionCheckpoint();
            fixture.Reopen(checkpoint);
            await fixture.Store.InitializeAsync(fixture.Token);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var audit = new Audit();
            var capture = new Capture();
            using var preview = new LocalFilePreview(capture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
            var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, fixture.Time, NullLogger<LocalFileSearch>.Instance);
            await using var files = new SessionFileAttachmentService(sessions, access,
                new LocalSessionFileAttach(capture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance),
                search, fixture.Time, audit, NullLogger<SessionFileAttachmentService>.Instance);
            using var controller = new SessionAttachmentWindowController(_ => { }, files, sessions, access, new Picker());
            LocalFilePreviewWindow? opened = null;
            using var windows = Window.WindowOpenedEvent.AddClassHandler<LocalFilePreviewWindow>((window, _) => opened = window);
            var target = await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token);
            await controller.Open(target, attach: true, () => true, fixture.Token);
            await Press(opened!, "Confirm: capture and retain this exact file for this Session");
            controller.Close();
            if (done)
            {
                await sessions.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation, false,
                    RequestOrigin.LocalUi, fixture.Token);
            }
            if (boundary is "pending")
            {
                var other = await sessions.CreateAsync(new("Other"), RequestOrigin.LocalUi, fixture.Token);
                await controller.Open(other, attach: true, () => true, fixture.Token);
            }
            var previous = opened!;
            var reads = capture.Reads;
            var generation = (await fixture.Store.ReadMetadataAsync(target.Authority.SessionId, fixture.Token)).Authority.Generation;
            var taskCount = fixture.Count("host_tasks");
            var admitted = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
            if (boundary is "cancelled") { cancellation.Cancel(); }
            var viewer = new CommandViewer(controller, () =>
            {
                if (boundary is "late-input") { admitted = false; }
                if (boundary is "late-cancel") { cancellation.Cancel(); }
            });
            var metadataReads = 0;
            checkpoint.MetadataRead = () =>
            {
                if (++metadataReads != 4) { return; }
                if (boundary is "opening-cancel") { cancellation.Cancel(); }
                if (boundary is "opening-privacy") { access.CanInspect = false; }
            };
            var service = new SessionAttachmentCommandService(sessions, files, access, viewer);
            var run = () => service.Execute(new(SessionCommandOperation.AttachmentInspect, target.Authority.SessionId.Value),
                RequestOrigin.ActivatedVoice, () => admitted, cancellation.Token);
            if (boundary is not "open")
            {
                await run.Should().ThrowAsync<Exception>();
                if (boundary is "pending")
                {
                    opened.Should().BeSameAs(previous);
                    files.Review.Should().NotBeNull();
                    previous.IsVisible.Should().BeTrue();
                }
                else { opened!.IsVisible.Should().BeFalse(); }
            }
            else
            {
                await run();
                opened.Should().NotBeSameAs(previous);
                var current = opened!;
                current.IsVisible.Should().BeTrue();
                current.UpdateLayout();
                string.Join("\n", current.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text))
                    .Should().Contain(Capture.Text);
                current.GetVisualDescendants().OfType<Button>().Select(button => button.Content)
                    .Should().NotContain("Replace retained attachment").And.NotContain("Review Remove attachment and Kora copies");
                cancellation.Cancel();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                current.Content.Should().BeNull();
                current.IsVisible.Should().BeFalse();
            }
            capture.Reads.Should().Be(reads);
            fixture.Count("host_tasks").Should().Be(taskCount);
            var after = await fixture.Store.ReadMetadataAsync(target.Authority.SessionId, fixture.Token);
            after.Authority.Generation.Should().Be(generation);
            after.Authority.IsActive.Should().Be(!done);
        });
    }

    private sealed class InspectionCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal Action? MetadataRead { get; set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeAttachmentMetadataRead(SqliteCommand command) => MetadataRead?.Invoke();
    }

    private sealed class CommandViewer(SessionAttachmentWindowController controller, Action before) : IRetainedAttachmentViewer
    {
        public Task Open(SessionWorkspaceEntry target, SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
        {
            before();
            return controller.Open(target, exact, admitted, token);
        }
    }
}
