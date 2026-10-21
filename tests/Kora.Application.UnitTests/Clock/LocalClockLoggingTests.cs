using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Clock;
using Kora.Core.Clock;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Neovolve.Logging.Xunit;

namespace Kora.Application.UnitTests.Clock;

[Collection("Host tracing")]
public sealed class LocalClockLoggingTests(ITestOutputHelper output) : LoggingTestsBase<LocalClockWorkflow>(output)
{
    [Fact]
    public void EnabledDiagnosticsCoverObservationAndFailureWithoutClockValuesInTheTrace()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var clock = new ClockTestHost();
        var workflow = clock.CreateWorkflow(Logger);
        workflow.Execute(LocalClockOperation.Time, static () => true, CancellationToken.None)
            .Outcome.Should().Be(LocalClockOutcome.Succeeded);
        clock.Failure = new InvalidTimeZoneException("fixture");
        workflow.Execute(LocalClockOperation.Date, static () => true, CancellationToken.None)
            .Outcome.Should().Be(LocalClockOutcome.Unavailable);
        parent.Activity!.Tags.Should().NotContain(pair => pair.Value != null
            && pair.Value.Contains("fixture/+14", StringComparison.Ordinal));
        parent.Complete(HostOperationOutcome.Completed);
    }
}
