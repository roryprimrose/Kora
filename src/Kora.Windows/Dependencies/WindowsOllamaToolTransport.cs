using System.Net;
using System.Net.Http.Headers;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.Dependencies;

/// <summary>One host-bound native Ollama conversation. Missing evidence never falls back to the legacy reasoner.</summary>
public sealed partial class WindowsOllamaToolTransport : IModelToolTransport, IModelAnswerTransport
{
    internal static Uri Endpoint { get; } = new("http://127.0.0.1:11434/");
    private readonly Lock gate = new();
    private readonly HttpClient client;
    private readonly Func<ModelToolRequest, WindowsOllamaToolEndpointBinding?> resolveBinding;
    private readonly Func<WindowsOllamaToolEndpointBinding, CancellationToken, Task<bool>> observeProcess;
    private readonly TimeProvider time;
    private readonly ILogger<WindowsOllamaToolTransport> logger;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<OllamaToolWire.Exchange> exchanges = [];
    private byte[][] admittedHistory = [];
    private TaskCompletionSource? running;
    private Task? disposal;
    private bool closed;
    private volatile bool disposed;
    private WindowsOllamaToolEndpointBinding? binding;
    private ModelToolRequest? original;
    private ModelTurnProvenance? completedProvenance;
    private CancellationTokenRegistration revocation;
    private CancellationTokenRegistration callerCancellation;
    private DateTimeOffset deadline;
    private int inputBytes;
    private int outputBytes;
    private ModelClarificationQuestion? issuedQuestion;
    private int issuedQuestionIndex;

    public WindowsOllamaToolTransport()
        : this(static _ => null, TimeProvider.System, NullLogger<WindowsOllamaToolTransport>.Instance) { }

    public WindowsOllamaToolTransport(Func<ModelToolRequest, WindowsOllamaToolEndpointBinding?> resolveBinding,
        TimeProvider time, ILogger<WindowsOllamaToolTransport> logger)
        : this(CreateOwnedClient(resolveBinding, time, logger), resolveBinding,
            WindowsOllamaToolProcessObservation.IsCurrentAsync, time, logger)
    { }

