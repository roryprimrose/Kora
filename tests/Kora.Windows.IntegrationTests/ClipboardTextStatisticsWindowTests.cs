using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardTextStatisticsWindowTests
{
    [Fact]
    public async Task OnlyExplicitAccessibleButtonCountsFullSnapshotAndPreservesFindAndInertSource()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var snapshot = Snapshot("hello world\r\n");
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected counting failure"));
            window.Show(snapshot, UnexpectedMutation, UnexpectedMutation, () => true, countText: (source, current, token) =>
            {
                calls++;
                source.Should().BeSameAs(snapshot);
                current().Should().BeTrue();
                return new(source, ClipboardTextStatistics.Measure(source, token));
            });
            var status = Status(window);
            var button = Button(window);
            var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
            peer.GetName().Should().Be("Count this exact clipboard snapshot text");
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Button);
            status.Focusable.Should().BeTrue();
            calls.Should().Be(0);
            status.Text.Should().BeNull();
            var find = window.GetVisualDescendants().OfType<TextBox>().Single();
            find.Text = "world";
            await ((AsyncCommand)window.GetVisualDescendants().OfType<Button>()
                .Single(control => Equals(control.Content, "Find first")).Command!).ExecuteAsync();
            var findStatus = window.GetVisualDescendants().OfType<NamedTextBlock>()
                .Single(control => AutomationProperties.GetName(control) is "Clipboard literal Find outcome and exact UTF-16 location");
            var previous = findStatus.Text;
            calls.Should().Be(0);
            await ((AsyncCommand)button.Command!).ExecuteAsync();
            calls.Should().Be(1);
            status.Text.Should().Be("UTF-8 bytes: 13; UTF-16 code units: 13; Unicode scalars: 13; whitespace-delimited words: 2; logical lines: 2.");
            status.Text.Should().NotContain("hello").And.NotContain("world");
            findStatus.Text.Should().Be(previous);
            window.GetVisualDescendants().OfType<NamedTextBlock>()
                .Single(control => AutomationProperties.GetName(control) is "Clipboard snapshot plain text content").Text.Should().Be(snapshot.Text);
            window.ClearAndClose();
            status.Text.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("replace")]
    [InlineData("same-id")]
    [InlineData("revoke")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task RetiredControllerAndQueuedButtonsNeverRetargetOrPublish(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var original = Snapshot("private");
            ClipboardSnapshot? current = original;
            var eligible = true;
            var calls = 0;
            var views = new List<ClipboardPreviewWindow>();
            using var controller = new ClipboardPreviewWindowController(() => eligible ? current : null,
                _ => UnexpectedMutation(), () => current = null, UnexpectedMutation,
                () => { var window = new ClipboardPreviewWindow(_ => { }); views.Add(window); return window; },
                _ => throw new InvalidOperationException("Unexpected controller failure"),
                countText: (source, admitted, token) =>
                {
                    calls++;
                    admitted().Should().BeTrue();
                    return new(source, ClipboardTextStatistics.Measure(source, token));
                });
            controller.Refresh();
            var old = views[0];
            var status = Status(old);
            var queued = (AsyncCommand)Button(old).Command!;
            await queued.ExecuteAsync();
            status.Text.Should().NotBeNullOrEmpty();
            switch (change)
            {
                case "privacy":
                case "owner":
                case "call": eligible = false; break;
                case "replace": current = Snapshot("new"); break;
                case "same-id": current = new(original.SourceId, original.SnapshotId, original.Request, "new", 1, original.CapturedAt); break;
                case "revoke": current = null; break;
                case "close": old.Close(); break;
                default: controller.Dispose(); break;
            }
            await queued.ExecuteAsync();
            status.Text.Should().BeNull();
            old.Content.Should().BeNull();
            calls.Should().Be(1);
            eligible = true;
            current = Snapshot("new");
            controller.Refresh();
            await queued.ExecuteAsync();
            calls.Should().Be(1);
            if (views.Count > 1) { Status(views[^1]).Text.Should().BeNull(); }
        });
    }

    [Theory]
    [InlineData("during-count")]
    [InlineData("during-publish")]
    [InlineData("reentrant-close")]
    public async Task ReentrantCallbacksCannotPublishRetiredCounts(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var eligible = true;
            var window = new ClipboardPreviewWindow(_ => { });
            window.Show(Snapshot("private"), UnexpectedMutation, UnexpectedMutation, () => eligible,
                countText: (snapshot, _, token) =>
                {
                    if (change is "during-count") { eligible = false; }
                    if (change is "reentrant-close") { window.ClearAndClose(); }
                    return new(snapshot, ClipboardTextStatistics.Measure(snapshot, token));
                });
            var status = Status(window);
            status.PropertyChanged += (_, args) =>
            {
                if (change is "during-publish" && args.Property == TextBlock.TextProperty && status.Text is not null) { eligible = false; }
            };
            await ((AsyncCommand)Button(window).Command!).ExecuteAsync();
            status.Text.Should().BeNull();
            window.Content.Should().BeNull();
        });
    }

    [Fact]
    public async Task OriginalInspectionExpiryRetiresCountsWithoutClosingOrRenewingSnapshot()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var admitted = true;
            var source = Snapshot("private");
            var calls = 0;
            ClipboardPreviewWindow? window = null;
            using var controller = new ClipboardPreviewWindowController(() => source,
                _ => UnexpectedMutation(), () => { }, UnexpectedMutation,
                () => window = new ClipboardPreviewWindow(_ => { }), _ => { },
                admitJson: _ => () => admitted,
                countText: (snapshot, _, token) =>
                {
                    calls++;
                    return new(snapshot, ClipboardTextStatistics.Measure(snapshot, token));
                });
            controller.Refresh();
            var status = Status(window!);
            var queued = (AsyncCommand)Button(window!).Command!;
            await queued.ExecuteAsync();
            status.Text.Should().NotBeNullOrEmpty();
            admitted = false;
            controller.Refresh();
            status.Text.Should().BeNull();
            window!.Content.Should().NotBeNull();
            admitted = true;
            await queued.ExecuteAsync();
            calls.Should().Be(1);
            source.Text.Should().Be("private");
        });
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("failure")]
    [InlineData("wrong-source")]
    public async Task FailuresAreExplicitWithoutSourceExcerptsOrSuccessShapedZeroCounts(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var failures = 0;
            var window = new ClipboardPreviewWindow(_ => failures++);
            window.Show(Snapshot("private marker"), UnexpectedMutation, UnexpectedMutation, () => true,
                countText: (snapshot, _, _) => change switch
                {
                    "failure" => throw new InvalidOperationException("private marker"),
                    "wrong-source" => new(Snapshot("other source"), new(0, 0, 0, 0, 0)),
                    _ => new(snapshot, null),
                });
            Button(window).Command!.Execute(null);
            Status(window).Text.Should().Contain("unavailable").And.Contain("No counts")
                .And.NotContain("private").And.NotContain("0");
            failures.Should().Be(change is "unavailable" ? 0 : 1);
            window.ClearAndClose();
            return Task.CompletedTask;
        });
    }

    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UtcNow);
    private static Button Button(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Count this snapshot text"));
    private static NamedTextBlock Status(Window window) =>
        window.GetVisualDescendants().OfType<NamedTextBlock>()
            .Single(control => AutomationProperties.GetName(control) is "Text counts for this exact clipboard snapshot");
    private static Task UnexpectedMutation() => throw new InvalidOperationException("Text counting cannot invoke clipboard mutations.");
}
