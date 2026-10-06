using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Kora.Mg1;

internal sealed record Reply(string Kind, string Content = "", int Status = 200);
internal sealed record WireRequest(int Bytes, string Body, bool CredentialInHeader);

internal sealed class LoopbackProvider : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(3));
    private readonly ConcurrentQueue<Reply> replies = new();
    private readonly ConcurrentBag<TcpClient> clients = [];
    private readonly ConcurrentBag<Task> handlers = [];
    private Task? acceptLoop;
    internal ConcurrentQueue<WireRequest> Requests { get; } = new();
    internal ConcurrentQueue<string> Errors { get; } = new();
    internal int ConnectionsTerminated;
    internal Uri BaseUri { get; private set; } = null!;

    internal void Start()
    {
        listener.Start();
        BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1");
        acceptLoop = AcceptAsync();
    }
    internal void Enqueue(Reply reply) => replies.Enqueue(reply);
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
            var header = new List<byte>();
            var single = new byte[1];
            while (header.Count < 16384)
            {
                if (await stream.ReadAsync(single, lifetime.Token) == 0)
                    throw new EndOfStreamException("Incomplete HTTP header.");
                header.Add(single[0]);
                if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
            }
            var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            if (header.Count >= 16384 || !lines[0].StartsWith("POST /v1/chat/completions ", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected owned-provider request.");
            var length = int.Parse(lines.Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .AsSpan(15).Trim(), CultureInfo.InvariantCulture);
            if (length is <= 0 or > 262144) throw new InvalidDataException("Unbounded provider body.");
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, lifetime.Token);
            var text = new UTF8Encoding(false, true).GetString(body);
            using var json = JsonDocument.Parse(body);
            Requests.Enqueue(new WireRequest(length, text, lines.Any(line =>
                line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)
                && line.Contains(Envelope.Credential, StringComparison.Ordinal))));
            if (!replies.TryDequeue(out var reply)) throw new InvalidDataException("No scripted provider reply.");
            if (reply.Kind == "error")
            {
                const string error = "{\"error\":{\"message\":\"synthetic-failure\"}}";
                await WriteAsync(stream, $"HTTP/1.1 {reply.Status} Synthetic\r\nContent-Type: application/json\r\nContent-Length: {error.Length}\r\nConnection: close\r\n\r\n{error}");
                return;
            }
            await WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n");
            if (reply.Kind == "hold")
            {
                if (await stream.ReadAsync(single, lifetime.Token) != 0)
                    throw new InvalidDataException("Unexpected held-request bytes.");
                return;
            }
            await EmitAsync(stream, new { role = "assistant" });
            if (reply.Kind == "tool")
            {
                await EmitAsync(stream, new
                {
                    tool_calls = new[] { new { index = 0, id = "synthetic-call", type = "function",
                        function = new { name = "mg1_owned_effect", arguments = reply.Content } } }
                });
                await EmitAsync(stream, new { }, "tool_calls");
            }
            else
            {
                // Split by Unicode scalar rather than cutting a surrogate pair.
                foreach (var rune in reply.Content.EnumerateRunes())
                    await EmitAsync(stream, new { content = rune.ToString() });
                await EmitAsync(stream, new { }, "stop");
            }
            await WriteAsync(stream, "data: [DONE]\n\n");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (IOException exception) { Errors.Enqueue(exception.GetType().Name); }
        catch (SocketException exception) { Errors.Enqueue(exception.GetType().Name); }
        catch (InvalidDataException exception) { Errors.Enqueue(exception.Message); }
        finally { client.Dispose(); Interlocked.Increment(ref ConnectionsTerminated); }
    }
    private Task EmitAsync<T>(NetworkStream stream, T delta, string? finish = null) =>
        WriteAsync(stream, "data: " + JsonSerializer.Serialize(new
        {
            id = "untrusted-provider-correlation", @object = "chat.completion.chunk", created = 1,
            model = "synthetic-only", choices = new[] { new { index = 0, delta, finish_reason = finish } }
        }) + "\n\n");
    private async Task WriteAsync(NetworkStream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), lifetime.Token);
        await stream.FlushAsync(lifetime.Token);
    }
    internal async Task WaitForRequestsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Requests.Count < count) await Task.Delay(10, timeout.Token);
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
