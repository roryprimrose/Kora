using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Tools;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Tools;

[Collection("Host tracing")]
public sealed class ReadOnlyCapabilityRegistryTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public ReadOnlyCapabilityRegistryTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void Results_emit_structured_host_correlated_logs_without_hostile_input_content()
    {
        var logger = new RecordingLogger();
        using var fixture = new Fixture(logger);
        using var root = Root();
        fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Management), "hostile-secret", "{}",
            TestContext.Current.CancellationToken);
        logger.TraceId.Should().Be(root.Activity!.TraceId);
        logger.Properties.Should().Contain(item => item.Key == "CapabilityId" && Equals(item.Value, "unknown"));
        logger.Properties.Should().Contain(item => item.Key == "CapabilityOutcome" && Equals(item.Value, CapabilityOutcome.Denied));
        logger.Properties.Should().NotContain(item => Equals(item.Value, "hostile-secret"));
    }

    [Theory]
    [InlineData(CapabilityLane.Native)]
    [InlineData(CapabilityLane.Management)]
    [InlineData(CapabilityLane.Execution)]
    public void Every_admitted_descriptor_has_a_direct_typed_handler(CapabilityLane lane)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(lane);
        foreach (var descriptor in ReadOnlyCapabilityCatalog.Descriptors)
        {
            var input = descriptor.Input == CapabilityInputShape.Id
                ? descriptor.Output == CapabilityOutputShape.Descriptor
                    ? "{\"id\":\"application.get_version\"}" : "{\"id\":\"local.inference\"}"
                : "{}";
            var reply = fixture.Registry.Invoke(caller, descriptor.Id, input, TestContext.Current.CancellationToken);
            reply.Outcome.Should().Be(CapabilityOutcome.Succeeded);
            var wire = Encoding.UTF8.GetString(ReadOnlyCapabilityRegistry.Serialize(reply));
            wire.Should().Contain(descriptor.Output switch
            {
                CapabilityOutputShape.Descriptors => "\"capabilities\"",
                CapabilityOutputShape.Descriptor => "\"descriptor\"",
                CapabilityOutputShape.Version => "\"version\"",
                CapabilityOutputShape.Readiness => "\"readiness\"",
                CapabilityOutputShape.Runtimes => "\"runtimes\"",
                _ => "\"runtime\"",
            });
            wire.Should().NotContain("computer.lock").And.NotContain("\"instructions\"").And.NotContain("\"script\"");
            Encoding.UTF8.GetByteCount(wire).Should().BeLessThanOrEqualTo(4096);
            HostActivity.Current.Should().BeSameAs(root);
        }
    }

    [Theory]
    [InlineData("capabilities.list", "null", "invalid-input")]
    [InlineData("capabilities.list", "[]", "invalid-input")]
    [InlineData("capabilities.list", "{", "invalid-input")]
    [InlineData("capabilities.list", "{\"count\":1,\"count\":2}", "duplicate-input-field")]
    [InlineData("capabilities.list", "{\"offset\":-1}", "page-out-of-range")]
    [InlineData("capabilities.list", "{\"count\":0}", "page-out-of-range")]
    [InlineData("capabilities.list", "{\"count\":7}", "page-out-of-range")]
    [InlineData("capabilities.list", "{\"offset\":6}", "page-out-of-range")]
    [InlineData("capabilities.list", "{\"offset\":2147483647}", "page-out-of-range")]
    [InlineData("capabilities.list", "{\"count\":2147483648}", "invalid-page-input")]
    [InlineData("capabilities.list", "{\"count\":1.1}", "invalid-page-input")]
    [InlineData("capabilities.list", "{\"count\":\"1\"}", "invalid-page-input")]
    [InlineData("capabilities.list", "{\"lane\":\"Execution\"}", "invalid-page-input")]
    [InlineData("capabilities.list", "{\"sessionId\":\"foreign\"}", "invalid-page-input")]
    [InlineData("capabilities.list", "{\"approved\":true}", "invalid-page-input")]
    [InlineData("capabilities.list", "{\"count\":{\"nested\":{\"too\":{\"deep\":{\"extra\":1}}}}}", "invalid-input")]
    [InlineData("application.get_version", "{\"path\":\"secret\"}", "unknown-input-field")]
    [InlineData("capabilities.get", "{}", "invalid-id-input")]
    [InlineData("capabilities.get", "{\"id\":null}", "invalid-id-input")]
    [InlineData("capabilities.get", "{\"Id\":\"application.get_version\"}", "invalid-id-input")]
    [InlineData("capabilities.get", "{\"id\":\"application.get_version\",\"lane\":\"Native\"}", "invalid-id-input")]
    [InlineData("capabilities.get", "{\"id\":\"computer.lock\"}", "unknown-capability")]
    [InlineData("runtime.get_status", "{\"id\":\"remote\"}", "unknown-runtime")]
    [InlineData("capabilities.List", "{}", "unknown-capability")]
    [InlineData("settings.get", "{}", "unknown-capability")]
    public void Hostile_or_unknown_input_is_explicitly_denied(string id, string input, string reason)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var result = fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Management), id, input,
            TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(CapabilityOutcome.Denied);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public void Paging_has_deterministic_records_counts_and_continuations()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Native);
        var first = fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{\"offset\":0,\"count\":2}",
            TestContext.Current.CancellationToken).Capabilities!;
        first.TotalRecords.Should().Be(6);
        first.NextOffset.Should().Be(2);
        first.Records.Select(item => item.Id).Should().Equal("capabilities.list", "capabilities.get");
        var last = fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{\"offset\":4,\"count\":6}",
            TestContext.Current.CancellationToken).Capabilities!;
        last.NextOffset.Should().BeNull();
        last.Records.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready, CapabilityAvailability.Available)]
    [InlineData(DependencyReadiness.Missing, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.NeedsConfiguration, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.Incompatible, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.Blocked, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.Failed, CapabilityAvailability.Unavailable)]
    public void Runtime_reports_actual_recorded_health_without_details_or_qualification(
        DependencyReadiness readiness, CapabilityAvailability expected)
    {
        using var fixture = new Fixture();
        fixture.Dependencies.RecordObservation(new("local.inference", "private model content", readiness,
            "secret credential http://private-endpoint C:\\private\\path"));
        using var root = Root();
        var result = fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Management),
            ReadOnlyCapabilityCatalog.RuntimeStatus, "{\"id\":\"local.inference\"}", TestContext.Current.CancellationToken);
        result.Runtime!.Readiness.Should().Be(readiness);
        result.Runtime.Availability.Should().Be(expected);
        result.Runtime.ObservedAt.Should().NotBeNull();
        result.Runtime.Locality.Should().Be(RuntimeLocality.Local);
        result.Runtime.ToolLoopQualified.Should().BeFalse();
        Encoding.UTF8.GetString(ReadOnlyCapabilityRegistry.Serialize(result)).Should()
            .NotContain("secret").And.NotContain("private").And.NotContain("http");
    }

    [Fact]
    public void Unobserved_runtime_and_version_never_fabricate_health_or_deployment()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Native);
        var runtime = fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.RuntimeList, "{}",
            TestContext.Current.CancellationToken).Runtimes!.Records.Single();
        runtime.Availability.Should().Be(CapabilityAvailability.NotObserved);
        runtime.ObservedAt.Should().BeNull();
        runtime.Readiness.Should().BeNull();
        var version = fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}",
            TestContext.Current.CancellationToken).Version!;
        version.Version.Should().Be("1.2.3+actual-build");
        version.DeploymentObservation.Should().Contain("Not observed");
    }

    [Fact]
    public void Readiness_is_a_bounded_projection_of_registered_dependencies_only()
    {
        using var fixture = new Fixture();
        fixture.Dependencies.RecordObservation(new("kora.storage", "private", DependencyReadiness.Ready, "C:\\private"));
        fixture.Dependencies.RecordObservation(new("foreign", "private", DependencyReadiness.Ready, "secret"));
        using var root = Root();
        var result = fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Management), ReadOnlyCapabilityCatalog.Readiness,
            "{\"offset\":0,\"count\":2}", TestContext.Current.CancellationToken);
        result.Readiness!.Records.Should().HaveCount(2);
        result.Readiness.NextOffset.Should().Be(2);
        result.Readiness.TotalRecords.Should().Be(6);
        result.Readiness.Records[0].Availability.Should().Be(CapabilityAvailability.Available);
        result.Readiness.Records[1].Availability.Should().Be(CapabilityAvailability.NotObserved);
        Encoding.UTF8.GetString(ReadOnlyCapabilityRegistry.Serialize(result)).Should().NotContain("private").And.NotContain("foreign");
    }

    [Fact]
    public void Context_is_current_host_registry_and_live_request_bound_not_incoming_trace_bound()
    {
        using var fixture = new Fixture();
        var withoutHost = () => fixture.Registry.Admit(CapabilityLane.Native);
        withoutHost.Should().Throw<InvalidOperationException>();
        using var foreignTrace = new Activity("untrusted").Start();
        withoutHost.Should().Throw<InvalidOperationException>();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Native);
        var invalidLane = () => fixture.Registry.Admit((CapabilityLane)99);
        invalidLane.Should().Throw<InvalidOperationException>();
        fixture.Host.Current = false;
        withoutHost.Should().Throw<InvalidOperationException>();
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
            .Reason.Should().Be("current-host-context-required");
        fixture.Host.Current = true;
        var hostileLane = new CapabilityCaller(caller.RegistryId, root, (CapabilityLane)99);
        fixture.Registry.Invoke(hostileLane, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        using var other = new Fixture();
        other.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        using (var otherRequest = Root())
        {
            fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
                .Outcome.Should().Be(CapabilityOutcome.Denied);
        }
        root.Complete(HostOperationOutcome.Completed);
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
            .Outcome.Should().Be(CapabilityOutcome.Denied);
    }

    [Fact]
    public void Disposed_context_and_reused_request_objects_do_not_reacquire_authority()
    {
        using var fixture = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        CapabilityCaller caller;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            caller = fixture.Registry.Admit(CapabilityLane.Native);
        }
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        using var replay = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken)
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        var missingCaller = () => fixture.Registry.Invoke(null!, ReadOnlyCapabilityCatalog.List, "{}", TestContext.Current.CancellationToken);
        missingCaller.Should().Throw<ArgumentNullException>();
        var missingInput = () => fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Native),
            ReadOnlyCapabilityCatalog.List, null!, TestContext.Current.CancellationToken);
        missingInput.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Tool_observations_preserve_host_trace_parentage_and_truthful_terminal_status()
    {
        using var fixture = new Fixture();
        var stopped = new List<Activity>();
        listener.ActivityStopped = activity => stopped.Add(activity);
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Native);
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", TestContext.Current.CancellationToken);
        var tool = stopped.Single();
        tool.OperationName.Should().Be("tool.invoke");
        tool.TraceId.Should().Be(root.Activity!.TraceId);
        tool.ParentSpanId.Should().Be(root.Activity.SpanId);
        tool.Status.Should().Be(ActivityStatusCode.Ok);
        tool.GetTagItem("kora.session.id").Should().Be(root.Request.SessionId.Value);
        fixture.Registry.Invoke(caller, "unknown", "{}", TestContext.Current.CancellationToken);
        stopped.Last().Status.Should().Be(ActivityStatusCode.Error);
        using var cancellation = new CancellationTokenSource();
        fixture.Info.OnRead = cancellation.Cancel;
        var cancelled = () => fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        stopped.Last().GetTagItem("kora.outcome").Should().Be("Cancelled");
    }
    [Fact]
    public void Host_loss_cancellation_and_provider_failure_never_return_late_success()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Native);
        fixture.Info.OnRead = () => fixture.Host.Current = false;
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", TestContext.Current.CancellationToken)
            .Reason.Should().Be("host-admission-changed");
        fixture.Host.Current = true;
        using var cancellation = new CancellationTokenSource();
        fixture.Info.OnRead = cancellation.Cancel;
        var late = () => fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", cancellation.Token);
        late.Should().Throw<OperationCanceledException>();
        var early = () => fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.List, "{}", cancellation.Token);
        early.Should().Throw<OperationCanceledException>();
        fixture.Info.OnRead = () => throw new IOException("provider failed");
        var failure = () => fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", TestContext.Current.CancellationToken);
        failure.Should().Throw<IOException>();
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Invalid_version_provider_does_not_fall_back_to_configured_success(string? version)
    {
        using var fixture = new Fixture();
        fixture.Info.Value = version!;
        using var root = Root();
        fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Native), ReadOnlyCapabilityCatalog.Version, "{}",
            TestContext.Current.CancellationToken).Reason.Should().Be("invalid-version-observation");
        fixture.Info.Value = new string('x', 129);
        fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Native), ReadOnlyCapabilityCatalog.Version, "{}",
            TestContext.Current.CancellationToken).Outcome.Should().Be(CapabilityOutcome.Failed);
    }

    [Theory]
    [InlineData(1024, CapabilityOutcome.Succeeded)]
    [InlineData(1025, CapabilityOutcome.Denied)]
    public void Entire_input_utf8_frame_has_an_exact_limit(int bytes, CapabilityOutcome outcome)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var input = "{}" + new string(' ', bytes - 2);
        Encoding.UTF8.GetByteCount(input).Should().Be(bytes);
        fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Management), ReadOnlyCapabilityCatalog.List, input,
            TestContext.Current.CancellationToken).Outcome.Should().Be(outcome);
        var multibyte = "{\"id\":\"" + new string('\u00e9', 510) + "\"}";
        multibyte.Length.Should().BeLessThan(1024);
        fixture.Registry.Invoke(fixture.Registry.Admit(CapabilityLane.Management), ReadOnlyCapabilityCatalog.Get, multibyte,
            TestContext.Current.CancellationToken).Reason.Should().Be("input-limit-exceeded");
    }

    [Theory]
    [InlineData(4096, CapabilityOutcome.Succeeded)]
    [InlineData(4097, CapabilityOutcome.Failed)]
    public void Entire_serialized_output_counts_framing_at_the_exact_limit(int bytes, CapabilityOutcome expected)
    {
        var baseline = ReadOnlyCapabilityRegistry.Serialize(new(CapabilityOutcome.Succeeded, "observed",
            Version: new("", "unknown"))).Length;
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
            Version: new(new string('x', bytes - baseline), "unknown"));
        ReadOnlyCapabilityRegistry.BoundReply(reply).Outcome.Should().Be(expected);
        ReadOnlyCapabilityRegistry.Serialize(reply).Length.Should().BeLessThanOrEqualTo(4096);
    }

    private static HostActivity Root() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

    private sealed class Fixture : IDisposable
    {
        public HostAccess Host { get; } = new();
        public AppInfo Info { get; } = new();
        public DependencyBootstrapper Dependencies { get; } = new([], NullLogger<DependencyBootstrapper>.Instance);
        public ReadOnlyCapabilityRegistry Registry { get; }
        public Fixture(ILogger<ReadOnlyCapabilityRegistry>? logger = null) =>
            Registry = new(Host, Info, Dependencies, logger ?? NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        public void Dispose() => Dependencies.Dispose();
    }

    private sealed class RecordingLogger : ILogger<ReadOnlyCapabilityRegistry>
    {
        public ActivityTraceId TraceId { get; private set; }
        public List<KeyValuePair<string, object?>> Properties { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            TraceId = Activity.Current!.TraceId;
            Properties.AddRange((IEnumerable<KeyValuePair<string, object?>>)(state!));
        }
    }

    private sealed class HostAccess : ICapabilityHostAccess
    {
        public bool Current { get; set; } = true;
        public bool IsCurrentHost => Current;
    }

    private sealed class AppInfo : IApplicationInfo
    {
        public string Value { get; set; } = "1.2.3+actual-build";
        public Action? OnRead { get; set; }
        public string Version
        {
            get
            {
                OnRead?.Invoke();
                return Value;
            }
        }
    }
}