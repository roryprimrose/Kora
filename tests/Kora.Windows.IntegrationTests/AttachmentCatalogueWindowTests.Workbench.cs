using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class AttachmentCatalogueWindowTests
{
    [Theory]
    [InlineData(1200, 740, "leave")]
    [InlineData(820, 600, "minimize")]
    [InlineData(320, 300, "hide")]
    [InlineData(820, 600, "read-hide")]
    public async Task HostedCatalogueKeepsMetadataReadsExplicitAndRetiresPageFindAndLateRead(int width, int height, string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            var shell = responses.PrepareWorkbenchOwner();
            var summary = Summary(storage.Request);
            var reads = 0;
            var navigations = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AttachmentCatalogueView? view = null;
            using var model = new AttachmentCatalogueViewModel(async (query, _, current, token) =>
            {
                reads++;
                if (action is "read-hide")
                {
                    entered.TrySetResult();
                    await release.Task;
                }
                return new SessionAttachmentPage(query, [summary], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
            }, (_, _, _) => { navigations++; return Task.CompletedTask; },
                new WindowsSqliteSessionWorkspaceTests.Access(), () => view?.IsNativePresented == true,
                NullLogger<AttachmentCatalogueViewModel>.Instance);
            view = new(shell, model, () => native.Main.CanRevealPrivatePresentation);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            try
            {
                responses.ShowHostedView(WorkbenchActivity.Attachments, view);
                shell.Width = width; shell.Height = height;
                shell.UpdateLayout();
                view.RefreshPresentation();
                view.IsNativePresented.Should().BeTrue();
                reads.Should().Be(0);
                view.PrepareExactSession(summary.Session.Value, static () => true);
                reads.Should().Be(0);
                view.FindControl<TextBox>("AttachmentQueryText")!.IsFocused.Should().BeTrue();
                view.FindControl<Button>("RefreshPage")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (action is "read-hide") { await entered.Task; }
                else
                {
                    await view.Operation;
                    model.Records.Should().ContainSingle();
                    model.Select(model.Records.Single());
                    model.MetadataFindQuery = "text";
                    model.FindMetadata(SnapshotFindDirection.First);
                    model.CanNavigate.Should().BeTrue();
                    model.HasMetadataFindMatch.Should().BeTrue();
                }
                if (action is "minimize")
                { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
                else if (action is "leave") { shell.ShowSessions(expanded: true); }
                else { shell.Hide(); shell.Show(); shell.Activate(); }
                if (action is "read-hide")
                {
                    view.Operation.IsCompleted.Should().BeFalse();
                    release.TrySetResult();
                    await view.Operation;
                }
                Dispatcher.UIThread.RunJobs();
                model.Records.Should().BeEmpty();
                model.SelectedDetails.Should().BeEmpty();
                model.MetadataFindQuery.Should().BeEmpty();
                model.CanNavigate.Should().BeFalse();
                model.CanNext.Should().BeFalse();
                navigations.Should().Be(0);
                reads.Should().Be(1);
                responses.ShowHostedView(WorkbenchActivity.Attachments, view);
                view.RefreshPresentation();
                model.Records.Should().BeEmpty("restoring never repeats metadata reads or selection");
                reads.Should().Be(1);
                windows.Distinct().Should().Equal(shell);
            }
            finally { release.TrySetResult(); await view.Operation; view.Dispose(); }
        });
    }
}
