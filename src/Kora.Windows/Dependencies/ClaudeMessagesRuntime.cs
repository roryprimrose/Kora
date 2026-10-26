using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Dependencies;

/// <summary>Bounded, tool-free, stateless native Messages profile. All HTTP work remains owned until joined.</summary>
public sealed partial class ClaudeMessagesRuntime : IClaudeToolTurnRuntime, IClaudeAnswerTurnRuntime, IAsyncDisposable
{
    private readonly ClaudeAccountBroker accounts;
    private readonly TimeProvider time;
    private readonly ILogger<ClaudeMessagesRuntime> logger;
    private readonly Func<HttpClient> create;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private bool disposed;
    private volatile bool quarantined;
    public bool IsQuiescent => !quarantined && operation.CurrentCount != 0;

    public ClaudeMessagesRuntime(ClaudeAccountBroker accounts, TimeProvider time, ILogger<ClaudeMessagesRuntime> logger)
        : this(accounts, time, logger, ClaudeAccountBroker.CreateHttpClient) { }
    internal ClaudeMessagesRuntime(ClaudeAccountBroker accounts, TimeProvider time,
        ILogger<ClaudeMessagesRuntime> logger, Func<HttpClient> create)
    { this.accounts = accounts; this.time = time; this.logger = logger; this.create = create; }

