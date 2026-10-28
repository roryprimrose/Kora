using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using Kora.Core.Mcp;

namespace Kora.Windows.Mcp;

internal sealed class McpHttpConnection : IMcpConnection
{
    private readonly McpAccountLease lease;
    private readonly IPAddress[] addresses;
    private readonly HttpClient client;
    private readonly TimeProvider time;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly Lock sync = new();
    private readonly HashSet<Task> active = [];
    private Task? disposal;

    internal McpHttpConnection(McpAccountReference account, McpAccountLease lease, IPAddress[] addresses,
        HttpMessageHandler handler, TimeProvider? time = null)
    {
        this.lease = lease;
        this.addresses = addresses;
        Account = account;
        this.time = time ?? TimeProvider.System;
        client = new(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public McpAccountReference Account { get; }
    public McpEndpoint Endpoint => Account.Destination;
    public IReadOnlyList<IPAddress> RemoteAddresses => addresses.Select(WindowsMcpConnectionBroker.CloneAddress).ToArray();

    public Task<bool> IsCurrentAsync(CancellationToken token) => RunAsync(async current =>
    {
        current.ThrowIfCancellationRequested();
        var valid = lease.Account == Account && await lease.IsCurrentAsync(current).ConfigureAwait(false) && lease.Account == Account;
        current.ThrowIfCancellationRequested();
        return valid;
    }, token);

    public Task<ImmutableArray<byte>> ExchangeAsync(ImmutableArray<byte> request, int maximumResponseBytes, CancellationToken token)
    {
        if (maximumResponseBytes is < 1 or > McpProtocol.MaximumDiscoveryUtf8Bytes)
        { throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes)); }
        return RunAsync(current => PostAsync(request, maximumResponseBytes, notification: false, current), token);
    }

    public async Task NotifyAsync(ImmutableArray<byte> notification, CancellationToken token) =>
        await RunAsync(current => PostAsync(notification, 0, notification: true, current), token).ConfigureAwait(false);

    private async Task<ImmutableArray<byte>> PostAsync(ImmutableArray<byte> wire, int limit, bool notification, CancellationToken token)
    {
        if (wire.IsDefaultOrEmpty || wire.Length > McpProtocol.MaximumDiscoveryUtf8Bytes)
        { throw new InvalidDataException("The MCP request is empty or exceeds the transport byte bound."); }
        await WindowsMcpConnectionBroker.RequireCurrentAsync(lease, Account, token).ConfigureAwait(false);
        var credential = lease.AccessToken;
        if (credential.Length is < 1 or > 8192 || credential.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '.' or '_' or '~' or '+' or '/' or '=')))
        { throw new InvalidOperationException("The qualified MCP delegated credential is unavailable or malformed."); }
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint.Address)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(wire.ToArray()),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", McpProtocol.Version);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        token.ThrowIfCancellationRequested();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await WindowsMcpConnectionBroker.RequireCurrentAsync(lease, Account, token).ConfigureAwait(false);
        if (response.StatusCode != (notification ? HttpStatusCode.Accepted : HttpStatusCode.OK)
            || response.Headers.Location is not null || response.Headers.Contains("Mcp-Session-Id")
            || response.Content.Headers.ContentEncoding.Count != 0
            || response.RequestMessage?.RequestUri is { } observed && observed != Endpoint.Address)
        { throw new InvalidDataException("The MCP response requires an unsupported status, redirect, session or encoding."); }
        var type = response.Content.Headers.ContentType;
        if (!notification && (type is null || !string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || type.CharSet is { } charset && !string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase)))
        { throw new InvalidDataException("The MCP transport requires a bounded UTF-8 JSON response."); }
        if (response.Content.Headers.ContentLength is { } length && (length > limit || length < 0))
        { throw new InvalidDataException("The MCP response exceeds the complete byte bound."); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[limit + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (read == 0) { break; }
            count += read;
        }
        if (count > limit || !notification && count == 0)
        { throw new InvalidDataException("The complete MCP response is empty or exceeds its byte bound."); }
        await WindowsMcpConnectionBroker.RequireCurrentAsync(lease, Account, token).ConfigureAwait(false);
        return ImmutableArray.CreateRange(bytes.AsSpan(0, count).ToArray());
    }

    private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linked;
        CancellationTokenSource deadline;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            deadline = new(TimeSpan.FromSeconds(30), time);
            linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token, lease.AdmissionWithdrawn, deadline.Token);
            active.Add(completion.Task);
        }
        return ExecuteAsync();

        async Task<T> ExecuteAsync()
        {
            try
            {
                await serial.WaitAsync(linked.Token).ConfigureAwait(false);
                try { return await operation(linked.Token).ConfigureAwait(false); }
                finally { serial.Release(); }
            }
            finally
            {
                linked.Dispose();
                deadline.Dispose();
                lock (sync)
                {
                    completion.SetResult();
                    active.Remove(completion.Task);
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposal ??= DisposeCoreAsync([.. active]);
            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(Task[] pending)
    {
        // Publish the shared disposal task before cancellation can reenter the owner.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        finally
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
            try { client.Dispose(); }
            finally
            {
                try { lease.Dispose(); }
                finally
                {
                    serial.Dispose();
                    lifetime.Dispose();
                }
            }
        }
    }
}
