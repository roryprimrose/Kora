using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

/// <summary>Validates the qualified raw SSE profile before bytes enter the SDK. The host owns asynchronous disposal.</summary>
internal sealed class ClaudeAnswerStream(Stream source, ProviderProfileTuple profile, int inputLimit, int outputLimit,
    int limit, Action quarantine, Action<ModelTokenUsage>? observeUsage = null) : Stream
{
    internal const int MaximumEvents = 128;
    private readonly byte[] bytes = new byte[limit + 1];
    private int count;
    private int lineStart;
    private int decodedBytes;
    private int inputTokens;
    private int phase;
    private int blocks;
    private int deltas;
    private int outputTokens;
    private int events;
    private bool ended;
    private string? eventName;
    private byte[]? data;
    private bool released;
    internal bool Completed { get; private set; }

    public override bool CanRead => !released;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(released, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty || Completed) { return 0; }
        var read = await source.ReadAsync(bytes.AsMemory(count, Math.Min(buffer.Length, bytes.Length - count)),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (read == 0)
        {
            if (lineStart != count || eventName is not null || data is not null || phase != 5)
            { throw new InvalidDataException("Claude SSE ended without a complete, unique normal terminal."); }
            Completed = true;
            return 0;
        }
        var end = count + read;
        if (end > limit) { throw new InvalidDataException("Cumulative Claude SSE exceeds its byte budget."); }
        for (var index = count; index < end; index++)
        {
            if (bytes[index] != (byte)'\n') { continue; }
            var length = index - lineStart;
            if (length > 0 && bytes[index - 1] == (byte)'\r') { length--; }
            Line(bytes.AsMemory(lineStart, length), cancellationToken);
            lineStart = index + 1;
        }
        bytes.AsMemory(count, read).CopyTo(buffer);
        count = end;
        return read;
    }

    public override async ValueTask DisposeAsync()
    {
        if (released) { return; }
        released = true;
        try { await DisposeOwnedAsync(source, quarantine).ConfigureAwait(false); }
        finally { await base.DisposeAsync().ConfigureAwait(false); }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    internal static void DisposeOwned(IDisposable? resource, Action? quarantine)
    {
        try { resource?.Dispose(); }
        catch { quarantine?.Invoke(); throw; }
    }

    internal static async ValueTask DisposeOwnedAsync(Stream stream, Action? quarantine)
    {
        try { await stream.DisposeAsync().ConfigureAwait(false); }
        catch { quarantine?.Invoke(); throw; }
    }

    private void Line(ReadOnlyMemory<byte> line, CancellationToken token)
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
            Consume(eventName, root);
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

    private void Consume(string type, JsonElement root)
    {
        switch (type)
        {
            case "ping":
                Fields(root, "type");
                return;
            case "message_start" when phase == 0:
                Fields(root, "type", "message");
                var start = root.GetProperty("message");
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
                inputTokens = input;
                observeUsage?.Invoke(new(input, null, input));
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
                deltas++;
                decodedBytes = checked(decodedBytes + new UTF8Encoding(false, true).GetByteCount(text));
                if (decodedBytes > ModelContextEnvelope.MaximumOutputUtf8Bytes)
                { throw new InvalidDataException("Decoded Claude answer exceeds its byte budget."); }
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
                observeUsage?.Invoke(new(inputTokens, outputTokens, inputTokens));
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
