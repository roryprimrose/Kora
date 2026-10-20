using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Clock;
using Kora.Core.Clock;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Clock;

[Collection("Host tracing")]
public sealed class LocalClockWorkflowTests : IDisposable
{
    private readonly List<Activity> stopped = [];
    private readonly ActivityListener listener;

    public LocalClockWorkflowTests()
    {
        listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
    }

    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(LocalClockOperation.Time)]
    [InlineData(LocalClockOperation.Date)]
    [InlineData(LocalClockOperation.DateAndTime)]
    public void AdmittedActionsRetainHostCorrelationAndEndTheirChildTrace(LocalClockOperation operation)
    {
        var clock = new ClockTestHost();
        var workflow = clock.CreateWorkflow();
        using var hostile = new Activity("untrusted").Start();
        var requests = new[] { HostRequest.Create(RequestOrigin.LocalUi), HostRequest.Create(RequestOrigin.ActivatedVoice) };
        foreach (var request in requests)
        {
            using var parent = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            clock.BeforeRead = () => HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
            var result = workflow.Execute(operation, static () => true, CancellationToken.None);
            result.Outcome.Should().Be(LocalClockOutcome.Succeeded);
            result.Answer!.Instant.Should().Be(clock.Instant);
            var child = stopped.Last();
            child.TraceId.Should().Be(parent.Activity!.TraceId).And.NotBe(hostile.TraceId);
            child.ParentSpanId.Should().Be(parent.Activity.SpanId);
            child.Status.Should().Be(ActivityStatusCode.Ok);
            child.GetTagItem("kora.session.id").Should().Be(request.SessionId.Value);
            parent.Complete(HostOperationOutcome.Completed);
        }
        stopped.Where(activity => string.Equals(activity.OperationName, stopped[0].OperationName, StringComparison.Ordinal))
            .Select(activity => activity.GetTagItem("kora.session.id")).Should().OnlyHaveUniqueItems();
        HostActivity.Current.Should().BeNull();
        clock.Reads.Should().Be(2);
    }

    [Theory]
    [InlineData("denied", LocalClockOutcome.Denied, 0)]
    [InlineData("system", LocalClockOutcome.Denied, 0)]
    [InlineData("cancelled", LocalClockOutcome.Cancelled, 0)]
    [InlineData("late-cancelled", LocalClockOutcome.Cancelled, 1)]
    [InlineData("stale", LocalClockOutcome.Stale, 1)]
    [InlineData("invalid-zone", LocalClockOutcome.Unavailable, 1)]
    [InlineData("missing-zone", LocalClockOutcome.Unavailable, 1)]
    [InlineData("unavailable", LocalClockOutcome.Unavailable, 1)]
    public void RefusedStaleCancelledAndUnavailableRequestsNeverReturnAnAnswer(string mode, LocalClockOutcome expected, int reads)
    {
        var clock = new ClockTestHost();
        using var cancellation = new CancellationTokenSource();
        var eligible = !string.Equals(mode, "denied", StringComparison.Ordinal);
        switch (mode)
        {
            case "cancelled": cancellation.Cancel(); break;
            case "late-cancelled": clock.BeforeRead = cancellation.Cancel; break;
            case "stale": clock.BeforeRead = () => eligible = false; break;
            case "invalid-zone": clock.Failure = new InvalidTimeZoneException("fixture"); break;
            case "missing-zone": clock.Failure = new TimeZoneNotFoundException("fixture"); break;
            case "unavailable": clock.Failure = new InvalidOperationException("fixture"); break;
        }
        using var parent = HostActivity.BeginRoot(HostRequest.Create(string.Equals(mode, "system", StringComparison.Ordinal)
            ? RequestOrigin.HostSystem : RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var result = clock.CreateWorkflow().Execute(LocalClockOperation.Time, () => eligible, cancellation.Token);
        result.Should().Be(new LocalClockResult(expected));
        clock.Reads.Should().Be(reads);
        stopped.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
        parent.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void MissingOrExpiredHostContextCannotCreateClockAuthority()
    {
        var clock = new ClockTestHost();
        var workflow = clock.CreateWorkflow();
        var execute = () => workflow.Execute(LocalClockOperation.Time, static () => true, CancellationToken.None);
        execute.Should().Throw<InvalidOperationException>();
        using (var parent = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request))
        {
            using (new Activity("foreign").Start()) { execute.Should().Throw<InvalidOperationException>(); }
            var invalid = () => workflow.Execute(LocalClockOperation.Help, static () => true, CancellationToken.None);
            invalid.Should().Throw<ArgumentOutOfRangeException>();
            parent.Complete(HostOperationOutcome.Completed);
        }
        execute.Should().Throw<InvalidOperationException>();
        clock.Reads.Should().Be(0);
    }
}
