using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Tools.Files;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionAttachmentWindowTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("hide-pending")]
    [InlineData("hide-source")]
    [InlineData("minimize-source")]
    [InlineData("tab-source")]
    public async Task HostedAttachmentUsesSameShellForSeparateCaptureAndExactRetainedReader(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(storage, HostTaskState.Succeeded);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(storage, access);
            var capture = new Capture();
            var audit = new Audit();
            using var preview = new LocalFilePreview(capture, audit, storage.Time, NullLogger<LocalFilePreview>.Instance);
            await using var files = new SessionFileAttachmentService(sessions, access,
                new LocalSessionFileAttach(capture, audit, storage.Time, NullLogger<LocalFilePreview>.Instance),
                new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, storage.Time, NullLogger<LocalFileSearch>.Instance),
                storage.Time, audit, NullLogger<SessionFileAttachmentService>.Instance);
            var failures = new List<string>();
            using var controller = new SessionAttachmentWindowController(failures.Add, files, sessions, access, new Picker());
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            controller.BindWorkbench(responses.PrepareWorkbenchOwner);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            var target = await storage.Store.ReadMetadataAsync(storage.Request.SessionId, storage.Token);
            await controller.Open(target, attach: true, static () => true, storage.Token);
            var view = controller.View!;
            var shell = (ResponseWindow)view.Owner;
            var confirm = view.GetVisualDescendants().OfType<Button>().Single(button =>
                string.Equals(button.Content as string, "Confirm: capture and retain this exact file for this Session", StringComparison.Ordinal));
            capture.Reads.Should().Be(0);
            storage.Count("session_file").Should().Be(0);
            if (action is not "hide-pending")
            {
                await ((AsyncCommand)confirm.Command!).ExecuteAsync();
                capture.Reads.Should().Be(1);
                storage.Count("session_file").Should().Be(1);
                var retained = (await files.Read(target.Authority.SessionId, storage.Token))!;
                view.GetVisualDescendants().OfType<NamedTextBlock>().Should().Contain(block =>
                    string.Equals(block.Text, Capture.Text, StringComparison.Ordinal));
                var query = view.GetVisualDescendants().OfType<TextBox>().Single(box =>
                    box.PlaceholderText?.StartsWith("Untrusted lexical query", StringComparison.Ordinal) == true);
                query.Text = "café";
                var search = view.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Content as string, "Search selected immutable revision locally", StringComparison.Ordinal));
                await ((AsyncCommand)search.Command!).ExecuteAsync();
                var rows = view.GetVisualDescendants().OfType<ListBox>().Single();
                rows.SelectedItem = rows.ItemsSource!.Cast<LocalFileCitation>().Single();
                var navigate = view.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Content as string, "Show exact cited source span", StringComparison.Ordinal));
                await ((AsyncCommand)navigate.Command!).ExecuteAsync();
                view.GetVisualDescendants().OfType<NamedTextBlock>().Should().Contain(block =>
                    block.IsFocused && string.Equals(block.Text, Capture.Text, StringComparison.Ordinal));
                capture.Reads.Should().Be(1, "search and citations use the retained copy, not a fresh original-path read");
                (await files.Read(target.Authority.SessionId, storage.Token))!.File.Reference.Should().Be(retained.File.Reference);
            }
            if (action is "minimize-source")
            { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
            else if (action is "tab-source")
            {
                var other = new TextBlock { Text = "Another exact review" };
                shell.ShowNativeReview(other, "Another exact review", other, static () => true, _ => { });
            }
            else { shell.Hide(); shell.Show(); shell.Activate(); }
            view.Content.Should().BeNull();
            controller.View.Should().BeNull();
            confirm.Command.Should().BeNull();
            capture.Reads.Should().Be(action is "hide-pending" ? 0 : 1);
            storage.Count("session_file").Should().Be(action is "hide-pending" ? 0 : 1);
            storage.Count("conversation_messages").Should().Be(0);
            windows.Distinct().Should().Equal(shell);
            failures.Should().BeEmpty();
        });
    }
}
