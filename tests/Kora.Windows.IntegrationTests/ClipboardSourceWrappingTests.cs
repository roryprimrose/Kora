using System.Diagnostics;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
public sealed class ClipboardSourceWrappingTests
{
    private static readonly int[] Widths = [640, 1000, 760];
    private static readonly bool[] WrappingDirections = [false, true, false, true];
    private static readonly bool[] FindWrappingDirections = [true, false, true];

    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ActualDefaultAndBothDirectionsFitResizedViewportWithReaderScaleAndExactText(int percent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath());
            await fixture.InitializeAsync();
            var snapshot = Snapshot("α\t🙂 needle\r\n" + new string('x', 4000) + "\r\nneedle\tend");
            var originalBytes = snapshot.GetUtf8Bytes();
            var failures = new List<Exception>();
            var window = new ClipboardPreviewWindow(failures.Add, fixture.Main);
            window.Show(snapshot, Unexpected, Unexpected, () => true);
            try
            {
                var source = Source(window);
                var toggle = Toggle(window);
                var viewport = source.GetVisualAncestors().OfType<ScrollViewer>().Single();
                var baseline = source.FontSize;
                toggle.IsChecked.Should().BeFalse();
                source.TextWrapping.Should().Be(new NamedTextBlock().TextWrapping).And.Be(TextWrapping.NoWrap);
                var peer = ControlAutomationPeer.CreatePeerForElement(toggle)!;
                peer.GetName().Should().Be("Wrap snapshot text");
                peer.GetAutomationControlType().Should().Be(AutomationControlType.CheckBox);
                source.Focusable.Should().BeFalse();
                window.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle();
                fixture.Main.ReaderTextScalePercent = percent;
                await fixture.Main.AppearanceUpdate;
                foreach (var width in Widths)
                {
                    window.Width = width;
                    window.Height = 600;
                    foreach (var wrap in WrappingDirections)
                    {
                        toggle.IsChecked = wrap;
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        source.FontSize.Should().Be(baseline * percent / 100d);
                        source.TextWrapping.Should().Be(wrap ? TextWrapping.Wrap : TextWrapping.NoWrap);
                        viewport.HorizontalScrollBarVisibility.Should().Be(wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
                        viewport.Viewport.Height.Should().BeGreaterThan(0);
                        if (wrap)
                        {
                            source.Bounds.Width.Should().BeLessThanOrEqualTo(viewport.Viewport.Width + 1);
                            viewport.Extent.Width.Should().BeLessThanOrEqualTo(viewport.Viewport.Width + 1);
                            viewport.Extent.Height.Should().BeGreaterThan(viewport.Viewport.Height);
                        }
                        else
                        {
                            viewport.Extent.Width.Should().BeGreaterThan(viewport.Viewport.Width);
                            viewport.Offset = new Avalonia.Vector(150, 0);
                            viewport.Offset.X.Should().BeGreaterThan(0);
                        }
                        source.Text.Should().BeSameAs(snapshot.Text);
                        snapshot.GetUtf8Bytes().Should().Equal(originalBytes);
                        var point = toggle.TranslatePoint(default, window)!.Value;
                        point.Y.Should().BeGreaterThanOrEqualTo(0);
                        (point.Y + toggle.Bounds.Height).Should().BeLessThanOrEqualTo(window.ClientSize.Height);
                    }
                }
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task ToggleDoesNotReadRenewFindParseExplainOrChangeSnapshotAndTransferGuardsRemainInert()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var events = 0;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = _ => events++,
            };
            ActivitySource.AddActivityListener(listener);
            var reader = new Reader();
            using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
            using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
            await broker.CaptureAsync(() => true, CancellationToken.None);
            var snapshot = broker.Current!;
            var failures = new List<Exception>();
            var window = new ClipboardPreviewWindow(failures.Add);
            window.Show(snapshot, Unexpected, Unexpected, () => ReferenceEquals(broker.Current, snapshot), Unexpected);
            try
            {
                Query(window).Text = "needle";
                await Press(window, "Find first");
                var span = Text(window, "Exact inert clipboard literal match");
                var status = Text(window, "Clipboard literal Find outcome and exact UTF-16 location");
                var firstStatus = status.Text;
                var answer = Text(window, "Volatile local clipboard explanation; untrusted model response");
                window.SetExplanation("unchanged local answer");
                var states = window.GetVisualDescendants().OfType<NamedTextBlock>()
                    .Where(control => !ReferenceEquals(control, Source(window)))
                    .Select(control => (Control: control, control.Text, control.TextWrapping)).ToArray();
                var toggle = Toggle(window);
                events = 0;
                foreach (var wrap in FindWrappingDirections)
                {
                    toggle.Focus().Should().BeTrue();
                    toggle.IsChecked = wrap;
                    toggle.IsFocused.Should().BeTrue();
                    span.IsFocused.Should().BeFalse();
                    span.Text.Should().Be("needle");
                    Query(window).Text.Should().Be("needle");
                    status.Text.Should().Be(firstStatus);
                    foreach (var state in states)
                    {
                        state.Control.Text.Should().Be(state.Text);
                        state.Control.TextWrapping.Should().Be(state.TextWrapping);
                    }
                    broker.Current.Should().BeSameAs(snapshot);
                    Source(window).Text.Should().BeSameAs(snapshot.Text);
                    reader.Calls.Should().Be(1);
                    events.Should().Be(0);
                    foreach (var target in new Control[] { toggle, Source(window), Query(window), span })
                    {
                        foreach (var key in new[] { Key.C, Key.X, Key.V, Key.Insert })
                        {
                            KeyDown(target, key, KeyModifiers.Control).Handled.Should().BeTrue();
                        }
                        KeyDown(target, Key.Insert, KeyModifiers.Shift).Handled.Should().BeTrue();
                        KeyDown(target, Key.Delete, KeyModifiers.Shift).Handled.Should().BeTrue();
                        KeyDown(target, Key.Apps, KeyModifiers.None).Handled.Should().BeTrue();
                        KeyDown(target, Key.F10, KeyModifiers.Shift).Handled.Should().BeTrue();
                        var context = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                        target.RaiseEvent(context);
                        context.Handled.Should().BeTrue();
                    }
                }
                answer.Text.Should().Be("unchanged local answer");
                await Press(window, "Find next");
                status.Text.Should().Contain("2 matches; match 2/2;");
                await Press(window, "Find previous");
                status.Text.Should().Be(firstStatus);
                Query(window).Text = "NEEDLE";
                await Press(window, "Find first");
                status.Text.Should().StartWith("0 matches;");
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("revoke")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("call")]
    [InlineData("close")]
    [InlineData("dispose")]
    public async Task RetirementResetsChoiceAndContentAndLateControlCannotTouchReplacement(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            ClipboardSnapshot? current = Snapshot("old needle");
            var eligible = true;
            var views = new List<ClipboardPreviewWindow>();
            using var controller = new ClipboardPreviewWindowController(() => eligible ? current : null,
                _ => Unexpected(), () => current = null, Unexpected, () =>
                {
                    var view = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected native failure"));
                    views.Add(view);
                    return view;
                }, _ => throw new InvalidOperationException("Unexpected controller failure"));
            controller.Refresh();
            var old = views[0];
            var source = Source(old);
            var toggle = Toggle(old);
            toggle.IsChecked = true;
            switch (change)
            {
                case "replacement": current = Snapshot("replacement"); break;
                case "revoke": current = null; break;
                case "close": old.Close(); break;
                case "dispose": controller.Dispose(); break;
                default: eligible = false; break;
            }
            // The gesture must fence loss before the controller timer refreshes.
            toggle.IsChecked = false;
            old.Content.Should().BeNull();
            source.Text.Should().BeNull();
            source.TextWrapping.Should().Be(TextWrapping.NoWrap);
            toggle.IsChecked.Should().BeFalse();
            eligible = true;
            current = Snapshot("replacement");
            controller.Refresh();
            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            old.Content.Should().BeNull();
            if (change is not "dispose")
            {
                var replacement = views[^1];
                Toggle(replacement).IsChecked.Should().BeFalse();
                Source(replacement).TextWrapping.Should().Be(TextWrapping.NoWrap);
                Source(replacement).Text.Should().Be("replacement");
                Query(replacement).Text.Should().BeNull();
            }
        });
    }

    [Theory]
    [InlineData("property")]
    [InlineData("layout")]
    [InlineData("reentrant-toggle")]
    [InlineData("failure")]
    public async Task ReentrantNativePublicationCannotResurrectPrivateBodyOrOverwriteNewerChoice(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var current = true;
            var failures = new List<Exception>();
            var window = new ClipboardPreviewWindow(failures.Add);
            window.Show(Snapshot("needle " + new string('x', 3000)), Unexpected, Unexpected, () => current);
            var source = Source(window);
            var toggle = Toggle(window);
            var changed = false;
            source.PropertyChanged += (_, args) =>
            {
                if (changed || args.Property != TextBlock.TextWrappingProperty || change is "layout") { return; }
                changed = true;
                if (change is "failure") { throw new InvalidOperationException("Synthetic native failure"); }
                if (change is "reentrant-toggle") { toggle.IsChecked = false; }
                else { current = false; }
            };
            window.LayoutUpdated += (_, _) =>
            {
                if (changed || change is not "layout") { return; }
                changed = true;
                current = false;
            };
            try
            {
                toggle.IsChecked = true;
                Dispatcher.UIThread.RunJobs();
                changed.Should().BeTrue();
                if (change is "reentrant-toggle")
                {
                    toggle.IsChecked.Should().BeFalse();
                    source.TextWrapping.Should().Be(TextWrapping.NoWrap);
                    source.GetVisualAncestors().OfType<ScrollViewer>().Single()
                        .HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
                }
                else
                {
                    window.Content.Should().BeNull();
                    source.Text.Should().BeNull();
                    toggle.IsChecked.Should().BeFalse();
                    toggle.IsChecked = true;
                    source.Text.Should().BeNull();
                }
                failures.Should().HaveCount(change is "failure" ? 1 : 0);
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static Task Unexpected() => throw new InvalidOperationException("Presentation must not dispatch host actions.");
    private static ClipboardSnapshot Snapshot(string text) => new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
    private static TextBox Query(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single();
    private static NamedTextBlock Source(Window window) => Text(window, "Clipboard snapshot plain text content");
    private static NamedTextBlock Text(Window window, string name) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(control => string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
    private static CheckBox Toggle(Window window) => window.GetVisualDescendants().OfType<CheckBox>()
        .Single(control => AutomationProperties.GetName(control) is "Wrap snapshot text");
    private static Task Press(Window window, string caption) => ((AsyncCommand)window.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, caption)).Command!).ExecuteAsync();
    private static KeyEventArgs KeyDown(Control target, Key key, KeyModifiers modifiers)
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
            if (Calls != 1) { throw new InvalidOperationException("Only explicit capture may read."); }
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, "α\t🙂 needle\r\nneedle", 1));
        }
    }
}
