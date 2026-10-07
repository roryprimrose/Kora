using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class ReadOnlyCapabilityCatalogTests
{
    [Fact]
    public void Catalog_has_only_the_six_canonical_read_only_descriptors()
    {
        ReadOnlyCapabilityCatalog.Descriptors.Select(item => item.Id).Should().Equal(
            "capabilities.list", "capabilities.get", "application.get_version",
            "readiness.get", "runtime.list", "runtime.get_status");
        foreach (var descriptor in ReadOnlyCapabilityCatalog.Descriptors)
        {
            descriptor.SchemaVersion.Should().Be(1);
            descriptor.Effect.Should().Be(CapabilityEffect.ReadOnlyObservation);
            descriptor.Availability.Should().Be(CapabilityAvailability.Available);
            descriptor.CallerLanes.Should().Equal(CapabilityLane.Native, CapabilityLane.Management, CapabilityLane.Execution);
            descriptor.Limits.Should().Be(ReadOnlyCapabilityCatalog.Limits);
            ReadOnlyCapabilityCatalog.MatchCommand(BuiltInCommandRouter.Normalize(descriptor.Id))!.Id.Should().Be(descriptor.Id);
        }
    }

    [Fact]
    public void Exact_local_aliases_and_descriptor_gets_are_synchronized_without_selector_actions()
    {
        var builtIns = new BuiltInCommandCatalog().GetCommands().SelectMany(item => item.AllPhrases)
            .Select(BuiltInCommandRouter.Normalize).ToArray();
        foreach (var alias in ReadOnlyCapabilityCatalog.ExactCommands)
        {
            ReadOnlyCapabilityCatalog.MatchCommand(alias.Key)!.Id.Should().Be(alias.Value);
            builtIns.Should().NotContain(alias.Key);
        }
        foreach (var descriptor in ReadOnlyCapabilityCatalog.Descriptors)
        {
            foreach (var prefix in new[] { "describe capability ", "capabilities get " })
            {
                var command = ReadOnlyCapabilityCatalog.MatchCommand(prefix + BuiltInCommandRouter.Normalize(descriptor.Id));
                command!.Id.Should().Be(ReadOnlyCapabilityCatalog.Get);
                command.TargetId.Should().Be(descriptor.Id);
            }
        }
        ReadOnlyCapabilityCatalog.MatchCommand("describe capability computer lock")!.TargetId.Should().Be("unknown");
        ReadOnlyCapabilityCatalog.MatchCommand("please list capabilities").Should().BeNull();
        ReadOnlyCapabilityCatalog.MatchCommand("lock the machine").Should().BeNull();
        ReadOnlyCapabilityCatalog.MatchCommand("runtime get status")!.TargetId.Should().Be("local.inference");
        ReadOnlyCapabilityCatalog.MatchCommand("runtime list extra")!.InvalidInput.Should().BeTrue();
        Enum.GetValues<BuiltInAction>().Should().HaveCount(25);
    }
}
