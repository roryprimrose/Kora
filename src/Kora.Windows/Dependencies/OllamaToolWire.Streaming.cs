using System.Text;
using System.Text.Json;

namespace Kora.Windows.Dependencies;

internal static partial class OllamaToolWire
{
    internal sealed record AnswerWire(byte[] Terminal, int Charge);

    internal static async Task<AnswerWire> ReadAnswerStreamAsync(HttpContent content, string model, int limit,
        Func<string, ValueTask> publish, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit)
        { throw new InvalidDataException("The complete native stream exceeds the output budget."); }
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[limit + 1];
        var count = 0;
        var start = 0;
        var answer = new StringBuilder();
        byte[]? terminal = null;

        async ValueTask Consume(byte[] line)
        {
            token.ThrowIfCancellationRequested();
            if (terminal is not null)
            { throw new InvalidDataException("Native output followed its terminal response."); }
            using var document = ReadJson(line);
            var root = document.RootElement;
            var completed = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True;
            ValidateReplyEnvelope(root, model, completed);
            if (completed ? !root.TryGetProperty("done_reason", out _) : root.TryGetProperty("done_reason", out _))
            { throw new InvalidDataException("The native stream has no normal terminal or has premature terminal metadata."); }
            var message = root.GetProperty("message");
            RequireFields(message, ["role", "content"], ["thinking"]);
            if (!string.Equals(Text(message, "role"), "assistant", StringComparison.Ordinal)
                || message.TryGetProperty("thinking", out var thinking)
                    && (thinking.ValueKind != JsonValueKind.String || thinking.GetString()!.Length != 0))
            { throw new InvalidDataException("Only decoded tool-free assistant text may be provisional."); }
            var delta = Text(message, "content");
            answer.Append(delta);
            if (completed)
            {
                using var aggregate = new MemoryStream();
                using (var writer = new Utf8JsonWriter(aggregate))
                {
                    writer.WriteStartObject();
                    foreach (var field in root.EnumerateObject())
                    {
                        if (string.Equals(field.Name, "message", StringComparison.Ordinal))
                        {
                            writer.WriteStartObject("message");
                            writer.WriteString("role", "assistant");
                            writer.WriteString("content", answer.ToString());
                            writer.WriteEndObject();
                        }
                        else { field.WriteTo(writer); }
                    }
                    writer.WriteEndObject();
                }
                terminal = aggregate.ToArray();
                if (terminal.Length > limit)
                { throw new InvalidDataException("The complete native answer exceeds the output budget."); }
                // Provisional snapshots do not substitute for canonical whole-answer validation.
                _ = ReadReply(terminal, model, 1);
            }
            if (delta.Length != 0) { await publish(answer.ToString()).ConfigureAwait(false); }
        }

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (read == 0) { break; }
            var end = count + read;
            if (end > limit) { throw new InvalidDataException("The cumulative native stream exceeds the output budget."); }
            for (var index = count; index < end; index++)
            {
                if (bytes[index] != (byte)'\n') { continue; }
                await Consume(bytes.AsSpan(start, index - start).ToArray()).ConfigureAwait(false);
                start = index + 1;
            }
            count = end;
        }
        if (start < count) { await Consume(bytes.AsSpan(start, count - start).ToArray()).ConfigureAwait(false); }
        return terminal is null
            ? throw new InvalidDataException("The native stream ended without a complete terminal response.")
            : new(terminal, count);
    }
}