    public async Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, lifetime.Token);
        token = linked.Token;
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (disposed || quarantined || profile.Service != ProviderService.Claude
                || !ReferenceEquals(context.Request, provenance.Request) || context.ToolMode != ModelToolMode.None
                || !context.Evidence.IsEmpty || context.McpTool is not null
                || context.CreatedAt > time.GetUtcNow() || context.ExpiresAt <= time.GetUtcNow()
                || !await current(token).ConfigureAwait(false))
            { return new(ModelTurnOutcome.DeniedEgress); }
            context.RequireCompleteInputLimit();
            var account = await accounts.ResolveAsync(profile, token).ConfigureAwait(false);
            if (account is null) { return new(ModelTurnOutcome.Unavailable); }
            using var accountCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, account.Revocation);
            token = accountCancellation.Token;
            var model = ProviderProfileRules.Resolve(profile, account.Catalogue, time.GetUtcNow());
            if (model?.Selection != provenance.Selection) { return new(ModelTurnOutcome.Unavailable); }
            var inputLimit = model.Context.Single(option => option.Id == profile.Context).InputTokenLimit;
            if (inputLimit is null || !account.OutputLimits.TryGetValue(profile.Model, out var outputLimit))
            { return new(ModelTurnOutcome.Unavailable); }
            var countBody = Serialize(context, profile, null);
            var messageBody = Serialize(context, profile, outputLimit);
            using var http = create();
            var sender = new ClaudeFinalRequestHandler(http, accounts);
            async ValueTask<bool> Live(CancellationToken ct) => !disposed && !ct.IsCancellationRequested
                && context.ExpiresAt > time.GetUtcNow() && await accounts.CurrentAsync(account, ct).ConfigureAwait(false)
                && await current(ct).ConfigureAwait(false)
                && !disposed && !ct.IsCancellationRequested && context.ExpiresAt > time.GetUtcNow();
            var counted = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                "/v1/messages/count_tokens", countBody), account, admit, Live, token).ConfigureAwait(false);
            if (counted is null) { return new(ModelTurnOutcome.DeniedEgress); }
            using var count = Parse(counted);
            if (count.RootElement.EnumerateObject().Count() != 1
                || !count.RootElement.GetProperty("input_tokens").TryGetInt32(out var inputTokens)
                || inputTokens <= 0 || inputTokens > inputLimit)
            { return new(ModelTurnOutcome.DeniedEgress); }
            var response = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                "/v1/messages", messageBody), account, admit, Live, token).ConfigureAwait(false);
            if (response is null) { return new(ModelTurnOutcome.DeniedEgress); }
            var answer = ParseAnswer(response, profile, outputLimit, inputLimit.Value);
            token.ThrowIfCancellationRequested();
            return await Live(token).ConfigureAwait(false) ? new(ModelTurnOutcome.Succeeded, answer)
                : new(ModelTurnOutcome.DeniedEgress);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(ModelTurnOutcome.Cancelled); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            RequestFailed(logger, exception.GetType().Name);
            return new(ModelTurnOutcome.Unknown);
        }
        finally { operation.Release(); }
    }

    internal static byte[] Serialize(ModelContextEnvelope context, ProviderProfileTuple profile, int? outputLimit,
        bool streamAnswer = false)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteString("model", profile.Model.Value);
            writer.WriteString("system", context.SystemPolicy);
            writer.WriteStartArray("messages");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", context.UserRequest);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteStartObject("thinking");
            writer.WriteString("type", "disabled");
            writer.WriteEndObject();
            if (outputLimit is not null)
            {
                writer.WriteNumber("max_tokens", outputLimit.Value);
                writer.WriteBoolean("stream", streamAnswer);
                if (profile.Reasoning.Value is not "disabled")
                {
                    writer.WriteStartObject("output_config");
                    writer.WriteString("effort", profile.Reasoning.Value);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject();
        }
        if (memory.Length > ClaudeTransmission.MaximumBodyBytes) { throw new InvalidDataException("Complete native Claude request exceeds its byte bound."); }
        return memory.ToArray();
    }

    internal static JsonDocument Parse(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        try { RequireUnique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RequireUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate Claude JSON property."); }
                RequireUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        { foreach (var item in element.EnumerateArray()) { RequireUnique(item); } }
    }

    internal static string ParseAnswer(byte[] bytes, ProviderProfileTuple profile, int outputLimit, int inputLimit)
    {
        using var json = Parse(bytes);
        var root = json.RootElement;
        ValidateMessage(root, profile, outputLimit, inputLimit, "end_turn");
        var content = root.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() is < 1 or > 64)
        { throw new InvalidDataException("Claude final text blocks are missing or oversized."); }
        var answer = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.GetProperty("type").GetString() is not "text" || block.EnumerateObject().Any(property =>
                property.Name is not ("type" or "text" or "citations"))
                || block.TryGetProperty("citations", out var citations) && citations.ValueKind != JsonValueKind.Null
                    && (citations.ValueKind != JsonValueKind.Array || citations.GetArrayLength() != 0))
            { throw new InvalidDataException("Claude returned unsupported tools, thinking, citations or authority blocks."); }
            answer.Append(block.GetProperty("text").GetString() ?? throw new InvalidDataException("Missing final text."));
        }
        var text = answer.ToString();
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > ModelContextEnvelope.MaximumOutputUtf8Bytes)
        { throw new InvalidDataException("Claude final answer is empty or exceeds its byte bound."); }
        return text;
    }

    internal static void ValidateMessage(JsonElement root, ProviderProfileTuple profile, int outputLimit, int inputLimit, string stopReason)
    {
        if (root.EnumerateObject().Any(property => property.Name is not
            ("id" or "type" or "role" or "model" or "content" or "stop_reason" or "stop_sequence" or "usage"))
            || root.GetProperty("type").GetString() is not "message" || root.GetProperty("role").GetString() is not "assistant"
            || string.IsNullOrWhiteSpace(root.GetProperty("id").GetString())
            || !string.Equals(root.GetProperty("model").GetString(), profile.Model.Value, StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("stop_reason").GetString(), stopReason, StringComparison.Ordinal)
            || root.GetProperty("stop_sequence").ValueKind != JsonValueKind.Null)
        { throw new InvalidDataException("Claude did not return an exact final text message."); }
        var usage = root.GetProperty("usage");
        if (usage.EnumerateObject().Any(property => property.Name is not
                ("input_tokens" or "output_tokens" or "cache_creation_input_tokens" or "cache_read_input_tokens"))
            || !usage.GetProperty("output_tokens").TryGetInt32(out var output) || output <= 0 || output > outputLimit
            || !usage.GetProperty("input_tokens").TryGetInt32(out var input) || input <= 0 || input > inputLimit
            || usage.TryGetProperty("cache_creation_input_tokens", out var created) && created.GetInt32() != 0
            || usage.TryGetProperty("cache_read_input_tokens", out var cached) && cached.GetInt32() != 0)
        { throw new InvalidDataException("Claude token usage exceeds the exact profile."); }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate) { return new(disposal ??= DisposeCoreAsync()); }
    }

    private async Task DisposeCoreAsync()
    {
        disposed = true;
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            await operation.WaitAsync().ConfigureAwait(false);
            operation.Release();
            ClaudeAnswerStream.DisposeOwned(lifetime, () => quarantined = true);
        }
        if (quarantined) { throw new IOException("Claude resource disposal is unconfirmed; runtime remains quarantined."); }
    }

    private async Task StopAsync()
    {
        try { await lifetime.CancelAsync().ConfigureAwait(false); }
        catch { quarantined = true; throw; }
    }
}
