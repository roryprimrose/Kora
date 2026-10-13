using System.Text;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Controls;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Kora.Windows.Context;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionAttachmentWindowTests
{
    [Theory]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("session")]
    [InlineData("audit-failure")]
    public async Task LiteralFindInActualRetainedDoneControllerRevalidatesPrivateSqliteWithoutOriginalPathOrActivityRenewal(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var sessions = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var audit = new FindAudit();
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kora.slnx"))) { repository = repository.Parent; }
            var scratch = Path.Combine(repository!.FullName, ".literal-find-source-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            var path = Path.Combine(scratch, "original.md");
            const string text = "# inert\r\n🙂 EXACT_LITERAL_SECRET EXACT_LITERAL_SECRET\r\n";
            await File.WriteAllTextAsync(path, text, fixture.Token);
            try
            {
                var picker = new ReplacementPicker(path);
                var unrelatedCapture = new Capture();
                using var preview = new LocalFilePreview(unrelatedCapture, audit, fixture.Time, NullLogger<LocalFilePreview>.Instance);
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
                var retained = (await service.Read(target.Authority.SessionId, fixture.Token))!;
                File.Delete(path);
                await sessions.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation, false, RequestOrigin.LocalUi, fixture.Token);
                var done = await fixture.Store.ReadMetadataAsync(target.Authority.SessionId, fixture.Token);
                done.Authority.IsActive.Should().BeFalse();
                var clock = await fixture.Store.ReadRetentionAsync(target.Authority.SessionId, fixture.Token);
                await controller.Open(done, attach: false, () => true, fixture.Token);
                var view = opened!;
                view.UpdateLayout();
                var query = view.GetVisualDescendants().OfType<TextBox>().Single(box =>
                    AutomationProperties.GetName(box)?.StartsWith("Case-sensitive literal Find", StringComparison.Ordinal) == true);
                var source = view.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    string.Equals(AutomationProperties.GetName(block), "Untrusted local file plain text content", StringComparison.Ordinal));
                query.Text = "EXACT_LITERAL_SECRET";
                await Press(view, "Find first");
                var span = view.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    AutomationProperties.GetName(block)?.StartsWith("Exact literal original", StringComparison.Ordinal) == true);
                span.Text.Should().Be(query.Text);
                span.IsFocused.Should().BeTrue();
                await Press(view, "Find next");
                span.Text.Should().Be(query.Text);
                (await service.Read(done.Authority.SessionId, fixture.Token))!.File.Reference.Should().Be(retained.File.Reference);
                (await fixture.Store.ReadRetentionAsync(done.Authority.SessionId, fixture.Token)).Should().Be(clock);
                fixture.Count("session_file").Should().Be(1);
                unrelatedCapture.Reads.Should().Be(0);
                picker.Calls.Should().Be(1);
                File.Exists(path).Should().BeFalse();
                source.Text.Should().Be(text);
                audit.Events.Select(entry => entry.ActionId + entry.TargetId + entry.ReasonCode)
                    .Should().NotContain(value => value.Contains("EXACT_LITERAL_SECRET", StringComparison.Ordinal)
                        || value.Contains(path, StringComparison.Ordinal));
                switch (boundary)
                {
                    case "private": access.CanInspect = false; service.Revoke(); break;
                    case "session": controller.RevokeSession(done.Authority.SessionId); break;
                    case "audit-failure":
                        audit.Fail = true;
                        query.Focus().Should().BeTrue();
                        var find = () => Press(view, "Find first");
                        await find.Should().ThrowAsync<IOException>();
                        span.Text.Should().BeNull();
                        span.IsFocused.Should().BeFalse();
                        controller.Close();
                        break;
                    default: controller.Close(); break;
                }
                query.Text.Should().BeNull();
                span.Text.Should().BeNull();
                source.Text.Should().BeNull();
                view.Content.Should().BeNull();
                failures.Should().BeEmpty();
                audit.Fail = false;
            }
            finally { Directory.Delete(scratch, recursive: true); }
        });
    }

    private sealed class FindAudit : ISecurityAuditLog
    {
        internal bool Fail { get; set; }
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (Fail) { throw new IOException("mandatory private audit unavailable"); }
            Events.Add(auditEvent);
        }
    }
}