    internal WindowsOllamaToolTransport(HttpClient client,
        Func<ModelToolRequest, WindowsOllamaToolEndpointBinding?> resolveBinding,
        Func<WindowsOllamaToolEndpointBinding, CancellationToken, Task<bool>> observeProcess,
        TimeProvider time, ILogger<WindowsOllamaToolTransport> logger)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.resolveBinding = resolveBinding ?? throw new ArgumentNullException(nameof(resolveBinding));
        this.observeProcess = observeProcess ?? throw new ArgumentNullException(nameof(observeProcess));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken) =>
        CompleteOwnedAsync(request, cancellationToken);

    public Task<ModelToolTransportReply> CompleteWithAnswerAsync(ModelToolRequest request,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publish);
        return CompleteOwnedAsync(request, cancellationToken, publish);
    }

    private async Task<ModelToolTransportReply> CompleteOwnedAsync(ModelToolRequest request, CancellationToken cancellationToken,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        TaskCompletionSource completion;
        lock (gate)
        {
            if (running is not null || disposed) { return new(ModelTurnOutcome.Denied, request.Provenance); }
            if (!disposed && closed && completedProvenance is not null && request.Round == 1
                && request.Provenance != completedProvenance)
            {
                exchanges.Clear();
                admittedHistory = [];
                original = null;
                binding = null;
                inputBytes = outputBytes = 0;
                issuedQuestion = null;
                closed = false;
            }
            if (closed)
            { return Finish(request, ModelTurnOutcome.Denied); }
            running = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var completed = false;
        try
        {
            var current = HostActivity.Current;
            if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
                || !ReferenceEquals(current.Request, request.Provenance.Request))
            { return Finish(request, ModelTurnOutcome.Denied); }
            using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Runtime);
            ModelToolTransportReply reply;
            try { reply = await ExchangeAsync(request, publish, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException) { reply = Finish(request, ModelTurnOutcome.Denied); }
            catch (OperationCanceledException) { reply = Finish(request, ModelTurnOutcome.Unknown); }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            { reply = Finish(request, ModelTurnOutcome.Unavailable); }
            activity.Complete(reply.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                : reply.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            completed = true;
            return reply;
        }
        finally
        {
            lock (gate)
            {
                running = null;
                if (!completed || cancellationToken.IsCancellationRequested || binding?.Revoked.IsCancellationRequested == true)
                { CloseConversation(request.Provenance); }
                completion.TrySetResult();
            }
        }
    }

    private async Task<ModelToolTransportReply> ExchangeAsync(ModelToolRequest request,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken callerToken)
    {
        // Reuse the producer's complete history, provenance, catalogue and byte policy.
        ModelToolProtocol.CreateRequest(request.Provenance, request.Profile, request.Context, request.Messages,
            request.Round, request.Budget, request.ClarificationAllowed);
        if (request.Profile is null) { return Finish(request, ModelTurnOutcome.Unavailable); }
        if (original is null)
        {
            if (request.Round != 1) { return Finish(request, ModelTurnOutcome.Denied); }
            original = request;
            binding = resolveBinding(request);
            deadline = time.GetUtcNow() + ModelToolProtocol.Deadline;
            if (binding is not null)
            {
                revocation = binding.Revoked.Register(CloseInactiveConversation);
                callerCancellation = callerToken.Register(CloseInactiveConversation);
            }
        }
        var selected = binding;
        if (selected is null) { return Finish(request, ModelTurnOutcome.Unavailable); }
        if (!Current(request)) { return Finish(request, ModelTurnOutcome.Denied); }
        if (issuedQuestion is not null && (request.Messages.Length < issuedQuestionIndex + 2
            || !ReferenceEquals(request.Messages[issuedQuestionIndex], issuedQuestion)))
        { return Finish(request, ModelTurnOutcome.Denied); }
        if (request.Messages.Length < admittedHistory.Length
            || admittedHistory.Where((wire, index) => !wire.AsSpan()
                .SequenceEqual(ModelToolProtocol.SerializeMessage(request.Messages[index]))).Any())
        { return Finish(request, ModelTurnOutcome.Denied); }
        var resultBytes = request.Messages.Skip(admittedHistory.Length)
            .Where(message => message is ModelToolResultMessage or ModelQuestionAnswerMessage)
            .Sum(result => ModelToolProtocol.SerializeMessage(result).Length);
        var remaining = deadline - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero) { return Finish(request, ModelTurnOutcome.TimedOut); }
        using var timeout = new CancellationTokenSource(remaining, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, lifetime.Token,
            selected.Revoked, timeout.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!await ObserveAsync(request, linked.Token).ConfigureAwait(false))
            { return Finish(request, ModelTurnOutcome.Denied); }
            var streaming = publish is not null && request.Context.ToolMode == ModelToolMode.None
                && !request.ClarificationAllowed && request.Round == 1;
            var body = OllamaToolWire.WriteRequest(request, selected.Model, exchanges, streaming);
            var inputCharge = Math.Max(body.Length, ModelToolProtocol.SerializeRequest(request).Length);
            var outputLimit = Math.Min(request.Budget.OutputUtf8Bytes,
                ModelToolProtocol.MaximumOutputUtf8Bytes - outputBytes - resultBytes);
            if (inputCharge > request.Budget.InputUtf8Bytes
                || inputCharge > ModelToolProtocol.MaximumInputUtf8Bytes - inputBytes || outputLimit <= 0)
            { return Finish(request, ModelTurnOutcome.Denied); }
            inputBytes += inputCharge;
            outputBytes += resultBytes;
            admittedHistory = request.Messages.Select(ModelToolProtocol.SerializeMessage).ToArray();
            var metadata = await VerifyMetadataAsync(request, outputLimit, linked.Token).ConfigureAwait(false);
            if (metadata != ModelTurnOutcome.Succeeded) { return Finish(request, metadata); }
            if (!await ObserveAsync(request, linked.Token).ConfigureAwait(false))
            { return Finish(request, ModelTurnOutcome.Denied); }
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, "api/chat"))
            {
                Content = new ByteArrayContent(body),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                linked.Token).ConfigureAwait(false);
            var status = Status(response, message, streaming);
            if (status != ModelTurnOutcome.Succeeded) { return Finish(request, status); }
            var wire = streaming
                ? await OllamaToolWire.ReadAnswerStreamAsync(response.Content, selected.Model, outputLimit, async answer =>
                {
                    if (!await ObserveAsync(request, linked.Token).ConfigureAwait(false))
                    { throw new InvalidDataException("Native stream admission changed before publication."); }
                    await publish!(new(request.Provenance, answer), linked.Token).ConfigureAwait(false);
                }, linked.Token).ConfigureAwait(false)
                : new OllamaToolWire.AnswerWire(await ReadBoundedAsync(response.Content, outputLimit, linked.Token).ConfigureAwait(false), 0);
            var parsed = OllamaToolWire.ReadReply(wire.Terminal, selected.Model, request.Round, request.ClarificationAllowed,
                request.Context.McpTool, request.Context.ToolMode);
            var outputCharge = Math.Max(Math.Max(wire.Terminal.Length, wire.Charge), ModelToolProtocol.SerializeMessage(parsed.Message).Length);
            if (outputCharge > outputLimit) { return Finish(request, ModelTurnOutcome.Denied); }
            outputBytes += outputCharge;
            if (!await ObserveAsync(request, linked.Token).ConfigureAwait(false))
            { return Finish(request, ModelTurnOutcome.Denied); }
            linked.Token.ThrowIfCancellationRequested();
            if (time.GetUtcNow() >= deadline) { return Finish(request, ModelTurnOutcome.TimedOut); }
            if (parsed is { Message: ModelAssistantToolCalls calls })
            {
                if (request.Context.ToolMode == ModelToolMode.None)
                { return Finish(request, ModelTurnOutcome.Denied); }
                var previousIds = exchanges.SelectMany(exchange => exchange.Calls.Calls).Select(call => call.CallId)
                    .ToHashSet(StringComparer.Ordinal);
                if (calls.Calls.Length > ModelToolProtocol.MaximumCalls - previousIds.Count
                    || calls.Calls.Any(call => previousIds.Contains(call.CallId)))
                { return Finish(request, ModelTurnOutcome.Denied); }
                exchanges.Add(new(calls, parsed.NativeMessage));
            }
            else if (parsed.Message is ModelClarificationQuestion question)
            {
                if (!request.ClarificationAllowed || issuedQuestion is not null
                    || request.Context.ToolMode == ModelToolMode.ReadOnly && question.Question.Options.Count == 0)
                { return Finish(request, ModelTurnOutcome.Denied); }
                issuedQuestion = question;
                issuedQuestionIndex = request.Messages.Length;
            }
            else { CloseConversation(request.Provenance); }
            TransportCompleted(logger, ModelTurnOutcome.Succeeded);
            return new(ModelTurnOutcome.Succeeded, request.Provenance, parsed.Message, new(inputCharge, outputCharge));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Finish(request, selected.Revoked.IsCancellationRequested ? ModelTurnOutcome.Denied
                : callerToken.IsCancellationRequested || lifetime.IsCancellationRequested ? ModelTurnOutcome.Cancelled
                : ModelTurnOutcome.TimedOut);
        }
    }

    private bool Current(ModelToolRequest request) =>
        binding is not null && original is not null && !disposed && !lifetime.IsCancellationRequested
        && !binding.Revoked.IsCancellationRequested && time.GetUtcNow() < binding.ExpiresAt
        && request.Provenance == original.Provenance && request.Provenance == binding.Provenance
        && request.Profile is not null && request.Profile == binding.Profile && request.Profile == original.Profile
        && request.Context.Wire.AsSpan().SequenceEqual(original.Context.Wire.AsSpan())
        && request.ClarificationAllowed == original.ClarificationAllowed
        && request.Round == exchanges.Count + (issuedQuestion is not null ? 1 : 0) + 1 && binding.IsAdmitted()
        && HostActivity.Current is { Outcome: HostOperationOutcome.Unknown } host && !host.Activity!.IsStopped
        && ReferenceEquals(host.Request, request.Provenance.Request);

    private async Task<bool> ObserveAsync(ModelToolRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Current(request) || !await observeProcess(binding!, token).ConfigureAwait(false)) { return false; }
        token.ThrowIfCancellationRequested();
        return Current(request);
    }

    private async Task<ModelTurnOutcome> VerifyMetadataAsync(ModelToolRequest request, int outputLimit, CancellationToken token)
    {
        foreach (var path in new[] { "api/version", "api/tags" })
        {
            if (!await ObserveAsync(request, token).ConfigureAwait(false)) { return ModelTurnOutcome.Denied; }
            using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint, path));
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            var status = Status(response, message);
            if (status != ModelTurnOutcome.Succeeded) { return status; }
            var bytes = await ReadBoundedAsync(response.Content, outputLimit, token).ConfigureAwait(false);
            if (string.Equals(path, "api/version", StringComparison.Ordinal))
            { OllamaToolWire.VerifyVersion(bytes, binding!.RuntimeVersion); }
            else { OllamaToolWire.VerifyModel(bytes, binding!.Model, binding.ModelDigest); }
        }
        return ModelTurnOutcome.Succeeded;
    }

    internal static ModelTurnOutcome Status(HttpResponseMessage response, HttpRequestMessage request, bool streaming = false)
    {
        if (response.RequestMessage?.RequestUri != request.RequestUri
            || (int)response.StatusCode is >= 300 and < 400)
        { return ModelTurnOutcome.DeniedEgress; }
        if (!response.IsSuccessStatusCode) { return ModelTurnOutcome.Unavailable; }
        var type = response.Content.Headers.ContentType;
        if (type is null || !(string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                || streaming && string.Equals(type.MediaType, "application/x-ndjson", StringComparison.OrdinalIgnoreCase))
            || type.CharSet is { } charset && !string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentEncoding.Count != 0)
        { return ModelTurnOutcome.Denied; }
        return ModelTurnOutcome.Succeeded;
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit)
        { throw new InvalidDataException("The complete native response exceeds the remaining host output budget."); }
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[limit + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (read == 0) { return bytes[..count]; }
            count += read;
        }
        throw new InvalidDataException("The complete native response exceeds the remaining host output budget.");
    }

    private ModelToolTransportReply Finish(ModelToolRequest request, ModelTurnOutcome outcome)
    {
        CloseConversation(request.Provenance);
        if (HostActivity.Current is { } current && !current.Activity!.IsStopped)
        { TransportCompleted(logger, outcome); }
        return new(outcome, request.Provenance);
    }

    private void CloseInactiveConversation()
    {
        lock (gate)
        {
            if (running is null && original is not null) { CloseConversation(original.Provenance); }
        }
    }

    private void CloseConversation(ModelTurnProvenance provenance)
    {
        closed = true;
        completedProvenance = provenance;
        revocation.Unregister();
        callerCancellation.Unregister();
        exchanges.Clear();
        admittedHistory = [];
        original = null;
        binding = null;
    }

    internal static HttpClient CreateClient() => new(CreateHandler())
    { Timeout = Timeout.InfiniteTimeSpan };

    private static HttpClient CreateOwnedClient(
        Func<ModelToolRequest, WindowsOllamaToolEndpointBinding?> resolveBinding,
        TimeProvider time, ILogger<WindowsOllamaToolTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(resolveBinding);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        return CreateClient();
    }

    internal static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        Credentials = null,
        AutomaticDecompression = DecompressionMethods.None,
    };

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (gate) { return new(disposal ??= DisposeOwnedAsync()); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Waits for this transport's owned exchange completion; no UI dependency or synchronous join.")]
    private async Task DisposeOwnedAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        Task? pending;
        lock (gate) { pending = running?.Task; }
        if (pending is not null) { await pending.ConfigureAwait(false); }
        if (original is not null) { CloseConversation(original.Provenance); }
        client.Dispose();
        lifetime.Dispose();
    }
}