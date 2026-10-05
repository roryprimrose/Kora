using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Kora.Rt1;

internal sealed record Reply(string Kind, string? Tool = null, string Arguments = "{}", int Status = 200, string? Content = null)
{
    internal static Reply Text { get; } = new("text");
    internal static Reply Hold { get; } = new("hold");
}

internal sealed record CapturedRequest(string Body, bool CredentialInHeader, string Path);

internal sealed class SyntheticProvider : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(2));
    private readonly ConcurrentQueue<Reply> replies = new();
    private readonly ConcurrentBag<TcpClient> clients = [];
    private readonly ConcurrentBag<Task> handlers = [];
    private Task? acceptLoop;
    internal ConcurrentQueue<CapturedRequest> Requests { get; } = new();
    internal int ClosedConnections;
    internal int InfrastructureErrors;
    internal Uri BaseUri { get; private set; } = null!;

    internal void Start()
    {
        listener.Start();
        BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1");
        acceptLoop = AcceptAsync();
    }

    internal void Enqueue(params Reply[] values)
    {
        foreach (var value in values) replies.Enqueue(value);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                clients.Add(client);
                handlers.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true);
            var firstLine = await reader.ReadLineAsync(lifetime.Token)
                ?? throw new InvalidDataException("Empty synthetic HTTP request.");
            var length = 0;
            var credential = false;
            while (await reader.ReadLineAsync(lifetime.Token) is { Length: > 0 } line)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(line.AsSpan(15).Trim(), CultureInfo.InvariantCulture);
                if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                    credential = line.Contains(Candidate.Credential, StringComparison.Ordinal);
            }
            if (length is <= 0 or > 262144) throw new InvalidDataException("Unbounded synthetic HTTP body.");
            // All fixture wire content is ASCII JSON; fail rather than miscount multibyte HTTP framing.
            var body = new char[length];
            var read = 0;
            while (read < length)
            {
                var count = await reader.ReadAsync(body.AsMemory(read), lifetime.Token);
                if (count == 0) throw new EndOfStreamException("Incomplete synthetic HTTP request.");
                read += count;
            }
            var text = new string(body);
            if (Encoding.UTF8.GetByteCount(text) != length) throw new InvalidDataException("Fixture ASCII framing violated.");
            Requests.Enqueue(new CapturedRequest(text, credential, firstLine.Split(' ')[1]));
            if (!replies.TryDequeue(out var reply)) reply = new Reply("error", Status: 500);
            if (reply.Kind == "drop") return;
            if (reply.Kind == "redirect")
            {
                var destination = new Uri(reply.Content!);
                if (destination.Scheme != "http" || destination.Host != "127.0.0.1")
                    throw new InvalidDataException("Synthetic redirect must target owned loopback.");
                await WriteAsync(stream, $"HTTP/1.1 307 Synthetic\r\nLocation: {destination.AbsoluteUri}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                return;
            }
            if (reply.Kind == "error")
            {
                var error = "{\"error\":{\"message\":\"synthetic-provider-error\"}}";
                await WriteAsync(stream, $"HTTP/1.1 {reply.Status} Synthetic\r\nContent-Type: application/json\r\nContent-Length: {error.Length}\r\nConnection: close\r\n\r\n{error}");
                return;
            }
            await WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
            if (reply.Kind == "hold")
            {
                var buffer = new byte[1];
                if (await stream.ReadAsync(buffer, lifetime.Token) != 0)
                    throw new InvalidDataException("Unexpected body after held synthetic request.");
                return;
            }
            if (reply.Kind == "malformed")
            {
                await WriteAsync(stream, "data: {not-json}\n\n");
                return;
            }
            await EmitAsync(stream, new { role = "assistant" });
            if (reply.Kind == "tool")
            {
                await EmitAsync(stream, new
                {
                    tool_calls = new[]
                    {
                        new { index = 0, id = "synthetic-call", type = "function",
                            function = new { name = reply.Tool, arguments = reply.Arguments } }
                    }
                });
                await EmitAsync(stream, new { }, "tool_calls");
            }
            else
            {
                await EmitAsync(stream, new { content = reply.Content ?? "RT1_APPROVED_" });
                await Task.Delay(TimeSpan.FromMilliseconds(20), lifetime.Token);
                await EmitAsync(stream, new { content = "ANSWER" });
                await EmitAsync(stream, new { }, "stop");
            }
            await WriteAsync(stream, "data: [DONE]\n\n");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (IOException) { Interlocked.Increment(ref ClosedConnections); }
        catch (SocketException) { Interlocked.Increment(ref ClosedConnections); }
        catch (InvalidDataException) { Interlocked.Increment(ref InfrastructureErrors); }
        finally
        {
            client.Dispose();
            Interlocked.Increment(ref ClosedConnections);
        }
    }

    private Task EmitAsync<T>(NetworkStream stream, T delta, string? finish = null) =>
        WriteAsync(stream, "data: " + JsonSerializer.Serialize(new
        {
            id = "synthetic-completion", @object = "chat.completion.chunk", created = 1,
            model = "rt1-synthetic", choices = new[] { new { index = 0, delta, finish_reason = finish } }
        }) + "\n\n");

    private async Task WriteAsync(NetworkStream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), lifetime.Token);
        await stream.FlushAsync(lifetime.Token);
    }

    internal async Task WaitForRequestsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Requests.Count < count) await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        listener.Stop();
        foreach (var client in clients) client.Dispose();
        if (acceptLoop is not null) await acceptLoop;
        await Task.WhenAll(handlers).WaitAsync(TimeSpan.FromSeconds(10));
        lifetime.Dispose();
    }
}
