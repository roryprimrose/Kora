using System.Reflection;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Tools.Application;
using Kora.Tools.Capabilities;
using Kora.Tools.Readiness;
using Kora.Tools.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Capabilities;

public sealed class ReadOnlyActionTests
{
    [Fact]
    public void Each_R06_action_has_a_specific_class_and_no_public_gateway_bypass()
    {
        Type[] actions = [typeof(CapabilitiesList), typeof(CapabilitiesGet), typeof(ApplicationGetVersion),
            typeof(ReadinessGet), typeof(RuntimeList), typeof(RuntimeGetStatus)];
        foreach (var action in actions)
        {
            action.Assembly.GetName().Name.Should().Be("Kora.Tools");
            action.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
            action.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Count(method => string.Equals(method.Name, "Execute", StringComparison.Ordinal)).Should().Be(1);
        }
    }

    [Fact]
    public void Capability_actions_preserve_the_same_descriptors_and_exact_paging()
    {
        var list = new CapabilitiesList();
        var first = list.Execute(new(0, 2)).Capabilities!;
        first.TotalRecords.Should().Be(6);
        first.NextOffset.Should().Be(2);
        first.Records.Should().Equal(ReadOnlyCapabilityCatalog.Descriptors.Take(2));
        var last = list.Execute(new(4, 6)).Capabilities!;
        last.NextOffset.Should().BeNull();
        last.Records.Should().Equal(ReadOnlyCapabilityCatalog.Descriptors.Skip(4));
        list.Execute(new(6, 1)).Reason.Should().Be("page-out-of-range");
        var get = new CapabilitiesGet();
        get.Execute(new(ReadOnlyCapabilityCatalog.Version)).Descriptor.Should().BeSameAs(
            ReadOnlyCapabilityCatalog.Descriptors.Single(item => string.Equals(item.Id, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal)));
        get.Execute(new("unknown")).Reason.Should().Be("unknown-capability");
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready, CapabilityAvailability.Available)]
    [InlineData(DependencyReadiness.Missing, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.NeedsConfiguration, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.Incompatible, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.Blocked, CapabilityAvailability.Unavailable)]
    [InlineData(DependencyReadiness.Failed, CapabilityAvailability.Unavailable)]
    public void Readiness_and_runtime_actions_share_content_minimizing_recorded_observations(
        DependencyReadiness state, CapabilityAvailability expected)
    {
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        dependencies.RecordObservation(new("local.inference", "private title", state, "private secret detail"));
        var readiness = new ReadinessGet(dependencies);
        var first = readiness.Execute(new(0, 2)).Readiness!;
        first.NextOffset.Should().Be(2);
        first.Records.Should().HaveCount(2);
        var page = readiness.Execute(new(3, 6)).Readiness!;
        page.NextOffset.Should().BeNull();
        var health = page.Records[0];
        health.Availability.Should().Be(expected);
        health.Readiness.Should().Be(state);
        health.ObservedAt.Should().Be(dependencies.Observations.Single().ObservedAt);
        health.Reason.Should().NotContain("private");
        readiness.Execute(new(6, 1)).Reason.Should().Be("page-out-of-range");
        var source = new RecordedRuntimeObservation(dependencies);
        var runtime = new RuntimeGetStatus(source).Execute(new("local.inference")).Runtime!;
        runtime.Locality.Should().Be(RuntimeLocality.Local);
        runtime.ToolLoopQualified.Should().BeFalse();
        runtime.Readiness.Should().Be(health.Readiness);
        runtime.Availability.Should().Be(health.Availability);
        runtime.Reason.Should().Be(health.Reason);
        new RuntimeGetStatus(source).Execute(new("remote")).Reason.Should().Be("unknown-runtime");
        var listed = new RuntimeList(source).Execute(new(0, 6)).Runtimes!;
        listed.Records.Should().Equal(runtime);
        listed.TotalRecords.Should().Be(1);
        listed.NextOffset.Should().BeNull();
        new RuntimeList(source).Execute(new(1, 1)).Reason.Should().Be("page-out-of-range");
    }

    [Fact]
    public void Missing_observations_are_unobserved_not_ready_and_no_probe_is_run()
    {
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var readiness = new ReadinessGet(dependencies).Execute(new(0, 6)).Readiness!;
        readiness.Records.Should().OnlyContain(item => item.Availability == CapabilityAvailability.NotObserved
            && item.Readiness == null && item.ObservedAt == null);
        new RuntimeGetStatus(new RecordedRuntimeObservation(dependencies)).Execute(new("local.inference"))
            .Runtime!.Availability.Should().Be(CapabilityAvailability.NotObserved);
        dependencies.Observations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", CapabilityOutcome.Failed)]
    [InlineData(" ", CapabilityOutcome.Failed)]
    [InlineData(null, CapabilityOutcome.Failed)]
    [InlineData("1.2.3", CapabilityOutcome.Succeeded)]
    public void Version_action_preserves_the_provider_contract_and_invalid_value_outcome(string? version, CapabilityOutcome expected)
    {
        var action = new ApplicationGetVersion(new Info(version!));
        action.Execute().Outcome.Should().Be(expected);
        new ApplicationGetVersion(new Info(new string('x', 129))).Execute().Reason.Should().Be("invalid-version-observation");
        new ApplicationGetVersion(new Info(new string('x', 128))).Execute().Outcome.Should().Be(CapabilityOutcome.Succeeded);
    }

    private sealed class Info(string version) : IApplicationInfo
    {
        public string Version => version;
    }
}
