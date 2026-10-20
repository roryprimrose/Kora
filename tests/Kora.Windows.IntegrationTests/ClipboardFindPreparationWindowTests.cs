using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardFindPreparationWindowTests
{
    [Fact]
    public async Task PreparationOpensOrFocusesSameNativeSnapshotAndOnlySeparateFindProducesCountAndOrdinal()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var snapshot = Snapshot("  MiXeD e\u0301界🙂 .*?  /  MiXeD e\u0301界🙂 .*?  ");
            ClipboardSnapshot? current = snapshot;
            var views = new List<ClipboardPreviewWindow>();
            var failures = new List<string>();
            var mutations = 0;
            using var controller = new ClipboardPreviewWindowController(() => current,
                _ => { mutations++; return Task.CompletedTask; }, () => current = null,
                () => { mutations++; return Task.CompletedTask; },
                () => { var view = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected native failure")); views.Add(view); return view; },
                failures.Add);
            const string literal = "  MiXeD e\u0301界🙂 .*?  ";
            using var request = new PreparedClipboardFindEventArgs(snapshot, literal, () => ReferenceEquals(current, snapshot));
            controller.OnFindPreparationRequested(null, request);
            request.IsPrepared.Should().BeTrue();
            var window = views.Should().ContainSingle().Which;
            var query = Query(window);
            window.UpdateLayout();
            query.Text.Should().Be(literal);
            query.IsFocused.Should().BeTrue();
            Status(window).Text.Should().Contain("PREPARED / REVIEW NEEDED").And.NotContain("matches;");
            Match(window).Text.Should().BeNull();
            Button(window, "Find next").IsEnabled.Should().BeFalse();
            current.Should().BeSameAs(snapshot);
            mutations.Should().Be(0);
            await Press(window, "Find first");
            Status(window).Text.Should().Contain("2 matches; match 1/2;");
            await Press(window, "Find next");
            Status(window).Text.Should().Contain("2 matches; match 2/2;");
            await Press(window, "Find next");
            Status(window).Text.Should().Contain("2 matches; match 2/2;").And.Contain("no wrap");
            using var repeat = new PreparedClipboardFindEventArgs(snapshot, literal, () => ReferenceEquals(current, snapshot));
            controller.OnFindPreparationRequested(null, repeat);
            repeat.IsPrepared.Should().BeTrue();
            views.Should().ContainSingle();
            query.Text.Should().Be(literal);
            query.IsFocused.Should().BeTrue();
            Match(window).Text.Should().BeNull();
            Status(window).Text.Should().NotContain("matches;");
            Button(window, "Find previous").IsEnabled.Should().BeFalse();
            failures.Should().BeEmpty();
            mutations.Should().Be(0);
            window.ClearAndClose();
            query.Text.Should().BeNull();
            window.PrepareFind(snapshot, literal, () => true).Should().BeFalse();
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("replacement")]
    [InlineData("same-id")]
    [InlineData("admission")]
    [InlineData("creation-source")]
    [InlineData("creation-admission")]
    [InlineData("disposed")]
    public async Task DeferredOpeningNeverRetargetsAnotherSnapshotOrChangedAdmission(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var snapshot = Snapshot("PRIVATE");
            ClipboardSnapshot? current = snapshot;
            var eligible = true;
            var views = new List<ClipboardPreviewWindow>();
            using var controller = new ClipboardPreviewWindowController(() => current, _ => Task.CompletedTask,
                () => current = null, () => Task.CompletedTask, () =>
                {
                    var view = new ClipboardPreviewWindow(_ => { });
                    views.Add(view);
                    if (change is "creation-source") { current = Snapshot("replacement"); }
                    if (change is "creation-admission") { eligible = false; }
                    return view;
                }, _ => throw new InvalidOperationException("Unexpected preparation failure"));
            using var request = new PreparedClipboardFindEventArgs(snapshot, "PRIVATE", () => eligible);
            if (change is "missing") { current = null; }
            if (change is "replacement") { current = Snapshot("replacement"); }
            if (change is "same-id") { current = new(snapshot.SourceId, snapshot.SnapshotId,
                snapshot.Request, snapshot.Text, snapshot.Version, snapshot.CapturedAt); }
            if (change is "admission") { eligible = false; }
            if (change is "disposed") { controller.Dispose(); }
            controller.OnFindPreparationRequested(null, request);
            request.IsPrepared.Should().BeFalse();
            views.Should().HaveCount(change.StartsWith("creation", StringComparison.Ordinal) ? 1 : 0);
            views.Should().OnlyContain(view => view.Content == null && !view.IsVisible);
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("admission")]
    [InlineData("invalid")]
    [InlineData("focus")]
    public async Task NativePreparationRevalidatesExactReferenceAndAdmissionThroughFocus(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var snapshot = Snapshot("PRIVATE");
            var eligible = true;
            var window = new ClipboardPreviewWindow(_ => { });
            window.PrepareFind(snapshot, "PRIVATE", () => true).Should().BeFalse();
            window.Show(snapshot, () => Task.CompletedTask, () => Task.CompletedTask, () => true);
            try
            {
                var query = Query(window);
                query.GotFocus += (_, _) =>
                {
                    if (change is "focus") { eligible = false; }
                };
                var target = change is "source" ? Snapshot(snapshot.Text) : snapshot;
                if (change is "admission") { eligible = false; }
                window.PrepareFind(target, change is "invalid" ? "PRIVATE\u202e" : "PRIVATE", () => eligible).Should().BeFalse();
                query.Text.Should().BeNullOrEmpty();
                Match(window).Text.Should().BeNull();
                Status(window).Text.Should().BeNullOrEmpty();
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task LostAdmissionDuringOpeningClosesNativeSourceAndReportsPreparationFailureWithoutRawException()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var snapshot = Snapshot("PRIVATE");
            var failures = new List<string>();
            using var controller = new ClipboardPreviewWindowController(() => snapshot, _ => Task.CompletedTask,
                () => { }, () => Task.CompletedTask, () => throw new InvalidOperationException("PRIVATE native failure"), failures.Add);
            using var request = new PreparedClipboardFindEventArgs(snapshot, "PRIVATE", () => true);
            controller.OnFindPreparationRequested(null, request);
            request.IsPrepared.Should().BeFalse();
            failures.Should().ContainSingle().Which.Should().Contain("unavailable").And.NotContain("PRIVATE");
        });
    }

    private static ClipboardSnapshot Snapshot(string text) => new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);
    private static TextBox Query(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single();
    private static NamedTextBlock Status(Window window) => Text(window, "Clipboard literal Find outcome and exact UTF-16 location");
    private static NamedTextBlock Match(Window window) => Text(window, "Exact inert clipboard literal match");
    private static NamedTextBlock Text(Window window, string name) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(control => string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
    private static Button Button(Window window, string caption) => window.GetVisualDescendants().OfType<Button>()
        .Single(control => string.Equals(control.Content as string, caption, StringComparison.Ordinal));
    private static Task Press(Window window, string caption) => ((AsyncCommand)Button(window, caption).Command!).ExecuteAsync();
}
