using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

internal sealed class ClaudeSdkRequestHandler(
    ClaudeFinalRequestHandler sender, ClaudeAccount account, ModelTurnProvenance provenance,
    ProviderProfileTuple profile, int inputLimit, int outputLimit,
    Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
    Func<CancellationToken, ValueTask<bool>> current, Action quarantine,
    Action<ModelTokenUsage> observeUsage, bool streamAnswer = false, ModelToolRequest? toolRequest = null) : HttpMessageHandler
{
    private int sent;
    private HttpResponseMessage? streamingResponse;
    private ClaudeAnswerStream? streamingBody;
    internal bool Denied { get; private set; }
    internal bool StreamCompleted => streamingBody?.Completed is true;
    internal int ChargedInputUtf8Bytes { get; private set; }
    internal int PhysicalOutputUtf8Bytes { get; private set; }

    internal async ValueTask DisposeStreamAsync()
    {
        var response = streamingResponse;
        streamingResponse = null;
        try
        {
            if (streamingBody is not null) { await streamingBody.DisposeAsync().ConfigureAwait(false); }
        }
        finally { ClaudeAnswerStream.DisposeOwned(response, quarantine); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Interlocked.Exchange(ref sent, 1) != 0 || request.Method != HttpMethod.Post
            || request.RequestUri != new Uri("https://api.anthropic.com/v1/messages") || request.Content is null)
        { throw new InvalidDataException("Only one stateless SDK Messages request is admitted."); }
        var body = await ClaudeAccountBroker.ReadBoundedAsync(request.Content,
            ClaudeTransmission.MaximumBodyBytes, token, quarantine).ConfigureAwait(false);
        using var message = ClaudeMessagesRuntime.Parse(body);
        var root = message.RootElement;
        if (root.EnumerateObject().Any(property => property.Name is not
                ("model" or "system" or "messages" or "thinking" or "max_tokens" or "stream" or "output_config")
                && !(toolRequest is not null && property.Name is "tools"))
            || toolRequest is not null && (streamAnswer || !root.TryGetProperty("tools", out _))
            || !string.Equals(root.GetProperty("model").GetString(), profile.Model.Value, StringComparison.Ordinal)
            || root.GetProperty("max_tokens").GetInt32() != outputLimit
            || (root.TryGetProperty("stream", out var stream) && stream.GetBoolean()) != streamAnswer
            || root.GetProperty("thinking").GetProperty("type").GetString() is not "disabled")
        { throw new InvalidDataException("SDK request exceeds the admitted profile."); }

        // Count the SDK's actual input, not a separately reconstructed approximation.
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("max_tokens" or "stream" or "output_config")) { property.WriteTo(writer); }
            }
            writer.WriteEndObject();
        }
        var countBody = memory.ToArray();
        ChargedInputUtf8Bytes = Math.Max(toolRequest is null ? 0 : ModelToolProtocol.SerializeRequest(toolRequest).Length,
            checked(countBody.Length + body.Length));
        var outputBudget = toolRequest?.Budget.OutputUtf8Bytes ?? ModelContextEnvelope.MaximumOutputUtf8Bytes;
        if (ChargedInputUtf8Bytes > (toolRequest?.Budget.InputUtf8Bytes ?? ModelContextEnvelope.MaximumInputUtf8Bytes))
        { return Deny(); }
        var counted = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
            "/v1/messages/count_tokens", countBody), account, admit, current, token,
            outputBudget, quarantine: quarantine).ConfigureAwait(false);
        if (counted is null) { return Deny(); }
        using var count = ClaudeMessagesRuntime.Parse(counted);
        if (count.RootElement.EnumerateObject().Count() != 1
            || !count.RootElement.GetProperty("input_tokens").TryGetInt32(out var inputTokens)
            || inputTokens <= 0 || inputTokens > inputLimit
            || counted.Length >= outputBudget)
        { return Deny(); }
        if (streamAnswer)
        {
            streamingResponse = await sender.OpenAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                "/v1/messages", body), account, admit, current, token, quarantine).ConfigureAwait(false);
            if (streamingResponse is null) { return Deny(); }
            var content = streamingResponse.Content;
            var limit = ModelContextEnvelope.MaximumOutputUtf8Bytes - counted.Length;
            if (content.Headers.ContentType?.MediaType is not "text/event-stream"
                || content.Headers.ContentEncoding.Count != 0 || content.Headers.ContentLength > limit
                || content.Headers.ContentType.CharSet is { } charset && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("Claude text streaming requires bounded unencoded UTF-8 SSE."); }
            streamingBody = new(await content.ReadAsStreamAsync(token).ConfigureAwait(false),
                profile, inputLimit, outputLimit, limit, quarantine, observeUsage);
            token.ThrowIfCancellationRequested();
            return new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(streamingBody)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
                },
            };
        }
        var response = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
            "/v1/messages", body), account, admit, current, token,
            outputBudget - counted.Length, quarantine: quarantine).ConfigureAwait(false);
        if (response is null) { return Deny(); }
        PhysicalOutputUtf8Bytes = checked(counted.Length + response.Length);
        observeUsage(ClaudeMessagesRuntime.ReadTokenUsage(response, outputLimit, inputLimit));
        if (toolRequest is not null)
        {
            ClaudeToolMessages.Validate(response, profile, outputLimit, inputLimit, toolRequest.Context.Tools,
                toolRequest.ClarificationAllowed && toolRequest.Messages.IsEmpty);
        }
        else
        {
            using var answer = ClaudeMessagesRuntime.Parse(response);
            ClaudeMessagesRuntime.ValidateMessage(answer.RootElement, profile, outputLimit, inputLimit, "end_turn");
            ClaudeMessagesRuntime.ValidateTextBlocks(answer.RootElement.GetProperty("content"));
        }
        return new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(response)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
    }

    private HttpResponseMessage Deny()
    {
        Denied = true;
        throw new InvalidDataException("SDK transmission was not admitted by the host.");
    }
}
