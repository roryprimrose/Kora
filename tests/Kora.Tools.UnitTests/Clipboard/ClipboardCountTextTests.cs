using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardCountTextTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("denied")]
    [InlineData("stale")]
    [InlineData("before-count")]
    [InlineData("before-publish")]
    [InlineData("last-check")]
    [InlineData("cancel-start")]
    [InlineData("cancel-publish")]
    [InlineData("revoke")]
    [InlineData("failure")]
    public async Task CountsRequireExactCurrentSourceAndAdmissionWithoutRecaptureRenewalOrMutation(string change)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var reader = new Reader();
        using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        await broker.CaptureAsync(() => true, TestContext.Current.CancellationToken);
        var original = broker.Current!;
        var snapshot = change is "stale" ? new(original.SourceId, original.SnapshotId, original.Request,
            original.Text, original.Version, original.CapturedAt) : original;
        using var cancellation = new CancellationTokenSource();
        if (change is "cancel-start") { cancellation.Cancel(); }
        var checks = 0;
        bool Eligible()
        {
            checks++;
            if (change is "failure") { throw new InvalidOperationException("private marker"); }
            if (change is "cancel-publish" && checks == 3) { cancellation.Cancel(); }
            if (change is "revoke" && checks == 4) { broker.Clear(); }
            return change is not "denied" && !(change is "before-count" && checks == 2)
                && !(change is "before-publish" && checks == 3) && !(change is "last-check" && checks == 4);
        }
        var execute = () => new ClipboardCountText(broker).Execute(snapshot, Eligible, cancellation.Token);
        if (change.StartsWith("cancel", StringComparison.Ordinal)) { execute.Should().Throw<OperationCanceledException>(); }
        else if (change is "failure") { execute.Should().Throw<InvalidOperationException>(); }
        else
        {
            var result = execute();
            result.Source.Should().BeSameAs(snapshot);
            if (change is "valid") { result.Statistics.Should().Be(new ClipboardTextStatistics(11, 11, 11, 2, 1)); }
            else { result.Statistics.Should().BeNull(); }
        }
        reader.Calls.Should().Be(1);
        if (change is not "revoke") { broker.Current.Should().BeSameAs(original); }
        original.CapturedAt.Should().Be(snapshot.CapturedAt);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false, true)]
    [InlineData(RequestOrigin.LocalUi, true, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false, false)]
    [InlineData(RequestOrigin.HostSystem, false, false)]
    public async Task NonNativeAndInvocationBoundRequestsCannotGainStatisticsAuthority(RequestOrigin origin, bool invocation, bool allowed)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var request = HostRequest.Create(origin);
        if (invocation) { request = new(request.RequestId, request.SessionId, request.TaskId, origin, new(Guid.NewGuid())); }
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var broker = new ClipboardSnapshotBroker(new Reader(), TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        ClipboardSnapshot snapshot;
        using (var capture = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await broker.CaptureAsync(() => true, TestContext.Current.CancellationToken);
            snapshot = broker.Current!;
        }
        var result = new ClipboardCountText(broker).Execute(snapshot, () => true, CancellationToken.None);
        (result.Statistics is not null).Should().Be(allowed);
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        public int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, "hello world", 1));
        }
    }
}
