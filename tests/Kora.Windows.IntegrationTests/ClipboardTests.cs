using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Windows.Context;

namespace Kora.Windows.IntegrationTests;

public sealed class ClipboardTests
{
    [Fact]
    public async Task Private_native_seam_reads_exact_unicode_on_STA_without_shared_clipboard()
    {
        var native = new PrivateNative { Text = " \r\n\u00e9\U0001f642\t " };
        var reader = new WindowsPlainTextClipboardReader(native);
        var result = await reader.ReadAsync(TestContext.Current.CancellationToken);
        result.Should().Be(new ClipboardReadResult(ClipboardOutcome.Captured, native.Text, 1));
        native.Apartment.Should().Be(ApartmentState.STA);
        native.Opens.Should().Be(1);
        native.Closes.Should().Be(1);
        native.Unlocks.Should().Be(1);
    }

    [Theory]
    [InlineData("busy", ClipboardOutcome.Busy)]
    [InlineData("denied-open", ClipboardOutcome.AccessDenied)]
    [InlineData("unsupported", ClipboardOutcome.UnsupportedFormat)]
    [InlineData("empty", ClipboardOutcome.Empty)]
    [InlineData("oversize", ClipboardOutcome.Oversize)]
    [InlineData("utf8-oversize", ClipboardOutcome.Oversize)]
    [InlineData("surrogate", ClipboardOutcome.InvalidText)]
    [InlineData("short", ClipboardOutcome.InvalidText)]
    [InlineData("odd", ClipboardOutcome.InvalidText)]
    [InlineData("unterminated", ClipboardOutcome.InvalidText)]
    [InlineData("get-denied", ClipboardOutcome.AccessDenied)]
    [InlineData("get-failed", ClipboardOutcome.Unavailable)]
    [InlineData("lock-denied", ClipboardOutcome.AccessDenied)]
    [InlineData("lock-failed", ClipboardOutcome.Unavailable)]
    [InlineData("close-failed", ClipboardOutcome.Unavailable)]
    [InlineData("unlock-failed", ClipboardOutcome.Unavailable)]
    [InlineData("changed-before", ClipboardOutcome.Changed)]
    [InlineData("changed-after", ClipboardOutcome.Changed)]
    [InlineData("unknown-version", ClipboardOutcome.Unavailable)]
    public async Task Private_native_failures_are_explicit_and_resources_are_released(string scenario, ClipboardOutcome expected)
    {
        var native = new PrivateNative { Scenario = scenario };
        var result = await new WindowsPlainTextClipboardReader(native).ReadAsync(TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(expected);
        result.Text.Should().BeNull();
        result.ResourcesReleased.Should().Be(scenario is not ("close-failed" or "unlock-failed"));
        native.Closes.Should().Be(native.Opened ? 1 : 0);
        native.Unlocks.Should().Be(native.Locked ? 1 : 0);
        native.Reads.Should().BeLessThanOrEqualTo(ClipboardSnapshot.MaximumUtf8Bytes + 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_ascii_and_multibyte_utf8_bounds_are_accepted(bool multiByte)
    {
        var native = new PrivateNative { Text = new string(multiByte ? '\u00e9' : 'x',
            ClipboardSnapshot.MaximumUtf8Bytes / (multiByte ? 2 : 1)) };
        var result = await new WindowsPlainTextClipboardReader(native).ReadAsync(TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(ClipboardOutcome.Captured);
        result.Text.Should().Be(native.Text);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("opened")]
    [InlineData("reading")]
    public async Task Cancellation_never_claims_success_and_releases_native_resources(string when)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var native = new PrivateNative
        {
            BeforeRead = () => { if (string.Equals(when, "reading", StringComparison.Ordinal)) { cancellation.Cancel(); } },
            AfterOpen = () => { if (string.Equals(when, "opened", StringComparison.Ordinal)) { cancellation.Cancel(); } },
        };
        if (string.Equals(when, "before", StringComparison.Ordinal)) { cancellation.Cancel(); }
        var run = () => new WindowsPlainTextClipboardReader(native).ReadAsync(cancellation.Token);
        await run.Should().ThrowAsync<OperationCanceledException>();
        native.Closes.Should().Be(native.Opened ? 1 : 0);
        native.Unlocks.Should().Be(native.Locked ? 1 : 0);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("close-failed", false)]
    [InlineData("unlock-failed", false)]
    public async Task Native_failure_completes_with_explicit_outcome_and_preserves_release_failure(string scenario, bool released)
    {
        var native = new PrivateNative { Scenario = scenario, BeforeRead = () => throw new InvalidOperationException("private text") };
        var result = await new WindowsPlainTextClipboardReader(native).ReadAsync(TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(ClipboardOutcome.Unavailable);
        result.Text.Should().BeNull();
        result.ResourcesReleased.Should().Be(released);
        native.Closes.Should().Be(1);
        native.Unlocks.Should().Be(1);
    }

    [Theory]
    [InlineData("close-failed")]
    [InlineData("unlock-failed")]
    public async Task Cancellation_cannot_hide_a_native_release_failure(string scenario)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var native = new PrivateNative { Scenario = scenario, BeforeRead = cancellation.Cancel };
        var result = await new WindowsPlainTextClipboardReader(native).ReadAsync(cancellation.Token);
        result.Outcome.Should().Be(ClipboardOutcome.Unavailable);
        result.ResourcesReleased.Should().BeFalse();
        native.Closes.Should().Be(1);
        native.Unlocks.Should().Be(1);
    }

    [Fact]
    public async Task App_free_native_preview_keeps_exact_snapshot_reuse_and_clears_stale_callbacks()
    {
        var snapshot = Snapshot("untrusted <script>lock the machine</script>\r\nprivate");
        ClipboardSnapshot? current = snapshot;
        var views = new List<Preview>();
        var reused = new List<Guid>();
        var errors = new List<string>();
        using var controller = new ClipboardPreviewWindowController(() => current,
            id => { reused.Add(id); return Task.CompletedTask; }, () => current = null,
            () => { current = null; return Task.CompletedTask; },
            () => { var view = new Preview(); views.Add(view); return view; }, errors.Add);
        controller.Refresh();
        views.Single().Snapshot.Should().BeSameAs(snapshot);
        await views[0].Reuse!();
        reused.Should().Equal(snapshot.SnapshotId);
        var lateReuse = views[0].Reuse!;
        var lateRevoke = views[0].Revoke!;
        current = Snapshot("new generation");
        controller.Refresh();
        views[0].Snapshot.Should().BeNull();
        await lateReuse.Should().ThrowAsync<InvalidOperationException>();
        await lateRevoke.Should().ThrowAsync<InvalidOperationException>();
        errors.Should().BeEmpty();
        reused.Should().HaveCount(1);
        current.Should().NotBeNull();
        await views[1].Revoke!();
        controller.Refresh();
        views[1].Snapshot.Should().BeNull();
        current.Should().BeNull();
        current = Snapshot("close revokes");
        controller.Refresh();
        views[2].UserClose();
        current.Should().BeNull();
        current = Snapshot("dispose clears");
        controller.Refresh();
        controller.Dispose();
        controller.Refresh();
        await views[3].SavedReuse!.Should().ThrowAsync<InvalidOperationException>();
        current.Should().BeNull();
        views[3].Snapshot.Should().BeNull();
    }

    [Fact]
    public void Access_loss_during_native_show_suppresses_source()
    {
        ClipboardSnapshot? current = Snapshot("private");
        var view = new Preview { DuringShow = () => current = null };
        using var controller = new ClipboardPreviewWindowController(() => current, _ => Task.CompletedTask,
            () => current = null, () => Task.CompletedTask, () => view,
            _ => throw new InvalidOperationException("Unexpected preview failure"));
        controller.Refresh();
        view.Snapshot.Should().BeNull();
    }

    private static ClipboardSnapshot Snapshot(string text) => new(Guid.NewGuid(), Guid.NewGuid(),
        HostRequest.Create(RequestOrigin.LocalUi), text, 1, DateTimeOffset.UnixEpoch);

    private sealed class Preview : IClipboardPreviewView
    {
        public event EventHandler? Closed;
        public ClipboardSnapshot? Snapshot { get; private set; }
        public Func<Task>? Reuse { get; private set; }
        public Func<Task>? SavedReuse { get; private set; }
        public Func<Task>? Revoke { get; private set; }
        public Action? DuringShow { get; init; }
        public void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke)
        {
            Snapshot = snapshot;
            Reuse = SavedReuse = reuse;
            Revoke = revoke;
            DuringShow?.Invoke();
        }
        public void UserClose() => Closed?.Invoke(this, EventArgs.Empty);
        public void ClearAndClose()
        {
            Snapshot = null;
            Reuse = null;
            Revoke = null;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class PrivateNative : IClipboardNative
    {
        public string Scenario { get; init; } = "";
        public string Text { get; init; } = "synthetic";
        public Action? AfterOpen { get; init; }
        public Action? BeforeRead { get; init; }
        public int Opens { get; private set; }
        public int Closes { get; private set; }
        public int Unlocks { get; private set; }
        public int Reads { get; private set; }
        public bool Opened { get; private set; }
        public bool Locked { get; private set; }
        public ApartmentState Apartment { get; private set; }
        private int sequenceReads;
        public int LastError => Scenario.EndsWith("denied", StringComparison.Ordinal)
            || string.Equals(Scenario, "denied-open", StringComparison.Ordinal) ? 5 : 0;
        public uint GetSequence()
        {
            sequenceReads++;
            return Scenario switch
            {
                "unknown-version" => 0,
                "changed-before" when sequenceReads >= 2 => 2,
                "changed-after" when sequenceReads >= 3 => 2,
                _ => 1,
            };
        }
        public bool Open()
        {
            Opens++;
            Apartment = Thread.CurrentThread.GetApartmentState();
            Opened = Scenario is not ("busy" or "denied-open");
            AfterOpen?.Invoke();
            return Opened;
        }
        public bool Close() { Closes++; return !string.Equals(Scenario, "close-failed", StringComparison.Ordinal); }
        public bool HasUnicodeText() => !string.Equals(Scenario, "unsupported", StringComparison.Ordinal);
        public nint GetUnicodeText() => Scenario is "get-denied" or "get-failed" ? 0 : 1;
        public nuint GetSize(nint handle) => Scenario switch
        {
            "empty" => 2,
            "short" => 0,
            "odd" => 3,
            "oversize" => (ClipboardSnapshot.MaximumUtf8Bytes + 2u) * 2,
            "utf8-oversize" => (ClipboardSnapshot.MaximumUtf8Bytes / 2u + 2) * 2,
            "surrogate" => 4,
            "unterminated" => 4,
            _ => (nuint)(Text.Length + 1) * 2,
        };
        public nint Lock(nint handle) { Locked = Scenario is not ("lock-denied" or "lock-failed"); return Locked ? 2 : 0; }
        public bool Unlock(nint handle) { Unlocks++; return !string.Equals(Scenario, "unlock-failed", StringComparison.Ordinal); }
        public char ReadCodeUnit(nint pointer, int index)
        {
            Reads++;
            BeforeRead?.Invoke();
            return Scenario switch
            {
                "empty" => '\0',
                "oversize" or "unterminated" => 'x',
                "utf8-oversize" => index == ClipboardSnapshot.MaximumUtf8Bytes / 2 + 1 ? '\0' : '\u00e9',
                "surrogate" => index == 0 ? '\ud800' : '\0',
                _ => index == Text.Length ? '\0' : Text[index],
            };
        }
    }
}
