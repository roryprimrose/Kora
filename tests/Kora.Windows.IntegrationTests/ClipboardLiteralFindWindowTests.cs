using System.Diagnostics;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardLiteralFindWindowTests
{
    [Theory]
    [InlineData(640, 480)]
    [InlineData(760, 600)]
    public async Task CountAndOrdinalRemainReachableAtSmallViewportAndReverseCanonicalOverlaps(int width, int height)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected Find failure"));
            window.Show(Snapshot("aaaaaXaa"), UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                window.Width = width;
                window.Height = height;
                Query(window).Text = "aa";
                await Press(window, "Find first");
                Status(window).Text.Should().Contain("3 matches; match 1/3;").And.Contain("[0, 2)");
                await Press(window, "Find next");
                Status(window).Text.Should().Contain("3 matches; match 2/3;").And.Contain("[2, 4)");
                await Press(window, "Find next");
                Status(window).Text.Should().Contain("3 matches; match 3/3;").And.Contain("[6, 8)");
                await Press(window, "Find previous");
                Status(window).Text.Should().Contain("3 matches; match 2/3;").And.Contain("[2, 4)");
                var location = Status(window).TranslatePoint(default, window)!.Value;
                location.Y.Should().BeGreaterThanOrEqualTo(0);
                (location.Y + Status(window).Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                Source(window).GetVisualAncestors().OfType<ScrollViewer>().Single().Viewport.Height.Should().BeGreaterThan(0);
                Query(window).Text = "aa ";
                Status(window).Text.Should().BeNull();
                Match(window).Text.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task NativeFindPreservesSnapshotAndFocusesOnlyOneInertExactSpanWithoutReuseOrClipboardIo()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            var activities = 0;
            listener.ActivityStarted = _ => activities++;
            ActivitySource.AddActivityListener(listener);
            var reader = new Reader();
            using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            (await broker.CaptureAsync(() => true, CancellationToken.None)).Should().Be(ClipboardOutcome.Captured);
            var snapshot = broker.Current!;
            var failures = new List<Exception>();
            var window = new ClipboardPreviewWindow(failures.Add);
            window.Show(snapshot, UnexpectedMutation, UnexpectedMutation, () => ReferenceEquals(broker.Current, snapshot));
            try
            {
                var query = Query(window);
                query.Focus().Should().BeTrue();
                query.Text = "alpha";
                Match(window).IsVisible.Should().BeFalse();
                await Press(window, "Find first");
                Match(window).Text.Should().Be("alpha");
                Match(window).IsFocused.Should().BeTrue();
                Status(window).Text.Should().Contain("2 matches; match 1/2;").And.Contain("UTF-16 [6, 11)");
                var peer = ControlAutomationPeer.CreatePeerForElement(Match(window))!;
                peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
                peer.GetName().Should().Contain("Exact inert clipboard literal match").And.Contain("alpha");
                window.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle();
                Source(window).Text.Should().Be(snapshot.Text);
                await Press(window, "Find next");
                Status(window).Text.Should().Contain("2 matches; match 2/2;").And.Contain("UTF-16 [12, 17)");
                await Press(window, "Find next");
                Status(window).Text.Should().Contain("2 matches; match 2/2;").And.Contain("End of forward direction").And.NotContain("No literal match");
                Match(window).IsVisible.Should().BeFalse();
                await Press(window, "Find previous");
                Status(window).Text.Should().Contain("UTF-16 [6, 11)");
                await Press(window, "Find previous");
                Status(window).Text.Should().Contain("End of backward direction");
                var beforeEditing = activities;
                query.Text = "ALPHA";
                activities.Should().Be(beforeEditing);
                reader.Calls.Should().Be(1);
                Match(window).Text.Should().BeNull();
                Status(window).Text.Should().BeNull();
                Button(window, "Find next").IsEnabled.Should().BeFalse();
                await Press(window, "Find first");
                Match(window).Text.Should().Be("ALPHA");
                query.Text = " .*? ";
                await Press(window, "Find first");
                Match(window).Text.Should().Be(" .*? ");
                query.Text = "not found";
                await Press(window, "Find first");
                Status(window).Text.Should().Be("0 matches; No literal match in this exact clipboard snapshot.");
                Click(window, "Clear literal Find");
                query.Text.Should().BeNull();
                Status(window).Text.Should().BeNull();
                Match(window).Text.Should().BeNull();
                reader.Calls.Should().Be(1);
                broker.Current.Should().BeSameAs(snapshot);
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("characters")]
    [InlineData("bytes")]
    [InlineData("surrogate")]
    [InlineData("multiline")]
    [InlineData("format")]
    public async Task InvalidNativeQueriesAreVisiblyRejectedWithoutTruncationOrFallback(string invalid)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var failures = new List<Exception>();
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new ClipboardPreviewWindow(exception => { failures.Add(exception); failed.SetResult(); });
            window.Show(Snapshot("alpha"), UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                var query = invalid switch
                {
                    "empty" => "",
                    "characters" => new string('a', 257),
                    "bytes" => new string('界', 171),
                    "surrogate" => new string('\ud800', 1),
                    "multiline" => "alpha\nalpha",
                    _ => "\u202ealpha",
                };
                Query(window).Text = query;
                Button(window, "Find first").Command!.Execute(null);
                await failed.Task;
                Query(window).Text.Should().Be(query);
                Status(window).Text.Should().Contain("rejected").And.Contain("nothing was truncated");
                Status(window).Text.Should().NotContain("0 matches").And.NotContain("match 1/");
                Match(window).Text.Should().BeNull();
                Button(window, "Find next").IsEnabled.Should().BeFalse();
                failures.Should().ContainSingle().Which.Should().BeOfType<InvalidDataException>();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("replacement")]
    [InlineData("same-id-version")]
    [InlineData("revoke")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task ControllerFencesOldFindReuseRevokeAndClearCallbacksWithoutRetargetingNewCapture(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var original = Snapshot("alpha alpha");
            ClipboardSnapshot? current = original;
            var eligible = true;
            var views = new List<ClipboardPreviewWindow>();
            var reused = 0;
            var revoked = 0;
            using var controller = new ClipboardPreviewWindowController(() => eligible ? current : null,
                _ => { reused++; return Task.CompletedTask; }, () => current = null,
                () => { revoked++; current = null; return Task.CompletedTask; },
                () =>
                {
                    var view = new ClipboardPreviewWindow(_ => { });
                    views.Add(view);
                    return view;
                }, _ => throw new InvalidOperationException("Unexpected controller failure"));
            controller.Refresh();
            var old = views[0];
            var query = Query(old);
            var span = Match(old);
            var status = Status(old);
            var clear = Button(old, "Clear literal Find");
            var lateFind = (AsyncCommand)Button(old, "Find first").Command!;
            var lateReuse = (AsyncCommand)Button(old, "Reuse this exact snapshot ID").Command!;
            var lateRevoke = (AsyncCommand)Button(old, "Revoke and clear").Command!;
            query.Text = "alpha";
            await lateFind.ExecuteAsync();
            span.Text.Should().Be("alpha");
            status.Text.Should().Contain("2 matches; match 1/2;");
            switch (change)
            {
                case "privacy":
                case "owner":
                case "call": eligible = false; break;
                case "replacement": current = Snapshot("new source"); break;
                case "same-id-version": current = new(original.SourceId, original.SnapshotId, original.Request,
                    "new source", original.Version + 1, original.CapturedAt); break;
                case "revoke": current = null; break;
                case "close": old.Close(); break;
                default: controller.Dispose(); break;
            }
            // A gesture must fail closed before the existing privacy timer gets a turn.
            await lateFind.ExecuteAsync();
            span.Text.Should().BeNull();
            status.Text.Should().BeNull();
            query.Text.Should().BeNull();
            old.Content.Should().BeNull();
            if (change is "replacement" or "same-id-version") { current!.Text.Should().Be("new source"); }
            eligible = true;
            current = Snapshot("new source");
            controller.Refresh();
            await lateFind.ExecuteAsync();
            var reuse = () => lateReuse.ExecuteAsync();
            var revoke = () => lateRevoke.ExecuteAsync();
            await reuse.Should().ThrowAsync<InvalidOperationException>();
            await revoke.Should().ThrowAsync<InvalidOperationException>();
            clear.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            current!.Text.Should().Be("new source");
            reused.Should().Be(0);
            revoked.Should().Be(0);
            if (views.Count > 1)
            {
                Query(views[^1]).Text.Should().BeNull();
                Match(views[^1]).Text.Should().BeNull();
            }
        });
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task LossDuringFinalPublicationOrFocusClearsEveryVolatileReference(int loseAt)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var checks = 0;
            var failures = new List<Exception>();
            var window = new ClipboardPreviewWindow(failures.Add);
            window.Show(Snapshot("alpha"), UnexpectedMutation, UnexpectedMutation, () => ++checks < loseAt);
            var query = Query(window);
            var span = Match(window);
            var status = Status(window);
            query.Text = "alpha";
            checks = 0;
            var command = (AsyncCommand)Button(window, "Find first").Command!;
            await command.ExecuteAsync();
            window.Content.Should().BeNull();
            query.Text.Should().BeNull();
            span.Text.Should().BeNull();
            status.Text.Should().BeNull();
            await command.ExecuteAsync();
            window.Content.Should().BeNull();
            failures.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("clear")]
    [InlineData("focus-loss")]
    public async Task ReentrantLayoutEditsAndPrivateFocusLossCannotPublishAnOldMatch(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var eligible = true;
            var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected Find failure"));
            window.Show(Snapshot("alpha alpha"), UnexpectedMutation, UnexpectedMutation, () => eligible);
            var query = Query(window);
            var span = Match(window);
            var status = Status(window);
            query.Text = "alpha";
            query.Focus().Should().BeTrue();
            var changed = false;
            window.LayoutUpdated += (_, _) =>
            {
                if (changed || !span.IsVisible || change is "focus-loss") { return; }
                changed = true;
                if (change is "clear") { Click(window, "Clear literal Find"); }
                else { query.Text = "beta"; query.Text = "alpha"; }
            };
            span.GotFocus += (_, _) =>
            {
                if (change is "focus-loss") { eligible = false; }
            };
            try
            {
                await Press(window, "Find first");
                span.Text.Should().BeNull();
                span.IsVisible.Should().BeFalse();
                status.Text.Should().BeNull();
                if (change is "focus-loss")
                {
                    eligible.Should().BeFalse();
                    window.Content.Should().BeNull();
                    query.Text.Should().BeNull();
                }
                else
                {
                    changed.Should().BeTrue();
                    query.IsFocused.Should().BeTrue();
                    query.Text.Should().Be(change is "clear" ? null : "alpha");
                    Button(window, "Find next").IsEnabled.Should().BeFalse();
                }
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task KeyboardFindAndClipboardTransferGuardsNeverInvokeClipboardOrHostActions()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var failures = new List<Exception>();
            var window = new ClipboardPreviewWindow(failures.Add);
            window.Show(Snapshot("alpha alpha"), UnexpectedMutation, UnexpectedMutation, () => true);
            try
            {
                var query = Query(window);
                query.Text = "alpha";
                RaiseKey(window, Key.F, KeyModifiers.Control).Handled.Should().BeTrue();
                query.IsFocused.Should().BeTrue();
                RaiseKey(query, Key.Enter).Handled.Should().BeTrue();
                Match(window).Text.Should().Be("alpha");
                Status(window).Text.Should().Contain("2 matches; match 1/2;");
                RaiseKey(window, Key.F3).Handled.Should().BeTrue();
                Status(window).Text.Should().Contain("2 matches; match 2/2;").And.Contain("[6, 11)");
                RaiseKey(window, Key.F3, KeyModifiers.Shift).Handled.Should().BeTrue();
                Status(window).Text.Should().Contain("[0, 5)");
                foreach (var target in new Control[] { query, Match(window), Source(window) })
                {
                    foreach (var key in new[] { Avalonia.Input.Key.C, Avalonia.Input.Key.X, Avalonia.Input.Key.V, Avalonia.Input.Key.Insert })
                    {
                        RaiseKey(target, key, KeyModifiers.Control).Handled.Should().BeTrue();
                    }
                    RaiseKey(target, Key.Insert, KeyModifiers.Shift).Handled.Should().BeTrue();
                    RaiseKey(target, Key.Delete, KeyModifiers.Shift).Handled.Should().BeTrue();
                    RaiseKey(target, Key.Apps).Handled.Should().BeTrue();
                    RaiseKey(target, Key.F10, KeyModifiers.Shift).Handled.Should().BeTrue();
                    var context = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                    target.RaiseEvent(context);
                    context.Handled.Should().BeTrue();
                }
                foreach (var transfer in new[] { TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent, TextBox.PastingFromClipboardEvent })
                {
                    var args = new RoutedEventArgs(transfer);
                    query.RaiseEvent(args);
                    args.Handled.Should().BeTrue();
                }
                query.Text.Should().Be("alpha");
                Query(window).ContextMenu.Should().BeNull();
                RaiseKey(window, Key.Escape).Handled.Should().BeFalse();
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static Task UnexpectedMutation() => throw new InvalidOperationException("Find must not invoke reuse or revoke.");
    private static ClipboardSnapshot Snapshot(string text) => new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
    private static TextBox Query(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single();
    private static NamedTextBlock Match(Window window) => Text(window, "Exact inert clipboard literal match");
    private static NamedTextBlock Source(Window window) => Text(window, "Clipboard snapshot plain text content");
    private static NamedTextBlock Status(Window window) => Text(window, "Clipboard literal Find outcome and exact UTF-16 location");
    private static NamedTextBlock Text(Window window, string name) => window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
        string.Equals(AutomationProperties.GetName(block), name, StringComparison.Ordinal));
    private static Button Button(Window window, string caption) => window.GetVisualDescendants().OfType<Button>().Single(button =>
        string.Equals(button.Content as string, caption, StringComparison.Ordinal));
    private static Task Press(Window window, string caption) => ((AsyncCommand)Button(window, caption).Command!).ExecuteAsync();
    private static void Click(Window window, string caption) =>
        Button(window, caption).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static KeyEventArgs RaiseKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        target.RaiseEvent(args);
        return args;
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        public int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls != 1) { throw new InvalidOperationException("Only the explicit initial capture may read."); }
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, "α\r\n🙂 alpha alpha ALPHA .*?  ", 1));
        }
    }
}
