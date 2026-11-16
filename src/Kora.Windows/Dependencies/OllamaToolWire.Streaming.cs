using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal static partial class OllamaToolWire
{
    internal static async ValueTask DisposeStreamAsync(Stream stream, Action<Exception>? releaseFailed)
    {
        try { await stream.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { releaseFailed?.Invoke(exception); throw; }
    }

    internal static bool ValidateStreamChunk(byte[] line, string model, Action<ModelTokenUsage?> observeUsage)
    {
        using var document = ReadJson(line);
        var root = document.RootElement;
        var completed = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True;
        ValidateReplyEnvelope(root, model, completed);
        if (completed) { observeUsage(ReadTokenUsage(root)); }
        if (completed ? !root.TryGetProperty("done_reason", out _) : root.TryGetProperty("done_reason", out _))
        { throw new InvalidDataException("The native stream has no normal terminal or has premature terminal metadata."); }
        var message = root.GetProperty("message");
        RequireFields(message, ["role", "content"], ["thinking"]);
        if (!string.Equals(Text(message, "role"), "assistant", StringComparison.Ordinal)
            || message.TryGetProperty("thinking", out var thinking)
                && (thinking.ValueKind != JsonValueKind.String || thinking.GetString()!.Length != 0))
        { throw new InvalidDataException("Only decoded tool-free assistant text may be provisional."); }
        _ = Text(message, "content");
        return completed;
    }
}
