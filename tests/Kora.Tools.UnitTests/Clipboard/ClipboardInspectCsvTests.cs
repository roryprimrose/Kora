using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

namespace Kora.Tools.UnitTests.Clipboard;

[Collection("Host tracing")]
public sealed class ClipboardInspectCsvTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.HostSystem, false)]
    public void OnlyNativeGestureCanInspectExactSource(RequestOrigin origin, bool admitted)
    {
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var source = Snapshot();
        var result = new ClipboardInspectCsv().Execute(source, () => source, () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(admitted ? ClipboardCsvOutcome.Inspected : ClipboardCsvOutcome.Stale);
        if (admitted) { result.Inspection!.RecordCount.Should().Be(1); }
        else { result.Inspection.Should().BeNull(); }
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("same-id")]
    [InlineData("ineligible")]
    [InlineData("cancel")]
    [InlineData("cancel-after")]
    public void FencesReplacementEligibilityAndCancellationWithoutPartialOutput(string change)
    {
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        var source = Snapshot();
        var calls = 0;
        ClipboardSnapshot? Current()
        {
            calls++;
            if (change is "cancel-after" && calls == 2) { cancellation.Cancel(); }
            if (change is "same-id") { return new(source.SourceId, source.SnapshotId, source.Request, source.Text, source.Version, source.CapturedAt); }
            return change is "before" || (change is "after" && calls == 2) ? null : source;
        }
        if (change is "cancel") { cancellation.Cancel(); }
        var result = new ClipboardInspectCsv().Execute(source, Current, () => change is not "ineligible", cancellation.Token);
        result.Outcome.Should().Be(change.StartsWith("cancel", StringComparison.Ordinal) ? ClipboardCsvOutcome.Cancelled : ClipboardCsvOutcome.Stale);
        result.Inspection.Should().BeNull();
        var nullSource = () => new ClipboardInspectCsv().Execute(null!, Current, () => true, cancellation.Token);
        nullSource.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ActivityIsCorrelatedWithoutSourceTagsAndUnexpectedFailureIsNotSyntaxMismatch()
    {
        using var listener = Listen();
        var activities = new List<Activity>();
        listener.ActivityStopped = activities.Add;
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var source = Snapshot();
        new ClipboardInspectCsv().Execute(source, () => source, () => true, TestContext.Current.CancellationToken);
        activities.Should().ContainSingle();
        activities[0].ParentId.Should().Be(host.Activity!.Id);
        activities[0].Status.Should().Be(ActivityStatusCode.Ok);
        activities[0].TagObjects.Should().NotContain(tag => Equals(tag.Value, source.Text));
        var failure = () => new ClipboardInspectCsv().Execute(source, () => throw new IOException("private marker"),
            () => true, TestContext.Current.CancellationToken);
        failure.Should().Throw<IOException>();
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static ClipboardSnapshot Snapshot() =>
        new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi), "=literal,\"a,b\"", 1, DateTimeOffset.UnixEpoch);
}
