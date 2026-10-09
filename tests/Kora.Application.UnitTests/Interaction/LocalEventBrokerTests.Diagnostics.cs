using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class LocalEventBrokerTests
{
    [Fact]
    public async Task Diagnostics_are_content_free_typed_correlated_and_terminated_with_only_trusted_causal_links()
    {
        const string hostile = "private-content-url-title-path-never-log";
        using var f = new Fixture();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var logger = new CaptureLogger();
        await using var broker = f.Create(logger);
        using (var incoming = new Activity("untrusted.remote").AddBaggage("user-content", hostile).Start())
        {
            await f.Observe(broker);
            var root = stopped.Single(activity => string.Equals(activity.OperationName, "presentation.update", StringComparison.Ordinal));
            root.ParentId.Should().BeNull();
            root.Links.Should().BeEmpty();
            Activity.Current.Should().BeSameAs(incoming);
        }
        var observed = logger.Records.Single();
        observed.Id.Should().Be(8430);
        observed.Properties.Keys.Should().BeEquivalentTo(["Count", "Omitted", "{OriginalFormat}"]);
        observed.Properties["Count"].Should().Be(1);
        stopped.Clear();
        using (var parent = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request))
        {
            f.BeforeCurrent = () => throw new IOException(hostile);
            await f.Reading(broker).Should().ThrowAsync<IOException>();
            var failed = stopped.Single(activity => string.Equals(activity.OperationName, "presentation.update", StringComparison.Ordinal));
            failed.ParentId.Should().BeNull();
            failed.Links.Should().ContainSingle().Which.Context.Should().Be(parent.Activity!.Context);
            failed.Status.Should().Be(ActivityStatusCode.Error);
            HostActivity.Current.Should().BeSameAs(parent);
            Activity.Current.Should().BeSameAs(parent.Activity);
        }
        foreach (var activity in stopped)
        {
            activity.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            activity.Baggage.Should().BeEmpty();
            activity.GetTagItem("kora.session.id").Should().Be(f.Session.Value);
            activity.TagObjects.Should().NotContain(tag => Equals(tag.Value, hostile));
        }
        var failure = logger.Records.Last();
        failure.Id.Should().Be(8431);
        failure.Properties.Keys.Should().BeEquivalentTo(["ExceptionType", "{OriginalFormat}"]);
        failure.Properties["ExceptionType"].Should().Be(nameof(IOException));
        logger.Records.Should().OnlyContain(item => !item.Message.Contains(hostile, StringComparison.Ordinal));
        f.Audits.Should().HaveCount(2);
        f.Audits.Should().OnlyContain(item => item.Session == f.Session.Value && item.Event.ApprovalId == null);
    }

    private sealed class CaptureLogger : ILogger<LocalEventBroker>
    {
        internal List<(int Id, string Message, IReadOnlyDictionary<string, object?> Properties)> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            Records.Add((eventId.Id, formatter(state, exception), properties));
        }
    }
}
