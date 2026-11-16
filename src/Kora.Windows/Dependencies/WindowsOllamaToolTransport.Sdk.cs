using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Dependencies;
using Kora.Core.Agents;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace Kora.Windows.Dependencies;

public sealed partial class WindowsOllamaToolTransport
{
    private async Task<ModelToolTransportReply> CompleteSdkAsync(ModelToolRequest request,
        WindowsOllamaToolEndpointBinding selected, int outputLimit, int resultBytes, Action<ModelTokenUsage?> observeUsage,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken token)
    {
        ModelTurnOutcome? denied = null;
        OllamaToolWire.Reply? validated = null;
        var inputCharge = 0;
        var physicalOutput = 0;
        var toolExchange = request.Context.ToolMode != ModelToolMode.None || request.ClarificationAllowed;
        var nativeRequest = toolExchange ? OllamaChatMessages.CreateRequest(request, selected.Model, exchanges) : null;
        var streaming = publish is not null && !toolExchange;
        JsonElement? outputSchema = request.Context.OutputFormat == ModelOutputFormat.OwnedAgentProposal
            ? OwnedAgentProfileDocument.ProposalSchema : null;
        HttpResponseMessage? physicalResponse = null;
        OllamaAnswerStream? physicalStream = null;
        using var sdkHttp = new HttpClient(new OllamaSdkRequestHandler(selected.Model, streaming, SendAsync, nativeRequest, outputSchema))
        {
            BaseAddress = Endpoint,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var sdk = new OllamaApiClient(sdkHttp, selected.Model);
        sdk.OutgoingJsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        IChatClient chat = sdk;
        try
        {
            ChatOptions options = new()
            {
                ModelId = selected.Model,
                ResponseFormat = outputSchema is { } schema ? ChatResponseFormat.ForJsonSchema(schema) : null,
                RawRepresentationFactory = _ => new ChatRequest { Think = false },
            };
            ModelAssistantMessage parsed;
            if (nativeRequest is not null)
            {
                ChatDoneResponseStream? completed = null;
                await foreach (var response in sdk.ChatAsync(nativeRequest.Request, token).ConfigureAwait(false))
                {
                    if (completed is not null || response is not ChatDoneResponseStream done)
                    { throw new InvalidDataException("A buffered SDK exchange must contain one complete response."); }
                    completed = done;
                }
                if (completed is null || validated is null)
                { throw new InvalidDataException("The SDK tool exchange did not return a validated response."); }
                parsed = OllamaChatMessages.Project(completed, request);
                if (!ModelToolProtocol.SerializeMessage(parsed).AsSpan()
                    .SequenceEqual(ModelToolProtocol.SerializeMessage(validated.Message)))
                { throw new InvalidDataException("SDK tool projection differs from the admitted native message."); }
            }
            else if (streaming)
            {
                var prefix = new StringBuilder();
                var terminal = false;
                await foreach (var update in chat.GetStreamingResponseAsync(OllamaChatMessages.CreateContext(request),
                    options, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (terminal || update.Role != Microsoft.Extensions.AI.ChatRole.Assistant
                        || !string.Equals(update.ModelId, selected.Model, StringComparison.Ordinal)
                        || update.Contents.Any(content => content is not (TextContent or UsageContent))
                        || update.RawRepresentation is not ChatResponseStream native
                        || !string.Equals(update.Text, native.Message.Content, StringComparison.Ordinal))
                    { throw new InvalidDataException("SDK streaming projection differs from admitted native text."); }
                    prefix.Append(update.Text);
                    if (Encoding.UTF8.GetByteCount(prefix.ToString()) > outputLimit)
                    { throw new InvalidDataException("The SDK answer exceeds its output budget."); }
                    terminal = native.Done;
                    if (terminal) { _ = FinalAnswer(prefix.ToString()); }
                    if (update.Text.Length == 0) { continue; }
                    if (!await ObserveAsync(request, token).ConfigureAwait(false))
                    { return Finish(request, ModelTurnOutcome.Denied); }
                    var publication = new ModelAnswerUpdate(request.Provenance, prefix.ToString());
                    publication.Validate();
                    await publish!(publication, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                if (!terminal || physicalStream?.Completed is not true)
                { throw new InvalidDataException("The SDK stream did not consume a complete normal terminal and physical EOF."); }
                physicalOutput = physicalStream.WireBytes;
                parsed = FinalAnswer(prefix.ToString());
            }
            else
            {
                var response = await chat.GetResponseAsync(OllamaChatMessages.CreateContext(request), options, token).ConfigureAwait(false);
                if (validated?.Message is not ModelFinalResponse final || !string.Equals(response.ModelId, selected.Model, StringComparison.Ordinal)
                    || response.Messages.Count != 1 || response.Messages[0].Role != Microsoft.Extensions.AI.ChatRole.Assistant
                    || response.Messages[0].Contents.Any(content => content is not TextContent)
                    || !string.Equals(response.Text, final.Answer, StringComparison.Ordinal))
                { return Finish(request, ModelTurnOutcome.Denied); }
                parsed = FinalAnswer(response.Text);
            }
            await ReleaseAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var outputCharge = Math.Max(physicalOutput, ModelToolProtocol.SerializeMessage(parsed).Length);
            if (outputCharge > outputLimit) { return Finish(request, ModelTurnOutcome.Denied); }
            outputBytes += outputCharge;
            if (!await ObserveAsync(request, token).ConfigureAwait(false))
            { return Finish(request, ModelTurnOutcome.Denied); }
            token.ThrowIfCancellationRequested();
            if (time.GetUtcNow() >= deadline) { return Finish(request, ModelTurnOutcome.TimedOut); }
            return AcceptReply(request, parsed, validated?.NativeMessage ?? [], inputCharge, outputCharge);
        }
        catch (InvalidDataException) when (denied is not null)
        { return Finish(request, denied.Value); }
        catch (JsonException exception)
        { throw new InvalidDataException("The SDK could not decode the admitted Ollama response.", exception); }
        finally { await ReleaseAsync().ConfigureAwait(false); }

        async ValueTask ReleaseAsync()
        {
            var response = physicalResponse;
            physicalResponse = null;
            try
            {
                if (physicalStream is not null) { await physicalStream.DisposeAsync().ConfigureAwait(false); }
            }
            finally { DisposeResource(response); }
        }

        async Task<HttpResponseMessage> SendAsync(byte[] body, CancellationToken cancellationToken)
        {
            if (client.DefaultRequestHeaders.Any())
            { throw new InvalidDataException("Ambient headers are not admitted by the SDK transport."); }
            var opened = await OpenChatAsync(request, body, streaming, outputLimit, resultBytes,
                cancellationToken).ConfigureAwait(false);
            inputCharge = opened.InputCharge;
            if (opened.Outcome != ModelTurnOutcome.Succeeded) { return Deny(opened.Outcome); }
            physicalResponse = opened.Response;
            if (streaming)
            {
                if (physicalResponse!.Content.Headers.ContentLength > outputLimit)
                { throw new InvalidDataException("The complete native stream exceeds its output budget."); }
                physicalStream = new(await physicalResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                    selected.Model, outputLimit, RecordReleaseFailure, observeUsage, token);
                cancellationToken.ThrowIfCancellationRequested();
                return new(HttpStatusCode.OK)
                {
                    Content = new StreamContent(physicalStream)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/x-ndjson") },
                    },
                };
            }
            var wire = await ReadBoundedAsync(physicalResponse!.Content, outputLimit, cancellationToken,
                RecordReleaseFailure).ConfigureAwait(false);
            physicalOutput = wire.Length;
            observeUsage(OllamaToolWire.ReadTokenUsage(wire, selected.Model));
            validated = OllamaToolWire.ReadReply(wire, selected.Model, request.Round, request.ClarificationAllowed,
                request.Context.McpTool, request.Context.ToolMode);
            if (!toolExchange && validated.Message is not ModelFinalResponse)
            { throw new InvalidDataException("A tool-free SDK response cannot request a tool or clarification."); }
            return new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(wire)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                },
            };
        }

        HttpResponseMessage Deny(ModelTurnOutcome outcome)
        {
            denied = outcome;
            throw new InvalidDataException("The physical SDK exchange was not admitted by the host.");
        }

        static ModelAssistantMessage FinalAnswer(string answer) =>
            ModelToolProtocol.ParseAssistant(JsonSerializer.SerializeToUtf8Bytes(new { kind = "answer", answer }));
    }
}
