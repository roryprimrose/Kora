using AwesomeAssertions;

using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Coordination;

public sealed class InstanceLaunchPolicyTests
{
    [Theory]
    [InlineData("Activate", false, false, true)]
    [InlineData("Activate", false, true, true)]
    [InlineData("Start", false, false, true)]
    [InlineData("Start", true, true, true)]
    [InlineData("Start", false, true, false)]
    public void Only_different_Debug_start_requests_can_propose_a_handoff(
        string kind, bool sameBuild, bool incomingDebug, bool expected)
    {
        InstanceLaunchPolicy.RequiresActivation(new(InstanceProtocol.Version, kind), sameBuild, incomingDebug)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("Transfer", false, false, "")]
    [InlineData("Activate", true, false, "")]
    [InlineData("Activate", false, true, "")]
    [InlineData("Activate", false, false, "untrusted-ticket")]
    public void Activation_rejects_unsupported_or_transaction_bound_startup(
        string kind, bool epoch, bool transaction, string ticket)
    {
        var request = new InstanceMessage(InstanceProtocol.Version, kind,
            epoch ? Guid.NewGuid() : Guid.Empty, transaction ? Guid.NewGuid() : Guid.Empty, ticket);
        var act = () => InstanceLaunchPolicy.RequiresActivation(request, false, false);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Current_build_uses_its_compile_time_launch_policy()
    {
#if DEBUG
        InstanceLaunchPolicy.RequestKind.Should().Be("Start");
        InstanceLaunchPolicy.AcceptsActivation(false).Should().BeFalse();
#else
        InstanceLaunchPolicy.RequestKind.Should().Be("Activate");
        InstanceLaunchPolicy.AcceptsActivation(false).Should().BeTrue();
#endif
        InstanceLaunchPolicy.AcceptsActivation(true).Should().BeTrue();
    }

    [Fact]
    public void Automatic_recovery_is_absent_from_Release_metadata()
    {
        var assembly = typeof(WindowsInstanceCoordinator).Assembly;
        var recoveryTypes = assembly.GetTypes().Where(type =>
            string.Equals(type.Namespace, "Kora.Windows.Coordination", StringComparison.Ordinal)
            && type.Name.Contains("Debug", StringComparison.Ordinal)).ToArray();
#if DEBUG
        recoveryTypes.Should().Contain(type => string.Equals(type.Name, "DebugOwnerRecovery", StringComparison.Ordinal));
        typeof(WindowsInstanceCoordinator).GetProperty("DebugRecovery").Should().NotBeNull();
#else
        recoveryTypes.Should().BeEmpty();
        typeof(WindowsInstanceCoordinator).GetProperty("DebugRecovery").Should().BeNull();
        assembly.GetTypes().SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Should().NotContain(parameter => parameter.ParameterType.Name.Contains("DebugRecovery", StringComparison.Ordinal));
#endif
    }

    [Fact]
    public async Task Activation_only_request_round_trips_without_a_handoff_ticket()
    {
        using var stream = new MemoryStream();
        await InstanceProtocol.WriteAsync(stream, new(InstanceProtocol.Version, "Activate"),
            TestContext.Current.CancellationToken);
        stream.Position = 0;
        var request = await InstanceProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);
        InstanceLaunchPolicy.RequiresActivation(request, false, true).Should().BeTrue();
        request.Ticket.Should().BeEmpty();
        request.Transaction.Should().BeEmpty();
    }
}
