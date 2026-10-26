using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Strict incremental named SSE text profile for anthropic-version 2023-06-01.</summary>
internal sealed class ClaudeAnswerStream(ProviderProfileTuple profile, int inputLimit, int outputLimit,
    Func<string, ValueTask> publish)
{
    internal const int MaximumEvents = 128;
    private readonly StringBuilder answer = new();
    private JsonElement message;
    private int phase;
    private int blocks;
    private int deltas;
    private int outputTokens;
    private int events;
    private bool ended;
    private string? eventName;
    private byte[]? data;

    internal static Task<byte[]> ReadCountAsync(HttpContent content, Action quarantine, CancellationToken token) =>
        ReadOwnedAsync(content, ModelContextEnvelope.MaximumOutputUtf8Bytes, async stream =>
        {
            using var memory = new MemoryStream();
            var buffer = new byte[ModelContextEnvelope.MaximumOutputUtf8Bytes + 1];
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length - (int)memory.Length), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (read == 0) { return memory.ToArray(); }
                await memory.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                if (memory.Length > ModelContextEnvelope.MaximumOutputUtf8Bytes)
                { throw new InvalidDataException("Cumulative Claude output exceeds its byte budget."); }
            }
        }, quarantine, token);

    internal static Task<byte[]> ReadAsync(HttpContent content, ProviderProfileTuple profile, int inputLimit,
        int outputLimit, int limit, Func<string, ValueTask> publish, Action quarantine, CancellationToken token)
    {
        if (content.Headers.ContentType?.MediaType is not "text/event-stream"
            || content.Headers.ContentEncoding.Count != 0
            || content.Headers.ContentType.CharSet is { } charset && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Claude text streaming requires unencoded UTF-8 SSE."); }
        var decoder = new ClaudeAnswerStream(profile, inputLimit, outputLimit, publish);
        return ReadOwnedAsync(content, limit, async stream =>
        {
            var bytes = new byte[limit + 1];
            var count = 0;
            var start = 0;
            while (true)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (read == 0) { break; }
                var end = count + read;
                if (end > limit) { throw new InvalidDataException("Cumulative Claude SSE exceeds its byte budget."); }
                for (var index = count; index < end; index++)
                {
                    if (bytes[index] != (byte)'\n') { continue; }
                    var length = index - start;
                    if (length > 0 && bytes[index - 1] == (byte)'\r') { length--; }
                    await decoder.LineAsync(bytes.AsMemory(start, length), token).ConfigureAwait(false);
                    start = index + 1;
                }
                count = end;
            }
            if (start != count || decoder.eventName is not null || decoder.data is not null || decoder.phase != 5)
            { throw new InvalidDataException("Claude SSE ended without a complete, unique normal terminal."); }
            var terminal = decoder.Terminal();
            if (terminal.Length > limit) { throw new InvalidDataException("Complete Claude answer exceeds its byte budget."); }
            return terminal;
        }, quarantine, token);
    }

    private static async Task<byte[]> ReadOwnedAsync(HttpContent content, int limit,
        Func<Stream, Task<byte[]>> read, Action quarantine, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit)
        { throw new InvalidDataException("Claude output exceeds its byte budget."); }
        var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); return await read(stream).ConfigureAwait(false); }
        finally { await DisposeOwnedAsync(stream, quarantine).ConfigureAwait(false); }
    }

    internal static void DisposeOwned(IDisposable? resource, Action? quarantine)
    {
        try { resource?.Dispose(); }
        catch { quarantine?.Invoke(); throw; }
    }

    private static async ValueTask DisposeOwnedAsync(Stream stream, Action quarantine)
    {
        try { await stream.DisposeAsync().ConfigureAwait(false); }
        catch { quarantine(); throw; }
    }

    private async ValueTask LineAsync(ReadOnlyMemory<byte> line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (line.IsEmpty)
        {
            if (eventName is null && data is null) { return; }
            if (eventName is null || data is null || ++events > MaximumEvents || phase == 5)
            { throw new InvalidDataException("Invalid, excessive or post-terminal Claude event."); }
            using var json = ClaudeMessagesRuntime.Parse(data);
            var root = json.RootElement;
            if (!string.Equals(root.GetProperty("type").GetString(), eventName, StringComparison.Ordinal))
            { throw new InvalidDataException("Claude named event does not match its JSON type."); }
            await ConsumeAsync(eventName, root).ConfigureAwait(false);
            eventName = null;
            data = null;
            return;
        }
        if (line.Span.StartsWith("event: "u8) && eventName is null && data is null)
        { eventName = new UTF8Encoding(false, true).GetString(line.Span[7..]); }
        else if (line.Span.StartsWith("data: "u8) && eventName is not null && data is null)
        { data = line.Span[6..].ToArray(); }
        else { throw new InvalidDataException("Unsupported Claude SSE framing."); }
    }

    private async ValueTask ConsumeAsync(string type, JsonElement root)
    {
        switch (type)
        {
            case "ping":
                Fields(root, "type");
                return;
            case "message_start" when phase == 0:
                Fields(root, "type", "message");
                message = root.GetProperty("message").Clone();
                var start = message;
                Fields(start, "id", "type", "role", "model", "content", "stop_reason", "stop_sequence", "usage");
                if (start.GetProperty("type").GetString() is not "message" || start.GetProperty("role").GetString() is not "assistant"
                    || string.IsNullOrWhiteSpace(start.GetProperty("id").GetString())
                    || !string.Equals(start.GetProperty("model").GetString(), profile.Model.Value, StringComparison.Ordinal)
                    || start.GetProperty("content").ValueKind != JsonValueKind.Array || start.GetProperty("content").GetArrayLength() != 0
                    || start.GetProperty("stop_reason").ValueKind != JsonValueKind.Null
                    || start.GetProperty("stop_sequence").ValueKind != JsonValueKind.Null)
                { throw new InvalidDataException("Claude SSE start does not match the exact tool-free model."); }
                var usage = start.GetProperty("usage");
                if (usage.EnumerateObject().Any(property => property.Name is not
                        ("input_tokens" or "output_tokens" or "cache_creation_input_tokens" or "cache_read_input_tokens"))
                    || !usage.GetProperty("input_tokens").TryGetInt32(out var input) || input <= 0 || input > inputLimit
                    || !usage.GetProperty("output_tokens").TryGetInt32(out outputTokens) || outputTokens < 0 || outputTokens > outputLimit
                    || usage.TryGetProperty("cache_creation_input_tokens", out var created) && created.GetInt32() != 0
                    || usage.TryGetProperty("cache_read_input_tokens", out var cached) && cached.GetInt32() != 0)
                { throw new InvalidDataException("Claude SSE start exceeds the exact token profile."); }
                phase = 1;
                return;
            case "content_block_start" when phase == 1 && blocks < 64:
                Fields(root, "type", "index", "content_block");
                Index(root, blocks);
                var block = root.GetProperty("content_block");
                Fields(block, "type", "text");
                if (block.GetProperty("type").GetString() is not "text" || block.GetProperty("text").GetString() is not "")
                { throw new InvalidDataException("Only initially empty native text blocks are supported."); }
                deltas = 0;
                phase = 2;
                return;
            case "content_block_delta" when phase == 2:
                Fields(root, "type", "index", "delta");
                Index(root, blocks);
                var delta = root.GetProperty("delta");
                Fields(delta, "type", "text");
                if (delta.GetProperty("type").GetString() is not "text_delta")
                { throw new InvalidDataException("Only decoded native text deltas are supported."); }
                var text = delta.GetProperty("text").GetString() ?? throw new InvalidDataException("Missing native text delta.");
                answer.Append(text);
                deltas++;
                if (new UTF8Encoding(false, true).GetByteCount(answer.ToString()) > ModelContextEnvelope.MaximumOutputUtf8Bytes)
                { throw new InvalidDataException("Decoded Claude answer exceeds its byte budget."); }
                if (text.Length != 0) { await publish(answer.ToString()).ConfigureAwait(false); }
                return;
            case "content_block_stop" when phase == 2 && deltas > 0:
                Fields(root, "type", "index");
                Index(root, blocks++);
                phase = 1;
                return;
            case "message_delta" when (phase == 1 && blocks > 0 || phase == 3) && !ended:
                Fields(root, "type", "delta", "usage");
                var changes = root.GetProperty("delta");
                Fields(changes, "stop_reason", "stop_sequence");
                var reason = changes.GetProperty("stop_reason");
                if (changes.GetProperty("stop_sequence").ValueKind != JsonValueKind.Null
                    || reason.ValueKind != JsonValueKind.Null && reason.GetString() is not "end_turn")
                { throw new InvalidDataException("Claude stream did not end normally."); }
                var counts = root.GetProperty("usage");
                Fields(counts, "output_tokens");
                if (!counts.GetProperty("output_tokens").TryGetInt32(out var output)
                    || output < outputTokens || output > outputLimit)
                { throw new InvalidDataException("Claude cumulative output tokens changed invalidly."); }
                outputTokens = output;
                ended = reason.ValueKind != JsonValueKind.Null;
                phase = 3;
                return;
            case "message_stop" when phase == 3 && ended && outputTokens > 0:
                Fields(root, "type");
                phase = 5;
                return;
            default:
                throw new InvalidDataException("Unsupported, erroneous or out-of-order Claude streaming event.");
        }
    }

    private byte[] Terminal() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        id = message.GetProperty("id").GetString(), type = "message", role = "assistant", model = profile.Model.Value,
        content = new[] { new { type = "text", text = answer.ToString() } },
        stop_reason = "end_turn", stop_sequence = (string?)null,
        usage = new { input_tokens = message.GetProperty("usage").GetProperty("input_tokens").GetInt32(), output_tokens = outputTokens },
    });

    private static void Index(JsonElement element, int expected)
    {
        if (!element.GetProperty("index").TryGetInt32(out var index) || index != expected)
        { throw new InvalidDataException("Claude content blocks are missing, duplicated or out of order."); }
    }

    private static void Fields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != names.Length
            || names.Any(name => !element.TryGetProperty(name, out _)))
        { throw new InvalidDataException("Claude event has missing or unknown fields."); }
    }
}
