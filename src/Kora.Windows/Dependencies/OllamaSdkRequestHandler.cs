using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal sealed class OllamaSdkRequestHandler(string model, bool streaming,
    Func<byte[], CancellationToken, Task<HttpResponseMessage>> exchange,
    OllamaChatMessages.NativeRequest? nativeRequest = null, JsonElement? outputSchema = null) : HttpMessageHandler
{
    private int sent;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Interlocked.Exchange(ref sent, 1) != 0 || request.Method != HttpMethod.Post
            || request.RequestUri != new Uri(WindowsOllamaToolTransport.Endpoint, "api/chat")
            || request.Headers.Any() || request.Content is null)
        { throw new InvalidDataException("Only one explicit SDK chat request is admitted."); }
        var bytes = await WindowsOllamaToolTransport.ReadBoundedAsync(request.Content,
            ModelToolProtocol.MaximumInputUtf8Bytes * 2, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.EnumerateObject().Any(field => field.Name is not
                ("model" or "messages" or "stream" or "think" or "options" or "CustomHeaders")
                && !(nativeRequest is not null && field.Name is "tools" or "format")
                && !(outputSchema is not null && field.Name is "format"))
            || !string.Equals(root.GetProperty("model").GetString(), model, StringComparison.Ordinal)
            || root.GetProperty("stream").ValueKind != (streaming ? JsonValueKind.True : JsonValueKind.False)
            || root.GetProperty("think").ValueKind != JsonValueKind.False
            || root.GetProperty("messages").ValueKind != JsonValueKind.Array
            || root.TryGetProperty("options", out var options)
                && (options.ValueKind != JsonValueKind.Object || options.EnumerateObject().Any())
            || root.TryGetProperty("CustomHeaders", out var headers)
                && (headers.ValueKind != JsonValueKind.Object || headers.EnumerateObject().Any()))
        { throw new InvalidDataException("SDK chat request exceeds its host-selected profile."); }
        if (outputSchema is { } schema && (!root.TryGetProperty("format", out var format)
            || !JsonElement.DeepEquals(format, schema)))
        { throw new InvalidDataException("The SDK did not preserve the exact host-selected proposal schema. No fallback is admitted."); }

        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            foreach (var field in root.EnumerateObject())
            {
                if (field.Name is "messages" && nativeRequest is not null)
                {
                    writer.WriteStartArray("messages");
                    var index = 0;
                    foreach (var message in field.Value.EnumerateArray())
                    {
                        writer.WriteStartObject();
                        foreach (var property in message.EnumerateObject()) { property.WriteTo(writer); }
                        if (nativeRequest.ResultCallIds.TryGetValue(index++, out var callId))
                        {
                            if (message.GetProperty("role").GetString() is not "tool"
                                || message.TryGetProperty("tool_call_id", out _))
                            { throw new InvalidDataException("SDK tool result correlation changed."); }
                            writer.WriteString("tool_call_id", callId);
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                else if (field.Name is not ("CustomHeaders" or "options")) { field.WriteTo(writer); }
            }
            // These required context-integrity controls are not exposed by OllamaSharp.
            writer.WriteBoolean("truncate", false);
            writer.WriteBoolean("shift", false);
            writer.WriteEndObject();
        }
        var response = await exchange(memory.ToArray(), token).ConfigureAwait(false);
        response.RequestMessage = request;
        return response;
    }
}
