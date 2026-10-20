using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardBase64WindowTests
{
    private const string Caption = "Inspect this snapshot as standard Base64";

    [Theory]
    [InlineData(640, 480)]
    [InlineData(760, 600)]
    public async Task NativeActionAndResultAreScrollableWithoutCollapsingOriginalSourcePane(int width, int height)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected failure"));
            window.SetBase64Inspection((snapshot, token) => ClipboardBase64Policy.Inspect(snapshot.Text, token));
            window.Show(Snapshot("AA=="), UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                window.Width = width;
                window.Height = height;
                window.UpdateLayout();
                var source = window.GetVisualDescendants().OfType<NamedTextBlock>()
                    .Single(control => AutomationProperties.GetName(control) is "Clipboard snapshot plain text content");
                var sourceViewport = source.GetVisualAncestors().OfType<ScrollViewer>().Single();
                sourceViewport.Viewport.Width.Should().BeGreaterThan(0);
                sourceViewport.Viewport.Height.Should().BeGreaterThan(0);
                var button = Button(window);
                button.GetVisualAncestors().OfType<ScrollViewer>().Should().ContainSingle()
                    .Which.Should().NotBeSameAs(sourceViewport);
                button.BringIntoView();
                window.UpdateLayout();
                await ((AsyncCommand)button.Command!).ExecuteAsync();
                var status = Status(window);
                status.Text.Should().Contain("exact decoded byte count: 1");
                status.BringIntoView();
                window.UpdateLayout();
                var location = status.TranslatePoint(default, window)!.Value;
                location.X.Should().BeGreaterThanOrEqualTo(0);
                location.Y.Should().BeGreaterThanOrEqualTo(0);
                (location.X + status.Bounds.Width).Should().BeLessThanOrEqualTo(window.ClientSize.Width);
                (location.Y + status.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                source.Text.Should().Be("AA==");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("AA==", "exact decoded byte count: 1")]
    [InlineData("AAA=", "exact decoded byte count: 2")]
    [InlineData("AAAA", "exact decoded byte count: 3")]
    [InlineData("AB==", "InvalidProfile: NonCanonicalUnusedBits; UTF-16 offset 1")]
    public async Task ExplicitAccessibleNativeGestureOnlyPreservesOriginalSourceAndFind(string encoded, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var snapshot = Snapshot(encoded);
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected failure"));
            window.SetBase64Inspection((source, token) =>
            {
                source.Should().BeSameAs(snapshot);
                calls++;
                return ClipboardBase64Policy.Inspect(source.Text, token);
            });
            window.Show(snapshot, UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                var button = Button(window);
                var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
                peer.GetName().Should().Be(Caption);
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Button);
                Status(window).Text.Should().BeNull();
                calls.Should().Be(0);
                var query = window.GetVisualDescendants().OfType<TextBox>().Single();
                query.Text = "A";
                calls.Should().Be(0);
                var find = window.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, "Find first"));
                await ((AsyncCommand)find.Command!).ExecuteAsync();
                calls.Should().Be(0);
                await ((AsyncCommand)button.Command!).ExecuteAsync();
                Status(window).Text.Should().Contain(expected).And.NotContain(encoded);
                calls.Should().Be(1);
                window.GetVisualDescendants().OfType<NamedTextBlock>()
                    .Single(control => AutomationProperties.GetName(control) is "Clipboard snapshot plain text content")
                    .Text.Should().Be(encoded);
                query.Text.Should().Be("A");
                var status = Status(window);
                window.ClearAndClose();
                status.Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("during")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("reentrant")]
    public async Task StaleQueuedClosedCancelledOrReentrantCallbacksCannotPublish(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected failure"));
            CancellationToken admitted = default;
            window.SetBase64Inspection((snapshot, token) =>
            {
                calls++;
                admitted = token;
                switch (change)
                {
                    case "during": current = false; break;
                    case "close": window.ClearAndClose(); break;
                    case "cancel": throw new OperationCanceledException();
                    case "reentrant":
                        Button(window).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                        window.ClearAndClose();
                        break;
                }
                return ClipboardBase64Policy.Inspect(snapshot.Text, CancellationToken.None);
            });
            window.Show(Snapshot("AA=="), UnexpectedMutation, UnexpectedMutation, () => current);
            var status = Status(window);
            var command = (AsyncCommand)Button(window).Command!;
            if (change is "queued") { current = false; }
            await command.ExecuteAsync();
            status.Text.Should().BeNull();
            calls.Should().Be(change is "queued" ? 0 : 1);
            window.ClearAndClose();
            await command.ExecuteAsync();
            status.Text.Should().BeNull();
            if (change is not "queued") { admitted.IsCancellationRequested.Should().BeTrue(); }
        });
    }

    [Fact]
    public async Task UnexpectedFailureIsNativeUnavailableNotInvalidProfileOrRawExceptionText()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var reports = 0;
            var window = new ClipboardPreviewWindow(_ => reports++);
            window.SetBase64Inspection((_, _) => throw new InvalidOperationException("secret body"));
            window.Show(Snapshot("AA=="), UnexpectedMutation, UnexpectedMutation, () => true);
            Button(window).Command!.Execute(null);
            Status(window).Text.Should().Contain("unavailable").And.NotContain("InvalidProfile").And.NotContain("secret body");
            reports.Should().Be(1);
            window.Close();
        });
    }

    private static Button Button(Window window) => window.GetVisualDescendants().OfType<Button>()
        .Single(control => Equals(control.Content, Caption));
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(control => AutomationProperties.GetName(control) is "Native standard Base64 syntax and exact decoded byte count only");
    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UtcNow);
    private static Task UnexpectedMutation() => throw new InvalidOperationException("No clipboard or model action is allowed.");
}
