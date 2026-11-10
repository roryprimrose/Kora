using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ExactAnswerExportControllerTests
{
    [Theory]
    [InlineData("confirm")]
    [InlineData("cancel")]
    [InlineData("hide")]
    [InlineData("minimize")]
    [InlineData("tab")]
    [InlineData("source")]
    [InlineData("authority")]
    [InlineData("hold-close")]
    public async Task HostedExactExportSeparatesReadFromOneWriteAndJoinsRevokedStaging(string action)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var source = ExactAnswerExportWriterTests.Source();
            var current = source;
            var live = true;
            using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var writer = new Writer();
            writer.Destination.Hold = action is "hold-close";
            var audit = new Audit();
            var controller = new ExactAnswerExportWindowController(writer, audit, NullLogger<ExactAnswerExportReview>.Instance,
                (_, _) => Task.FromResult<string?>(writer.Destination.Path));
            var shell = new ResponseWindow(native.Main);
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => windows.Add(window));
            Task<string>? exporting = null;
            try
            {
                shell.ShowSessions(expanded: true);
                exporting = controller.ShowAsync(shell, source, () => live, _ => Task.FromResult(current), storage.Token);
                var view = shell.GetVisualDescendants().OfType<ExactAnswerExportView>().Single();
                var text = view.GetVisualDescendants().OfType<TextBox>().Single();
                text.Text.Should().Be(source.Source);
                writer.Destination.Calls.Should().Be(0);
                audit.Events.Should().BeEmpty();
                var confirm = view.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal));
                if (action is "source") { current = ExactAnswerExportWriterTests.Source(); }
                if (action is "confirm" or "source" or "hold-close")
                {
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (action is "hold-close") { await writer.Destination.Started.Task; }
                    else { await view.Operation; }
                }
                if (action is "hide" or "hold-close") { shell.Hide(); shell.Show(); shell.Activate(); }
                else if (action is "minimize")
                { shell.WindowState = WindowState.Minimized; shell.WindowState = WindowState.Normal; }
                else if (action is "tab")
                {
                    var other = new TextBlock { Text = "Another original review" };
                    shell.ShowNativeReview(other, "Another original review", other, static () => true, _ => { });
                }
                else if (action is "authority") { live = false; view.RefreshPresentation(); }
                else { view.Close(); }
                if (action is "hold-close")
                {
                    exporting.IsCompleted.Should().BeFalse("revocation is not physical writer quiescence");
                    writer.Destination.Disposals.Should().Be(0);
                    text.Text.Should().BeEmpty();
                    writer.Destination.Release.TrySetResult();
                }
                var result = await exporting.WaitAsync(TimeSpan.FromSeconds(10), storage.Token);
                result.Should().Contain(action is "confirm" ? "verified" : action is "source" ? "failed" : "cancelled");
                writer.Destination.Calls.Should().Be(action is "confirm" or "hold-close" ? 1 : 0);
                writer.Destination.Disposals.Should().Be(1);
                confirm.IsEnabled.Should().BeFalse();
                text.Text.Should().BeEmpty();
                string.Join("\n", view.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text))
                    .Should().NotContain(writer.Destination.Path).And.NotContain(source.Reference.ItemId.Value.ToString("D"));
                windows.Distinct().Should().Equal(shell);
            }
            finally
            {
                writer.Destination.Release.TrySetResult();
                shell.Close();
                if (exporting is not null) { await exporting; }
            }
        });
    }

    [Theory]
    [InlineData(480, 400, 14, false)]
    [InlineData(480, 400, 24, false)]
    [InlineData(480, 400, 24, true)]
    [InlineData(820, 720, 24, false)]
    public async Task CompleteLongDestinationReviewAndOneShotActionsRemainReachable(
        int width, int height, int fontSize, bool cancel)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            var source = ExactAnswerExportWriterTests.Source();
            using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var writer = new Writer();
            writer.Destination.Path = System.IO.Path.Combine(OwnedStorageFixture.ScratchParent(),
                string.Join(System.IO.Path.DirectorySeparatorChar, Enumerable.Repeat(new string('d', 80), 12)), "exact-answer.md");
            var audit = new Audit();
            var controller = new ExactAnswerExportWindowController(writer, audit, NullLogger<ExactAnswerExportReview>.Instance,
                (_, _) => Task.FromResult<string?>(writer.Destination.Path));
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = controller.ShowAsync(owner, source, () => true, _ => Task.FromResult(source), storage.Token);
                var review = owner.OwnedWindows.OfType<ExactAnswerExportWindow>().Single();
                review.Width = width;
                review.Height = height;
                review.FontSize = fontSize;
                review.UpdateLayout();
                var text = review.GetVisualDescendants().OfType<TextBox>().Single();
                text.Text.Should().Be(source.Source);
                text.Bounds.Height.Should().BeGreaterThan(60, "the complete original answer needs a usable independent viewport");
                var confirm = review.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal));
                var close = review.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "CancelExactAnswerExport", StringComparison.Ordinal));
                foreach (var control in new Control[] { text, confirm, close })
                {
                    var point = control.TranslatePoint(default, review)!.Value;
                    point.X.Should().BeGreaterThanOrEqualTo(0);
                    point.Y.Should().BeGreaterThanOrEqualTo(0);
                    (point.X + control.Bounds.Width).Should().BeLessThanOrEqualTo(review.ClientSize.Width);
                    (point.Y + control.Bounds.Height).Should().BeLessThanOrEqualTo(review.ClientSize.Height);
                }
                var metadata = review.GetVisualDescendants().OfType<TextBlock>()
                    .Single(label => label.Text?.Contains("New destination:", StringComparison.Ordinal) == true);
                metadata.Text.Should().Contain(writer.Destination.Path).And.Contain("Format:");
                var chrome = metadata.GetVisualAncestors().OfType<ScrollViewer>().First();
                chrome.Extent.Height.Should().BeGreaterThan(chrome.Viewport.Height);
                chrome.Focus().Should().BeTrue();
                review.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                review.KeyRelease(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                review.UpdateLayout();
                chrome.Offset.Y.Should().BeGreaterThan(0, "the exact destination and retention warning remain reachable, not truncated");
                chrome.Offset = new Vector(0, chrome.Extent.Height);
                review.UpdateLayout();
                chrome.Offset.Y.Should().BeApproximately(chrome.Extent.Height - chrome.Viewport.Height, 1);
                writer.Destination.Calls.Should().Be(0);
                audit.Events.Should().BeEmpty("layout and scrolling do not confirm disclosure");
                if (cancel)
                {
                    close.Focus().Should().BeTrue();
                    review.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    review.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                }
                else
                {
                    confirm.Focus().Should().BeTrue();
                    var invoke = ControlAutomationPeer.CreatePeerForElement(confirm)!.GetProvider<IInvokeProvider>()!;
                    if (fontSize == 14) { invoke.Invoke(); }
                    else
                    {
                        review.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                        review.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    }
                    await review.Operation;
                    review.Outcome.Should().Be(ExactAnswerExportOutcome.Saved);
                    confirm.IsEnabled.Should().BeFalse();
                    Action replay = invoke.Invoke;
                    replay.Should().Throw<ElementNotEnabledException>();
                    await review.Operation;
                    writer.Destination.Calls.Should().Be(1, "the accessible confirmation is still single-use");
                    text.Text.Should().BeEmpty();
                    review.UpdateLayout();
                    var status = review.GetVisualDescendants().OfType<TextBlock>()
                        .Single(label => string.Equals(label.Name, "ExportStatus", StringComparison.Ordinal));
                    status.Text.Should().Be(ExactAnswerExportWindow.Describe(ExactAnswerExportOutcome.Saved, writer.Destination.Path));
                    var outcome = status.GetVisualAncestors().OfType<ScrollViewer>().First();
                    outcome.Bounds.Height.Should().BeLessThanOrEqualTo(100);
                    outcome.Extent.Height.Should().BeGreaterThan(outcome.Viewport.Height);
                    close.Focus().Should().BeTrue();
                    var closePoint = close.TranslatePoint(default, review)!.Value;
                    (closePoint.Y + close.Bounds.Height).Should().BeLessThanOrEqualTo(review.ClientSize.Height,
                        "even a complete long outcome must not push cancellation off screen");
                    review.Close();
                }
                (await operation).Should().Contain(cancel ? "cancelled" : "verified");
                writer.Destination.Calls.Should().Be(cancel ? 0 : 1);
                writer.Destination.Disposals.Should().Be(1);
                string.Join("\n", review.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text))
                    .Should().NotContain(writer.Destination.Path).And.NotContain(source.Reference.ItemId.Value.ToString("D"));
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData("picker-cancel")]
    [InlineData("picker-private")]
    [InlineData("picker-source")]
    [InlineData("review-cancel")]
    [InlineData("review-source")]
    [InlineData("review-confirm")]
    public async Task SelectionAndReviewNeverBorrowWriteAuthority(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            var source = ExactAnswerExportWriterTests.Source();
            using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var writer = new Writer();
            var live = true;
            var current = source;
            var audit = new Audit();
            var controller = new ExactAnswerExportWindowController(writer, audit, NullLogger<ExactAnswerExportReview>.Instance, (_, _) =>
            {
                if (scenario is "picker-private") { live = false; }
                if (scenario is "picker-source") { current = ExactAnswerExportWriterTests.Source(); }
                return Task.FromResult<string?>(scenario is "picker-cancel" ? null : writer.Destination.Path);
            });
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = controller.ShowAsync(owner, source, () => live, _ => Task.FromResult(current), storage.Token);
                if (scenario is "picker-private" or "picker-source")
                {
                    var rejected = () => operation;
                    await rejected.Should().ThrowAsync<InvalidOperationException>();
                    writer.Destination.Calls.Should().Be(0);
                    owner.OwnedWindows.Should().BeEmpty();
                    return;
                }
                if (scenario is not "picker-cancel")
                {
                    var review = owner.OwnedWindows.OfType<ExactAnswerExportWindow>().Single();
                    review.GetVisualDescendants().OfType<TextBox>().Single().Text.Should().Be(source.Source);
                    writer.Destination.Calls.Should().Be(0);
                    audit.Events.Should().BeEmpty();
                    if (scenario is "review-cancel") { review.Close(); }
                    else
                    {
                        if (scenario is "review-source") { current = ExactAnswerExportWriterTests.Source(); }
                        review.GetVisualDescendants().OfType<Button>().Single(button =>
                            string.Equals(button.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await review.Operation;
                        review.Outcome.Should().Be(scenario is "review-confirm"
                            ? ExactAnswerExportOutcome.Saved : ExactAnswerExportOutcome.NotWritten);
                        review.Close();
                    }
                }
                var result = await operation;
                result.Should().Contain(scenario is "review-confirm" ? "verified" : scenario is "review-source" ? "failed" : "cancelled");
                writer.Destination.Calls.Should().Be(scenario is "review-confirm" ? 1 : 0);
                writer.Destination.Disposals.Should().Be(scenario is "picker-cancel" ? 0 : 1);
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public async Task ClosingAStagingReviewRetiresDisclosureAndJoinsTheWriterBeforeReleasingItsDestination()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            var source = ExactAnswerExportWriterTests.Source();
            using var host = HostActivity.BeginRoot(source.ConversationMessage!.Request, HostActivityLayer.Desktop, HostOperation.Request);
            var writer = new Writer();
            writer.Destination.Hold = true;
            var controller = new ExactAnswerExportWindowController(writer, new Audit(), NullLogger<ExactAnswerExportReview>.Instance,
                (_, _) => Task.FromResult<string?>(writer.Destination.Path));
            var owner = new Window();
            owner.Show();
            try
            {
                var operation = controller.ShowAsync(owner, source, () => true, _ => Task.FromResult(source), storage.Token);
                var review = owner.OwnedWindows.OfType<ExactAnswerExportWindow>().Single();
                var text = review.GetVisualDescendants().OfType<TextBox>().Single();
                review.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ConfirmExactAnswerExport", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await writer.Destination.Started.Task;
                review.Close();
                text.Text.Should().BeEmpty();
                operation.IsCompleted.Should().BeFalse("native close is not writer quiescence");
                writer.Destination.Disposals.Should().Be(0);
                writer.Destination.Release.SetResult();
                (await operation).Should().Contain("cancelled");
                string.Join("\n", review.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text))
                    .Should().NotContain(writer.Destination.Path).And.NotContain(source.Reference.ItemId.Value.ToString("D"));
                writer.Destination.Disposals.Should().Be(1);
                writer.Destination.Calls.Should().Be(1);
            }
            finally { writer.Destination.Release.TrySetResult(); owner.Close(); }
        });
    }

    private sealed class Writer : IExactAnswerExportWriter
    {
        internal Destination Destination { get; } = new();
        public Task<IExactAnswerExportDestination> SelectAsync(string path, CancellationToken token) =>
            Task.FromResult<IExactAnswerExportDestination>(Destination);
    }

    private sealed class Destination : IExactAnswerExportDestination
    {
        public string Path { get; set; } = System.IO.Path.Combine(OwnedStorageFixture.ScratchParent(), ".scratch", "orchestration",
            "exact-answer-export", "synthetic-not-written.md");
        internal bool Hold { get; set; }
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ExactAnswerExportOutcome> PublishAsync(AdmittedDetailContent source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            Calls++;
            await revalidate(token);
            Started.SetResult();
            if (Hold)
            {
                await Release.Task;
                token.IsCancellationRequested.Should().BeTrue();
                return ExactAnswerExportOutcome.Cancelled;
            }
            return ExactAnswerExportOutcome.Saved;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
