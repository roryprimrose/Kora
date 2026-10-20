using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardInspectBase64Tests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public ClipboardInspectBase64Tests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task InspectionTraceTerminatesWithOriginalParentAndContentFreeTags()
    {
        var stopped = new List<Activity>();
        listener.ActivityStopped = stopped.Add;
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var broker = new ClipboardSnapshotBroker(new Reader(), TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var snapshot = broker.Current!;
        stopped.Clear();
        new ClipboardInspectBase64(broker).Execute(snapshot, () => true, CancellationToken.None);
        var inspection = stopped.Should().ContainSingle().Subject;
        inspection.TraceId.Should().Be(host.Activity!.TraceId);
        inspection.ParentId.Should().Be(host.Activity.Id);
        inspection.Status.Should().Be(ActivityStatusCode.Ok);
        inspection.IdFormat.Should().Be(ActivityIdFormat.W3C);
        inspection.TagObjects.Should().NotContain(tag => Equals(tag.Value, snapshot.Text)
            || Equals(tag.Value, snapshot.SourceId) || Equals(tag.Value, snapshot.SnapshotId));
        Activity.Current.Should().BeSameAs(host.Activity);
        stopped.Clear();
        var cancelled = () => new ClipboardInspectBase64(broker).Execute(snapshot, () => false, CancellationToken.None);
        cancelled.Should().Throw<OperationCanceledException>();
        stopped.Should().ContainSingle().Which.GetTagItem("kora.outcome").Should().Be("Cancelled");
    }

    [Fact]
    public async Task ExactSourceOnlyNoRecaptureReuseRevocationOrRenewal()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var reader = new Reader();
        using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        var snapshot = broker.Current!;
        var action = new ClipboardInspectBase64(broker);
        action.Execute(snapshot, () => true, CancellationToken.None).Should().Be(new ClipboardBase64Inspection.AdmittedProfile(1));
        reader.Text = "AB==";
        broker.Current.Should().BeSameAs(snapshot);
        snapshot.Text.Should().Be("AA==");
        reader.Calls.Should().Be(1);
        var clone = new ClipboardSnapshot(snapshot.SourceId, snapshot.SnapshotId, snapshot.Request, snapshot.Text, snapshot.Version, snapshot.CapturedAt);
        var copiedIdentity = () => action.Execute(clone, () => true, CancellationToken.None);
        copiedIdentity.Should().Throw<OperationCanceledException>();
        broker.Clear();
        var missing = () => action.Execute(snapshot, () => true, CancellationToken.None);
        missing.Should().Throw<OperationCanceledException>();
        await broker.CaptureAsync(() => true, CancellationToken.None);
        action.Execute(broker.Current!, () => true, CancellationToken.None).Should()
            .Be(new ClipboardBase64Inspection.InvalidProfile(ClipboardBase64InvalidReason.NonCanonicalUnusedBits, 1));
        var replacement = () => action.Execute(snapshot, () => true, CancellationToken.None);
        replacement.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("original-denied")]
    [InlineData("cancelled")]
    [InlineData("revoked")]
    [InlineData("disposed")]
    [InlineData("failure")]
    public async Task RevalidateOriginalAndCurrentAdmissionBeforeReturningAnything(string change)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var reader = new Reader();
        var originalEligible = true;
        using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        await broker.CaptureAsync(() => originalEligible, CancellationToken.None);
        var snapshot = broker.Current!;
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        bool Eligible()
        {
            if (++checks == 2)
            {
                switch (change)
                {
                    case "denied": return false;
                    case "original-denied": originalEligible = false; break;
                    case "cancelled": cancellation.Cancel(); break;
                    case "revoked": broker.Clear(); break;
                    case "disposed": broker.Dispose(); break;
                    case "failure": throw new InvalidOperationException("synthetic");
                }
            }
            return true;
        }
        var execute = () => new ClipboardInspectBase64(broker).Execute(snapshot, Eligible, cancellation.Token);
        if (change is "failure") { execute.Should().Throw<InvalidOperationException>(); }
        else { execute.Should().Throw<OperationCanceledException>(); }
        reader.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task OnlyNativeGestureCanInspect(RequestOrigin origin)
    {
        using var capture = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var broker = new ClipboardSnapshotBroker(new Reader(), TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        await broker.CaptureAsync(() => true, CancellationToken.None);
        using var other = HostActivity.BeginRoot(HostRequest.Create(origin),
            HostActivityLayer.Application, HostOperation.Request);
        var execute = () => new ClipboardInspectBase64(broker).Execute(broker.Current!, () => true, CancellationToken.None);
        execute.Should().Throw<OperationCanceledException>();
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        public string Text { get; set; } = "AA==";
        public int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, Text, 1));
        }
    }
}
