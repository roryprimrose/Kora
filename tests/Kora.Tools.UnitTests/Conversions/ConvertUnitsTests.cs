using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Conversions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Conversions;

namespace Kora.Tools.UnitTests.Conversions;

[Collection("Host tracing")]
public sealed class ConvertUnitsTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, true, false, ConversionOutcome.Succeeded)]
    [InlineData(RequestOrigin.ActivatedVoice, true, false, ConversionOutcome.Succeeded)]
    [InlineData(RequestOrigin.HostSystem, true, false, ConversionOutcome.Denied)]
    [InlineData(RequestOrigin.LocalUi, false, false, ConversionOutcome.Denied)]
    [InlineData(RequestOrigin.LocalUi, true, true, ConversionOutcome.Cancelled)]
    public void ActionRequiresLiveOriginalHostAndAdmissionAndTerminatesTrace(
        RequestOrigin origin, bool admitted, bool cancelled, ConversionOutcome outcome)
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var result = new ConvertUnits().Execute(ConvertCommand.Parse("convert 1 m to cm", "Kora")!,
            () => admitted, new CancellationToken(cancelled));
        result.Outcome.Should().Be(outcome);
        var tool = activities.Single();
        tool.ParentId.Should().Be(host.Activity!.Id);
        tool.GetTagItem("kora.session.id").Should().Be(host.Request.SessionId.Value);
        tool.Status.Should().Be(outcome == ConversionOutcome.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        tool.Baggage.Should().BeEmpty();
        tool.TagObjects.Should().NotContain(pair => pair.Value is string && Equals(pair.Value, "convert 1 m to cm"));
    }

    [Fact]
    public void InvalidCommandAndPolicyFailuresCannotBecomeSuccessfulFallbacks()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var action = new ConvertUnits();
        foreach (var command in new[]
        {
            new ConvertCommand(false), new(false, Value: 1), new(false, Value: 1, From: ConversionUnit.M),
            new(false, Error: ConversionOutcome.Succeeded),
        })
        { action.Execute(command, () => true, CancellationToken.None).Should().Be(new ConversionResult(ConversionOutcome.InvalidInput)); }
        action.Execute(ConvertCommand.Parse("convert 1 xyz to m", "Kora")!, () => true, CancellationToken.None)
            .Should().Be(new ConversionResult(ConversionOutcome.UnknownUnit));
        action.Execute(ConvertCommand.Parse("convert 1 m to kg", "Kora")!, () => true, CancellationToken.None)
            .Should().Be(new ConversionResult(ConversionOutcome.IncompatibleUnits));
    }

    [Fact]
    public void MissingHostTraceCannotExecute()
    {
        var action = () => new ConvertUnits().Execute(new(false), () => true, CancellationToken.None);
        action.Should().Throw<InvalidOperationException>();
    }
}
