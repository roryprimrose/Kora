using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using Kora.Windows.Network;

namespace Kora.Windows.IntegrationTests.Network;

public sealed class WindowsWebPageTransportTests
{
    [Fact]
    public async Task SendsToThePinnedEndpointWithoutFollowingRedirects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }
            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 302 Found\r\n"
                + "Location: https://example.com/final\r\n"
                + "Content-Length: 0\r\n"
                + "Connection: close\r\n\r\n");
            await stream.WriteAsync(response);
        }, TestContext.Current.CancellationToken);
        var address = new Uri($"http://not-resolved.invalid:{port}/start");
        var transport = new WindowsWebPageTransport();

        await using var response = await transport.SendAsync(
            address,
            IPAddress.Loopback,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(302);
        response.Address.Should().Be(address);
        response.RedirectAddress.Should().Be(new Uri("https://example.com/final"));
        await server;
    }

    [Fact]
    public async Task ResolvesHostAddressesThroughTheExplicitResolverSeam()
    {
        var addresses = await new WindowsWebPageTransport().ResolveAsync(
            "localhost",
            TestContext.Current.CancellationToken);

        addresses.Should().NotBeEmpty();
        addresses.Should().OnlyContain(address => IPAddress.IsLoopback(address));
    }
}
