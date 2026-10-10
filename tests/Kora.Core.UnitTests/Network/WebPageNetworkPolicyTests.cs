using System.Net;
using AwesomeAssertions;
using Kora.Core.Network;

namespace Kora.Core.UnitTests.Network;

public sealed class WebPageNetworkPolicyTests
{
    [Fact]
    public void ModelCapabilityRemainsExplicitlyUnavailableUntilItsToolLoopIsQualified()
    {
        WebPageCapability.Descriptor.Id.Should().Be("network.get_web_page");
        WebPageCapability.Descriptor.SchemaVersion.Should().Be(1);
        WebPageCapability.Descriptor.Available.Should().BeFalse();
        WebPageCapability.Descriptor.AvailabilityReason
            .Should().Be("parameterized-model-tool-loop-not-qualified");
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:0a00:0001::1", false)]
    public void ClassifiesOnlyPublicNetworkDestinations(string value, bool expected) =>
        WebPageNetworkPolicy.IsPublic(IPAddress.Parse(value)).Should().Be(expected);
}
