using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Kora.Core.Network;

namespace Kora.Windows.Network;

public sealed class WindowsWebPageTransport : IWebPageTransport
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

    public async Task<WebPageResponse> SendAsync(
        Uri address,
        IPAddress endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(endpoint);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            Credentials = null,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(
                        new IPEndPoint(endpoint, context.DnsEndPoint.Port),
                        token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Kora", "1"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            Uri? redirect = null;
            if (response.Headers.Location is { } location)
            {
                redirect = location.IsAbsoluteUri ? location : new Uri(address, location);
            }
            return new(
                (int)response.StatusCode,
                address,
                response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentType?.CharSet,
                response.Content.Headers.ContentLength,
                redirect,
                response.Content.Headers.ContentEncoding.ToArray(),
                stream,
                new ResponseOwner(response, client));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class ResponseOwner(
        HttpResponseMessage response,
        HttpClient client) : IDisposable
    {
        public void Dispose()
        {
            response.Dispose();
            client.Dispose();
        }
    }
}
