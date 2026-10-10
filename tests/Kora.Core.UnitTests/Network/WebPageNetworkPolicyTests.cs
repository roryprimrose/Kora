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
    [InlineData("0.0.0.0", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.63.255.255", true)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.127.255.255", false)]
    [InlineData("100.128.0.0", true)]
    [InlineData("169.253.1.1", true)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.15.255.255", true)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.0", true)]
    [InlineData("192.0.0.1", false)]
    [InlineData("192.0.1.1", true)]
    [InlineData("192.0.2.1", false)]
    [InlineData("192.88.99.1", false)]
    [InlineData("192.88.98.1", true)]
    [InlineData("192.168.0.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.17.255.255", true)]
    [InlineData("198.19.255.255", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("198.51.99.1", true)]
    [InlineData("203.0.113.1", false)]
    [InlineData("203.1.1.1", true)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fec0::1", false)]
    [InlineData("ff00::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001:db7::1", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("2002:0a00:0001::1", false)]
    [InlineData("1fff::1", false)]
    public void ClassifiesOnlyPublicNetworkDestinations(string value, bool expected) =>
        WebPageNetworkPolicy.IsPublic(IPAddress.Parse(value)).Should().Be(expected);

    [Fact]
    public void ClassifiesMappedPublicAddressesAndRejectsNull()
    {
        WebPageNetworkPolicy.IsPublic(IPAddress.Parse("::ffff:8.8.8.8")).Should().BeTrue();
        FluentActions.Invoking(() => WebPageNetworkPolicy.IsPublic(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ResponseDisposesContentAndOwnerSynchronouslyAndAsynchronously()
    {
        var synchronousContent = new TrackingStream();
        var synchronousOwner = new TrackingDisposable();
        using (var response = Response(synchronousContent, synchronousOwner))
        {
            response.StatusCode.Should().Be(200);
            response.Address.Should().Be(new Uri("https://example.com/"));
            response.MediaType.Should().Be("text/plain");
            response.CharacterSet.Should().Be("utf-8");
            response.ContentLength.Should().Be(0);
            response.RedirectAddress.Should().BeNull();
            response.ContentEncodings.Should().BeEmpty();
            response.Content.Should().BeSameAs(synchronousContent);
        }
        synchronousContent.Disposed.Should().BeTrue();
        synchronousOwner.Disposed.Should().BeTrue();

        var asynchronousContent = new TrackingStream();
        var asynchronousOwner = new TrackingDisposable();
        await using (Response(asynchronousContent, asynchronousOwner))
        {
        }
        asynchronousContent.AsynchronouslyDisposed.Should().BeTrue();
        asynchronousOwner.Disposed.Should().BeTrue();

        var ownerlessContent = new TrackingStream();
        Response(ownerlessContent).Dispose();
        ownerlessContent.Disposed.Should().BeTrue();

        var asynchronouslyOwnerlessContent = new TrackingStream();
        await Response(asynchronouslyOwnerlessContent).DisposeAsync();
        asynchronouslyOwnerlessContent.AsynchronouslyDisposed.Should().BeTrue();
    }

    private static WebPageResponse Response(Stream content, IDisposable? owner = null) =>
        new(200, new("https://example.com/"), "text/plain", "utf-8", 0, null, [], content, owner);

    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        public bool AsynchronouslyDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = disposing;
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            AsynchronouslyDisposed = true;
            await base.DisposeAsync();
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
