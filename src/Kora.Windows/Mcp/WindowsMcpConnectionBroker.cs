using System.Net;
using System.Net.Sockets;
using Kora.Core.Mcp;
using Kora.Core.Network;

namespace Kora.Windows.Mcp;

/// <summary>Destination-bound HTTPS transport beneath the host's separate consent and effect policy.</summary>
public sealed class WindowsMcpConnectionBroker : IMcpConnectionBroker
{
    private readonly IQualifiedMcpAccountSource accounts;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolve;
    private readonly Func<McpEndpoint, IReadOnlyList<IPAddress>, HttpMessageHandler> createHandler;

    public WindowsMcpConnectionBroker() : this(new UnavailableAccounts()) { }

    public WindowsMcpConnectionBroker(IQualifiedMcpAccountSource accounts)
        : this(accounts, Dns.GetHostAddressesAsync, CreateHandler) { }

    internal WindowsMcpConnectionBroker(IQualifiedMcpAccountSource accounts,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<McpEndpoint, IReadOnlyList<IPAddress>, HttpMessageHandler> createHandler)
    {
        this.accounts = accounts;
        this.resolve = resolve;
        this.createHandler = createHandler;
    }

    public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return accounts.ReadAccountsAsync(token);
    }

    public async Task<IMcpConnection> ConnectAsync(McpAccountReference account, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(account);
        token.ThrowIfCancellationRequested();
        var lease = await accounts.AcquireAsync(account, token).ConfigureAwait(false);
        try
        {
            using var admission = CancellationTokenSource.CreateLinkedTokenSource(token, lease.AdmissionWithdrawn);
            await RequireCurrentAsync(lease, account, admission.Token).ConfigureAwait(false);
            var addresses = await resolve(account.Destination.Address.IdnHost, admission.Token).ConfigureAwait(false);
            if (addresses.Length == 0 || addresses.Any(address => !WebPageNetworkPolicy.IsPublic(address)))
            { throw new InvalidOperationException("The MCP destination has no exclusively public DNS binding."); }
            var pinned = addresses.Distinct().Select(CloneAddress).ToArray();
            await RequireCurrentAsync(lease, account, admission.Token).ConfigureAwait(false);
            return new McpHttpConnection(account, lease, pinned, createHandler(account.Destination, pinned));
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    internal static async Task RequireCurrentAsync(McpAccountLease lease, McpAccountReference account, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (lease.Account != account || !await lease.IsCurrentAsync(token).ConfigureAwait(false) || lease.Account != account)
        { throw new InvalidOperationException("The exact MCP qualification, account or egress admission is unavailable."); }
        token.ThrowIfCancellationRequested();
        lease.AdmissionWithdrawn.ThrowIfCancellationRequested();
    }

    internal static IPAddress CloneAddress(IPAddress address) => address.AddressFamily == AddressFamily.InterNetworkV6
        ? new(address.GetAddressBytes(), address.ScopeId) : new(address.GetAddressBytes());

    internal static SocketsHttpHandler CreateHandler(McpEndpoint endpoint, IReadOnlyList<IPAddress> addresses) => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        PreAuthenticate = false,
        ActivityHeadersPropagator = null,
        MaxResponseHeadersLength = 16,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        ConnectCallback = async (context, token) =>
        {
            if (!string.Equals(context.DnsEndPoint.Host, endpoint.Address.IdnHost, StringComparison.OrdinalIgnoreCase)
                || context.DnsEndPoint.Port != endpoint.Address.Port)
            { throw new InvalidOperationException("The MCP socket destination differs from the approved endpoint."); }
            var address = addresses[0];
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Address.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    private sealed class UnavailableAccounts : IQualifiedMcpAccountSource
    {
        public Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            throw Unavailable();
        }

        public Task<McpAccountLease> AcquireAsync(McpAccountReference account, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            throw Unavailable();
        }

        private static InvalidOperationException Unavailable() =>
            new("No delegated MCP account is qualified for the exact HTTPS transport and egress profile. No connection was attempted.");
    }
}
