using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardActionsTests
{
    [Fact]
    public async Task Each_registered_action_delegates_to_the_same_immutable_broker()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var reader = new Reader();
        using var broker = new ClipboardSnapshotBroker(reader, TimeProvider.System, NullLogger<ClipboardSnapshotBroker>.Instance);
        var read = new ClipboardRead(broker);
        var reuse = new ClipboardReuse(broker);
        var revoke = new ClipboardRevoke(broker);
        (await read.ExecuteAsync(() => false, TestContext.Current.CancellationToken)).Should().Be(ClipboardOutcome.Denied);
        (await read.ExecuteAsync(() => true, TestContext.Current.CancellationToken)).Should().Be(ClipboardOutcome.Captured);
        var snapshot = broker.Current!;
        reuse.Execute(snapshot.SnapshotId, () => true).Should().Be(ClipboardOutcome.Reused);
        reader.Calls.Should().Be(1);
        broker.Current.Should().BeSameAs(snapshot);
        revoke.Execute(() => false).Should().Be(ClipboardOutcome.Denied);
        broker.Current.Should().BeSameAs(snapshot);
        revoke.Execute(() => true).Should().Be(ClipboardOutcome.Revoked);
        broker.Current.Should().BeNull();
        reuse.Execute(snapshot.SnapshotId, () => true).Should().Be(ClipboardOutcome.Stale);
        using var nonhuman = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        revoke.Execute(() => true).Should().Be(ClipboardOutcome.Denied);
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        public int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, "inert untrusted text", 1));
        }
    }
}
