using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardJsonWindowTests
{
    [Fact]
    public async Task OnlyExplicitNativeGestureParsesExactSourceAndFindEditsRemainIndependent()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var calls = 0;
            var source = Snapshot("{\"private-marker\":true,\"private-marker\":false}");
            var originalBytes = source.GetUtf8Bytes();
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected fixture failure"));
            window.Show(source, NoMutation, NoMutation, () => true, validateJson: token =>
            { calls++; return ClipboardJsonSyntax.Validate(source, token); });
            try
            {
                var status = Status(window);
                status.Text.Should().BeNull();
                calls.Should().Be(0);
                var query = window.GetVisualDescendants().OfType<TextBox>().Single();
                query.Text = "private-marker";
                await Command(window, "Find first").ExecuteAsync();
                calls.Should().Be(0);
                await Command(window, "Validate this snapshot as JSON").ExecuteAsync();
                calls.Should().Be(1);
                status.Text.Should().Be("Syntactically valid JSON. Root kind: Object.");
                status.IsFocused.Should().BeTrue();
                window.GetVisualDescendants().OfType<NamedTextBlock>().Single(control =>
                    AutomationProperties.GetName(control) is "Clipboard snapshot plain text content").Text.Should().Be(source.Text);
                window.GetVisualDescendants().OfType<NamedTextBlock>().Should().Contain(control =>
                    control.Text != null && control.Text.Contains("Duplicate property names are not checked", StringComparison.Ordinal));
                query.Text = "true";
                calls.Should().Be(1);
                status.Text.Should().Contain("Root kind: Object");
                source.GetUtf8Bytes().Should().Equal(originalBytes);
                window.ClearAndClose();
                status.Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("before")]
    [InlineData("reentrant")]
    [InlineData("close")]
    [InlineData("json-retired")]
    public async Task QueuedAndReentrantCallbacksNeverPublishLateSyntaxOrRetargetSource(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var current = true;
            var jsonCurrent = true;
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected fixture failure"));
            var source = Snapshot("true");
            window.Show(source, NoMutation, NoMutation, () => current, validateJson: token =>
            {
                calls++;
                if (change is "reentrant") { current = false; }
                if (change is "close") { window.ClearAndClose(); token.IsCancellationRequested.Should().BeTrue(); }
                if (change is "json-retired") { jsonCurrent = false; }
                return ClipboardJsonSyntax.Validate(source, CancellationToken.None);
            }, isJsonCurrent: () => jsonCurrent);
            var status = Status(window);
            var queued = Command(window, "Validate this snapshot as JSON");
            if (change is "before") { current = false; }
            await queued.ExecuteAsync();
            status.Text.Should().BeNull();
            calls.Should().Be(change is "before" ? 0 : 1);
            await queued.ExecuteAsync();
            calls.Should().Be(change is "before" ? 0 : 1);
            window.ClearAndClose();
        });
    }

    [Fact]
    public async Task OriginalControlGenerationCannotValidateReopenedWindowAndClearOnlyRetiresDerivedState()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var calls = 0;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected fixture failure"));
            var source = Snapshot("true");
            ClipboardJsonSyntaxResult Validate(CancellationToken token) { calls++; return ClipboardJsonSyntax.Validate(source, token); }
            window.Show(source, NoMutation, NoMutation, () => true, validateJson: Validate);
            var old = Command(window, "Validate this snapshot as JSON");
            await old.ExecuteAsync();
            var status = Status(window);
            window.ClearJsonValidation();
            status.Text.Should().BeNull();
            window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Validate this snapshot as JSON")).IsEnabled.Should().BeFalse();
            window.Content.Should().NotBeNull();
            await old.ExecuteAsync();
            calls.Should().Be(1);
            window.ClearAndClose();
            window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected fixture failure"));
            window.Show(source, NoMutation, NoMutation, () => true, validateJson: Validate);
            await old.ExecuteAsync();
            calls.Should().Be(1);
            await Command(window, "Validate this snapshot as JSON").ExecuteAsync();
            calls.Should().Be(2);
            window.ClearAndClose();
        });
    }

    [Theory]
    [InlineData(ClipboardJsonSyntaxOutcome.InvalidSyntax, "Invalid JSON syntax")]
    [InlineData(ClipboardJsonSyntaxOutcome.DepthLimit, "nesting exceeds 64")]
    [InlineData(ClipboardJsonSyntaxOutcome.Cancelled, "cancelled")]
    [InlineData(ClipboardJsonSyntaxOutcome.Unavailable, "unavailable")]
    [InlineData(ClipboardJsonSyntaxOutcome.Failed, "failed internally")]
    public async Task NativeOnlySafeTypedOutcomesHaveNoExceptionOrSourceExcerpt(ClipboardJsonSyntaxOutcome outcome, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected fixture failure"));
            window.Show(Snapshot("{\"private-marker\":}"), NoMutation, NoMutation, () => true,
                validateJson: _ => new(outcome, LineNumber: 2, BytePositionInLine: 7));
            await Command(window, "Validate this snapshot as JSON").ExecuteAsync();
            Status(window).Text.Should().Contain(expected).And.NotContain("private-marker").And.NotContain("JsonException");
            window.ClearAndClose();
        });
    }

    [Fact]
    public void ControllerBindsOriginalSourceWindowAdmissionAndDisposalWithoutFallback()
    {
        var source = Snapshot("true");
        ClipboardSnapshot? current = source;
        var views = new List<Preview>();
        var admitted = true;
        var calls = 0;
        var clears = 0;
        using var controller = new ClipboardPreviewWindowController(() => current, _ => NoMutation(), () => clears++,
            NoMutation, () => { var view = new Preview(); views.Add(view); return view; },
            _ => throw new InvalidOperationException("Unexpected fixture failure"), admitJson: _ => () => admitted,
            validateJson: (exact, eligible, token) =>
            {
                calls++;
                exact.Should().BeSameAs(current);
                eligible().Should().BeTrue();
                return ClipboardJsonSyntax.Validate(exact, token);
            });
        controller.Refresh();
        var old = views[0].Validate!;
        old(CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Valid);
        admitted = false;
        controller.Refresh();
        views[0].JsonClears.Should().Be(1);
        clears.Should().Be(0);
        old(CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Unavailable);
        admitted = true;
        current = Snapshot("false");
        controller.Refresh();
        old(CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Unavailable);
        views[1].Validate!(CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Valid);
        controller.Dispose();
        views[1].Validate!(CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Unavailable);
        calls.Should().Be(2);
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("dispose")]
    [InlineData("admission")]
    public void ControllerRechecksAfterReentrantActionBeforeReturningSyntax(string change)
    {
        ClipboardSnapshot? source = Snapshot("true");
        Preview? view = null;
        var admitted = true;
        ClipboardPreviewWindowController? controller = null;
        using (controller = new ClipboardPreviewWindowController(() => source, _ => NoMutation(), () => { }, NoMutation,
            () => view = new(), _ => throw new InvalidOperationException("Unexpected fixture failure"),
            admitJson: _ => () => admitted, validateJson: (_, _, _) =>
            {
                if (change is "replace") { source = Snapshot("false"); controller!.Refresh(); }
                if (change is "dispose") { controller!.Dispose(); }
                if (change is "admission") { admitted = false; }
                return new(ClipboardJsonSyntaxOutcome.Valid, System.Text.Json.JsonValueKind.True);
            }))
        {
            controller.Refresh();
            var queued = view!.Validate!;
            queued(CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Cancelled);
        }
    }

    private static Task NoMutation() => throw new InvalidOperationException("No clipboard read/write/reuse/revoke permitted.");
    private static AsyncCommand Command(Window window, string caption) =>
        (AsyncCommand)window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, caption)).Command!;
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(control =>
        AutomationProperties.GetName(control) is "Clipboard JSON syntax outcome only; original snapshot remains untrusted inert text");
    private static ClipboardSnapshot Snapshot(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UtcNow);
    private sealed class Preview : IClipboardPreviewView
    {
        public event EventHandler? Closed { add { } remove { } }
        internal Func<CancellationToken, ClipboardJsonSyntaxResult>? Validate { get; private set; }
        internal int JsonClears { get; private set; }
        public void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke, Func<bool> isCurrent,
            Func<Task>? explain = null, Func<CancellationToken, ClipboardJsonSyntaxResult>? validateJson = null, Func<bool>? isJsonCurrent = null,
            Func<ClipboardSnapshot, Func<bool>, CancellationToken, ClipboardXmlValidationResult>? validateXml = null) =>
            Validate = validateJson;
        public void SetExplanation(string? text) { }
        public void SetBase64Inspection(Func<ClipboardSnapshot, CancellationToken, ClipboardBase64Inspection> inspect) { }
        public void ClearJsonValidation() => JsonClears++;
        public void ClearAndClose() { }
    }
}
