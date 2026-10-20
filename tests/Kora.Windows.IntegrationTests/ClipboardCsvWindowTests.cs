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
public sealed class ClipboardCsvWindowTests
{
    private const string Caption = "Inspect this snapshot as comma-separated rows";

    [Fact]
    public async Task ExplicitAccessibleNativeButtonDoesNotAutoparseOrChangeSourceOrFind()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Snapshot("=private-marker,\"a,b\"\nlast");
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected native failure"));
            window.BindCsvInspection(token =>
            {
                calls++;
                return new(ClipboardCsvOutcome.Inspected, CommaSeparatedProfile.Inspect(source.Text, token));
            });
            window.Show(source, UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                var button = Button(window, Caption);
                var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
                peer.GetName().Should().Be(Caption);
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Button);
                Status(window).Text.Should().BeNull();
                calls.Should().Be(0);
                window.GetVisualDescendants().OfType<TextBox>().Single().Text = "private-marker";
                await ((AsyncCommand)Button(window, "Find first").Command!).ExecuteAsync();
                var find = Named(window, "Clipboard literal Find outcome and exact UTF-16 location");
                var findResult = find.Text;
                calls.Should().Be(0);
                await ((AsyncCommand)button.Command!).ExecuteAsync();
                calls.Should().Be(1);
                Status(window).Text.Should().Contain("Records: 2").And.Contain("1 to 2").And.Contain("ragged shape")
                    .And.Contain("spreadsheet-safe").And.NotContain("private-marker").And.NotContain("a,b");
                find.Text.Should().Be(findResult);
                Named(window, "Clipboard snapshot plain text content").Text.Should().Be(source.Text);
                window.GetVisualDescendants().OfType<TextBox>().Single().Text = "last";
                calls.Should().Be(1);
                var retainedStatus = Status(window);
                window.ClearAndClose();
                retainedStatus.Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("same-id")]
    [InlineData("revoke")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task QueuedOldControlNeverParsesReplacementOrRestoresClearedResult(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var original = Snapshot("a,b");
            ClipboardSnapshot? current = original;
            var eligible = true;
            var calls = 0;
            var views = new List<ClipboardPreviewWindow>();
            using var controller = new ClipboardPreviewWindowController(() => eligible ? current : null,
                _ => UnexpectedMutation(), () => current = null, UnexpectedMutation, () =>
                {
                    var view = new ClipboardPreviewWindow(_ => { });
                    views.Add(view);
                    return view;
                }, _ => throw new InvalidOperationException("Unexpected controller failure"),
                csvInspection: source => token =>
                {
                    ReferenceEquals(source, original).Should().BeTrue();
                    calls++;
                    return new(ClipboardCsvOutcome.Inspected, CommaSeparatedProfile.Inspect(source.Text, token));
                });
            controller.Refresh();
            var old = views[0];
            var command = (AsyncCommand)Button(old, Caption).Command!;
            var status = Status(old);
            await command.ExecuteAsync();
            calls.Should().Be(1);
            switch (change)
            {
                case "replacement": current = Snapshot("new"); break;
                case "same-id": current = new(original.SourceId, original.SnapshotId, original.Request, "different", original.Version, original.CapturedAt); break;
                case "revoke": current = null; break;
                case "private":
                case "owner":
                case "call": eligible = false; break;
                case "close": old.Close(); break;
                default: controller.Dispose(); break;
            }
            await command.ExecuteAsync();
            controller.Refresh();
            await command.ExecuteAsync();
            calls.Should().Be(1);
            status.Text.Should().BeNull();
        });
    }

    [Theory]
    [InlineData("a\"bad", "Profile mismatch: InvalidQuoting")]
    [InlineData("a\rb", "Profile mismatch: InvalidRecordSeparator")]
    [InlineData("a,b\nc,d", "uniform shape")]
    public async Task FixedProfileOutcomesExposeOnlyShapeOrTypedLocation(string text, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var source = Snapshot(text);
            var window = new ClipboardPreviewWindow(_ => { });
            window.BindCsvInspection(token => new(ClipboardCsvOutcome.Inspected, CommaSeparatedProfile.Inspect(source.Text, token)));
            window.Show(source, UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                await ((AsyncCommand)Button(window, Caption).Command!).ExecuteAsync();
                Status(window).Text.Should().Contain(expected).And.NotContain(text);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task ReentrantCloseDuringInspectionDropsDerivedResultAndCancelsWithoutSourceMutation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => { });
            window.BindCsvInspection(token =>
            {
                window.ClearAndClose();
                token.IsCancellationRequested.Should().BeTrue();
                return new(ClipboardCsvOutcome.Inspected, new(CommaSeparatedSyntax.Admitted, 1, 1, 1));
            });
            window.Show(Snapshot("secret"), UnexpectedMutation, UnexpectedMutation, () => true);
            var status = Status(window);
            await ((AsyncCommand)Button(window, Caption).Command!).ExecuteAsync();
            status.Text.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(ClipboardCsvOutcome.Cancelled, "cancelled")]
    [InlineData(ClipboardCsvOutcome.Stale, "unavailable")]
    [InlineData(ClipboardCsvOutcome.Unavailable, "failed internally")]
    public async Task CancellationAndInternalFailureAreNotEmptyOrInvalidSyntax(ClipboardCsvOutcome outcome, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => { });
            window.BindCsvInspection(_ => new(outcome));
            window.Show(Snapshot("private-marker"), UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                await ((AsyncCommand)Button(window, Caption).Command!).ExecuteAsync();
                Status(window).Text.Should().Contain(expected).And.NotContain("private-marker")
                    .And.NotContain("Empty input").And.NotContain("Profile mismatch");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task ReentrancyIsNotQueuedAndSourceLossBeforePublicationClosesAndClears()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => { });
            var current = true;
            var calls = 0;
            AsyncCommand? command = null;
            window.BindCsvInspection(_ =>
            {
                calls++;
                command!.ExecuteAsync().IsCompletedSuccessfully.Should().BeTrue();
                current = false;
                return new(ClipboardCsvOutcome.Inspected, new(CommaSeparatedSyntax.Admitted, 1, 1, 1));
            });
            window.Show(Snapshot("secret"), UnexpectedMutation, UnexpectedMutation, () => current);
            var status = Status(window);
            command = (AsyncCommand)Button(window, Caption).Command!;
            await command.ExecuteAsync();
            calls.Should().Be(1);
            status.Text.Should().BeNull();
            window.Content.Should().BeNull();
        });
    }

    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
    private static Task UnexpectedMutation() => throw new InvalidOperationException("No reuse, revoke, capture or model dispatch allowed.");
    private static Button Button(ClipboardPreviewWindow window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, content));
    private static NamedTextBlock Named(ClipboardPreviewWindow window, string name) =>
        window.GetVisualDescendants().OfType<NamedTextBlock>().Single(control => string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
    private static NamedTextBlock Status(ClipboardPreviewWindow window) => Named(window, "Native comma-separated syntax and row shape result");
}
