using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Network;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardWebUrlWindowTests
{
    private const string Caption = "Inspect this snapshot as a web URL (no fetch)";
    private const string ResultName = "Local web URL syntax facts; not a verified destination";

    [Fact]
    public async Task Explicit_accessible_button_keeps_source_and_find_inert_and_unchanged()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var calls = 0;
            var snapshot = Snapshot("https://bücher.example/private?token=secret#hidden");
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected host callback"));
            window.BindWebUrlInspection(_ => { calls++; return WebPageAddressSyntax.InspectExact(snapshot.Text); });
            window.Show(snapshot, Unexpected, Unexpected, () => true);
            try
            {
                var button = Button(window);
                var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Button);
                peer.GetName().Should().Be(Caption);
                Result(window).Text.Should().BeNull();
                calls.Should().Be(0);
                var find = window.GetVisualDescendants().OfType<TextBox>().Single();
                find.Text = "private";
                calls.Should().Be(0);
                var first = window.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, "Find first"));
                await ((AsyncCommand)first.Command!).ExecuteAsync();
                var findStatus = window.GetVisualDescendants().OfType<NamedTextBlock>()
                    .Single(control => AutomationProperties.GetName(control) is "Clipboard literal Find outcome and exact UTF-16 location");
                var previousFind = findStatus.Text;
                await ((AsyncCommand)button.Command!).ExecuteAsync();
                calls.Should().Be(1);
                Result(window).Text.Should().Contain("Syntax accepted only").And.Contain("xn--bcher-kva.example")
                    .And.Contain("query present: True").And.Contain("fragment present: True")
                    .And.NotContain("private").And.NotContain("token").And.NotContain("secret").And.NotContain("hidden")
                    .And.NotContain("https://");
                find.Text.Should().Be("private");
                findStatus.Text.Should().Be(previousFind);
                var source = window.GetVisualDescendants().OfType<NamedTextBlock>()
                    .Single(control => AutomationProperties.GetName(control) is "Clipboard snapshot plain text content");
                source.Text.Should().Be(snapshot.Text);
                var result = Result(window);
                window.ClearAndClose();
                result.Text.Should().BeNull();
                await ((AsyncCommand)button.Command!).ExecuteAsync();
                calls.Should().Be(1);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("https://user:secret@example.com/", "URL syntax refused")]
    [InlineData("https://example.com/path?secret", "Syntax accepted only")]
    public async Task Controller_fences_replacement_queued_callbacks_and_disposal(string text, string outcome)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Snapshot(text);
            ClipboardSnapshot? current = source;
            ClipboardPreviewWindow? window = null;
            var calls = 0;
            using var controller = new ClipboardPreviewWindowController(() => current, _ => Unexpected(), () => current = null,
                Unexpected, () => window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected failure")),
                _ => throw new InvalidOperationException("Unexpected host failure"), inspectWebUrl: (exact, eligible, _) =>
                {
                    calls++;
                    exact.Should().BeSameAs(source);
                    eligible().Should().BeTrue();
                    return WebPageAddressSyntax.InspectExact(exact.Text);
                });
            controller.Refresh();
            var button = Button(window!);
            await ((AsyncCommand)button.Command!).ExecuteAsync();
            var result = Result(window!);
            result.Text.Should().Contain(outcome);
            current = Snapshot("https://other.example/");
            controller.Refresh();
            result.Text.Should().BeNull();
            await ((AsyncCommand)button.Command!).ExecuteAsync();
            calls.Should().Be(1);
            var replacement = Button(window!);
            controller.Dispose();
            await ((AsyncCommand)replacement.Command!).ExecuteAsync();
            calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    [InlineData("throw")]
    public async Task Late_changed_or_failed_operations_never_publish_facts(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            var snapshot = Snapshot("https://example.com/private?token=secret");
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("No raw failure may escape"));
            NamedTextBlock? result = null;
            window.BindWebUrlInspection(token =>
            {
                if (change is "close") { window.ClearAndClose(); token.IsCancellationRequested.Should().BeTrue(); }
                if (change is "privacy") { current = false; }
                if (change is "cancel") { return new(WebUrlSyntaxOutcome.Cancelled); }
                if (change is "throw") { throw new InvalidOperationException("https://password@private/path?secret"); }
                return WebPageAddressSyntax.InspectExact(snapshot.Text);
            });
            window.Show(snapshot, Unexpected, Unexpected, () => current);
            result = Result(window);
            var command = (AsyncCommand)Button(window).Command!;
            if (change is "throw")
            {
                // Framework ICommand boundary invokes its content-minimized native error handler.
                command.Execute(null);
                result.Text.Should().Contain("failed unexpectedly").And.NotContain("password").And.NotContain("private");
            }
            else { await command.ExecuteAsync(); }
            result.Text.Should().NotContain("Syntax accepted");
            window.ClearAndClose();
            result.Text.Should().BeNull();
        });
    }

    private static Task Unexpected() => Task.FromException(new InvalidOperationException("No source mutation is permitted."));
    private static ClipboardSnapshot Snapshot(string text) => new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UtcNow);
    private static Button Button(Window window) => window.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, Caption));
    private static NamedTextBlock Result(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(control => string.Equals(AutomationProperties.GetName(control), ResultName, StringComparison.Ordinal));
}
