using System.Diagnostics;
using System.Reflection;
using AwesomeAssertions;
using Kora.Core.Calculations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Calculations;

namespace Kora.Tools.UnitTests.Calculations;

[Collection("Host tracing")]
public sealed class CalculateTests : IDisposable
{
    private readonly ActivityListener hostListener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public CalculateTests() => ActivitySource.AddActivityListener(hostListener);
    public void Dispose() => hostListener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public void ActionUsesTheSamePureRulesAndHostTraceWithoutContent(RequestOrigin origin)
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var request = HostActivity.RequireCurrent().Request;
        var observed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.ParentId, root.Activity!.Id, StringComparison.Ordinal)) { observed.Add(activity); }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var action = new Calculate();
        action.Execute("12345+1", () => true, CancellationToken.None).Value.Should().Be("12346");
        action.Execute("1/0", () => true, CancellationToken.None).Outcome.Should().Be(CalculationOutcome.DivisionByZero);
        observed.Should().HaveCount(2);
        observed[0].Status.Should().Be(ActivityStatusCode.Ok);
        observed[1].Status.Should().Be(ActivityStatusCode.Error);
        observed.Should().OnlyContain(activity => activity.TraceId == root.Activity!.TraceId
            && activity.GetTagItem("kora.session.id")!.Equals(request.SessionId.Value));
        observed.SelectMany(activity => activity.TagObjects).Should().NotContain(tag => Equals(tag.Value, "12345+1"));
        typeof(Calculate).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    public void NonoriginalOriginIsDenied(RequestOrigin origin)
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var result = new Calculate().Execute("1+1", () => true, CancellationToken.None);
        result.Outcome.Should().Be(CalculationOutcome.Denied);
        result.Value.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrRevokedAdmissionFailsClosed(bool initiallyAdmitted)
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var calls = 0;
        new Calculate().Execute("1", () => ++calls == 1 && initiallyAdmitted, CancellationToken.None)
            .Outcome.Should().Be(CalculationOutcome.Denied);
    }

    [Fact]
    public void CancellationAndMissingHostContextCannotReturnSuccess()
    {
        var action = new Calculate();
        Action missing = () => action.Execute("1", () => true, CancellationToken.None);
        missing.Should().Throw<InvalidOperationException>();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        Action cancelled = () => action.Execute("1", () => true, new CancellationToken(true));
        cancelled.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void InvocationContextCannotTurnTheNativeActionIntoAModelTool()
    {
        var original = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(new(original.RequestId, original.SessionId, original.TaskId,
            original.Origin, new(Guid.NewGuid())), HostActivityLayer.Application, HostOperation.Request);
        new Calculate().Execute("1+1", () => true, CancellationToken.None).Outcome.Should().Be(CalculationOutcome.Denied);
    }

    [Fact]
    public void IncomingActivityCannotSelectOrLeakAHostSession()
    {
        using var incoming = new Activity("untrusted-provider").Start();
        incoming.SetTag("kora.session.id", Guid.NewGuid());
        var observed = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (string.Equals(activity.DisplayName, "tool.invoke", StringComparison.Ordinal)) { observed.Add(activity); }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var action = new Calculate();
        var sessions = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            sessions.Add(request.SessionId.Value);
            using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            action.Execute("1", () => true, CancellationToken.None).Value.Should().Be("1");
        }
        observed.Should().HaveCount(2);
        observed.Select(activity => activity.GetTagItem("kora.session.id")).Should().Equal(sessions.Cast<object>());
        observed.Select(activity => activity.TraceId).Should().OnlyHaveUniqueItems().And.NotContain(incoming.TraceId);
        Activity.Current.Should().BeSameAs(incoming);
    }
}
