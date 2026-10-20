using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Calendar;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Calendar;

[Collection("Host tracing")]
public sealed class DateArithmeticRegistryTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public DateArithmeticRegistryTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public void Registered_actions_are_traced_isolated_and_log_no_dates(RequestOrigin origin)
    {
        var host = new Host();
        var logger = new RecordingLogger();
        var registry = new DateArithmeticRegistry(host, new(), new(), new(), logger);
        var stopped = new List<Activity>();
        listener.ActivityStopped = stopped.Add;
        using var hostile = new Activity("hostile").AddBaggage("untrusted", "secret").Start();
        using (var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request))
        {
            registry.Invoke(new(DateArithmeticOperation.Help), CancellationToken.None).Should().Contain("date help");
            registry.Invoke(new(DateArithmeticOperation.Difference, new(2000, 3, 1), new(2000, 2, 29)),
                CancellationToken.None).Should().StartWith("-1 whole calendar days");
            registry.Invoke(new(DateArithmeticOperation.Add, new(2000, 2, 29), Days: 1),
                CancellationToken.None).Should().Be("2000-03-01");
            stopped.Should().HaveCount(3).And.OnlyContain(child => child.ParentSpanId == root.Activity!.SpanId
                && child.Status == ActivityStatusCode.Ok && !child.Baggage.Any());
            stopped.SelectMany(child => child.Tags).Should().NotContain(tag => tag.Value == "2000-02-29");
            logger.TraceIds.Should().OnlyContain(id => id == root.Activity!.TraceId);
            logger.Values.Should().NotContain("2000-02-29").And.NotContain("2000-03-01");
            root.Complete(HostOperationOutcome.Completed);
        }
        var previous = logger.TraceIds[0];
        using var other = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        registry.Invoke(new(DateArithmeticOperation.Add, new(1900, 2, 28), Days: 1), CancellationToken.None)
            .Should().Be("1900-03-01");
        logger.TraceIds[^1].Should().NotBe(previous);
    }

    [Fact]
    public void Unadmitted_unknown_expired_and_hostile_activity_states_fail_closed()
    {
        var host = new Host();
        var registry = new DateArithmeticRegistry(host, new(), new(), new(), new RecordingLogger());
        var invoke = () => registry.Invoke(new(DateArithmeticOperation.Help), CancellationToken.None);
        invoke.Should().Throw<InvalidOperationException>();
        using (var system = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request))
        {
            invoke.Should().Throw<InvalidOperationException>();
        }
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        host.Current = false;
        invoke.Should().Throw<InvalidOperationException>();
        host.Current = true;
        using (var foreign = new Activity("foreign").Start()) { invoke.Should().Throw<InvalidOperationException>(); }
        root.Complete(HostOperationOutcome.Completed);
        invoke.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancellation_invalid_operation_null_and_overflow_are_explicit_with_failed_tool_trace()
    {
        var logger = new RecordingLogger();
        var registry = new DateArithmeticRegistry(new Host(), new(), new(), new(), logger);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var stopped = new List<Activity>();
        listener.ActivityStopped = stopped.Add;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellation = () => registry.Invoke(new(DateArithmeticOperation.Help), cancelled.Token);
        cancellation.Should().Throw<OperationCanceledException>();
        var missing = () => registry.Invoke(null!, CancellationToken.None);
        missing.Should().Throw<ArgumentNullException>();
        var invalid = () => registry.Invoke(new((DateArithmeticOperation)100), CancellationToken.None);
        invalid.Should().Throw<InvalidDataException>();
        var overflow = () => registry.Invoke(new(DateArithmeticOperation.Add, DateOnly.MaxValue, Days: 1), CancellationToken.None);
        overflow.Should().Throw<InvalidDataException>();
        stopped.Should().HaveCount(2).And.OnlyContain(activity => activity.Status == ActivityStatusCode.Error);
        logger.Values.Should().Contain("failed");
    }

    private sealed class Host : ICapabilityHostAccess
    {
        public bool Current { get; set; } = true;
        public bool IsCurrentHost => Current;
    }

    private sealed class RecordingLogger : ILogger<DateArithmeticRegistry>
    {
        public List<ActivityTraceId> TraceIds { get; } = [];
        public List<string> Values { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            TraceIds.Add(Activity.Current!.TraceId);
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                Values.AddRange(properties.Select(property => property.Value?.ToString() ?? string.Empty));
            }
        }
    }
}
