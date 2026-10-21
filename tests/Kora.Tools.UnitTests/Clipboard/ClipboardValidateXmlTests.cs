using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardValidateXmlTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("denied")]
    [InlineData("stale")]
    [InlineData("before-parse")]
    [InlineData("before-publish")]
    [InlineData("cancel-parse")]
    [InlineData("cancel-publish")]
    public async Task ActionOnlyConsumesExactCurrentAdmittedObjectAndNeverRecapturesOrRenews(string change)
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
        var changes = 0;
        broker.Changed += (_, _) => changes++;
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        bool Eligible()
        {
            checks++;
            if (change is "cancel-parse" && checks == 2 || change is "cancel-publish" && checks == 3) { cancellation.Cancel(); }
            return change is not "denied" && !(change is "before-parse" && checks == 2)
                && !(change is "before-publish" && checks == 4);
        }
        var action = new ClipboardValidateXml(broker);
        if (change.StartsWith("cancel", StringComparison.Ordinal))
        {
            var execute = () => action.Execute(snapshot, Eligible, cancellation.Token);
            execute.Should().Throw<OperationCanceledException>();
        }
        else
        {
            var result = action.Execute(snapshot, Eligible, cancellation.Token);
            result.Source.Should().BeSameAs(snapshot);
            result.Outcome.Should().Be(change is "valid" ? ClipboardXmlOutcome.WellFormedXml : ClipboardXmlOutcome.PolicyRefused);
            result.Reason.Should().Be(change is "valid" ? ClipboardXmlReason.None : ClipboardXmlReason.SourceUnavailable);
        }
        reader.Calls.Should().Be(1);
        changes.Should().Be(0);
        broker.Current.Should().BeSameAs(original);
        original.CapturedAt.Should().Be(snapshot.CapturedAt);
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        public int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, "<private/>", 1));
        }
    }
}
