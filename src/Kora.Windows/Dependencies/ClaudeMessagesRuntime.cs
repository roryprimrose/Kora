using System.Text;
using System.Text.Json;
using Anthropic.Models.Messages;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;

namespace Kora.Windows.Dependencies;

/// <summary>Bounded, tool-free, stateless native Messages profile. All HTTP work remains owned until joined.</summary>
public sealed partial class ClaudeMessagesRuntime : IClaudeToolTurnRuntime, IClaudeAnswerTurnRuntime, IClaudeCustomizationTurnRuntime,
    IClaudeHistoryInputPreparation, IAsyncDisposable
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

    public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token) =>
        RunCoreAsync(provenance, profile, context, null, admit, current, token);

    private async Task<ClaudeTurnReply> RunCoreAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, SelectedHostedDefinition? selection,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        var turnLifetime = ModelTurnLifetime.For(provenance.Request);
        using var deadline = new ModelTurnDeadline(turnLifetime, TimeSpan.FromMinutes(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, lifetime.Token);
        token = linked.Token;
        await operation.WaitAsync(token).ConfigureAwait(false);
        ModelTokenUsage? tokenUsage = null;
        void Quarantine() => quarantined = true;
        try
        {
            if (disposed || quarantined || profile.Service != ProviderService.Claude
                || !ReferenceEquals(context.Request, provenance.Request) || context.ToolMode != ModelToolMode.None
                || !context.Evidence.IsEmpty && !Kora.Core.Conversation.HostedConversationHistoryContext.Supports(context)
                || context.McpTool is not null
                || !ContextLive(context, turnLifetime)
                || !await current(token).ConfigureAwait(false))
            { return new(ModelTurnOutcome.DeniedEgress); }
            context.RequireCompleteInputLimit();
            var account = await accounts.ResolveAsync(profile, token).ConfigureAwait(false);
            if (account is null) { return new(ModelTurnOutcome.Unavailable); }
            using var accountCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, account.Revocation);
            token = accountCancellation.Token;
            await using var renewal = HostedLeaseRenewal.Start(turnLifetime, () => account.Catalogue.ValidUntil,
                ct => accounts.RenewAsync(account, ct), time, exception => LeaseRenewalFailed(logger, exception.GetType().Name), token);
            var model = ProviderProfileRules.Resolve(profile, account.Catalogue, time.GetUtcNow());
            if (model?.Selection != provenance.Selection) { return new(ModelTurnOutcome.Unavailable); }
            var inputLimit = model.Context.Single(option => option.Id == profile.Context).InputTokenLimit;
            if (inputLimit is null || !account.OutputLimits.TryGetValue(profile.Model, out var outputLimit))
            { return new(ModelTurnOutcome.Unavailable); }
            var http = create();
            try
            {
                var sender = new ClaudeFinalRequestHandler(http, accounts);
                async ValueTask<bool> Live(CancellationToken ct) => !disposed && !quarantined && !ct.IsCancellationRequested
                    && ContextLive(context, turnLifetime) && await accounts.CurrentAsync(account, ct).ConfigureAwait(false)
                    && await current(ct).ConfigureAwait(false)
                    && !disposed && !quarantined && !ct.IsCancellationRequested && ContextLive(context, turnLifetime);
                using var handler = new ClaudeSdkRequestHandler(sender, account, provenance, profile,
                    inputLimit.Value, outputLimit, admit, Live, Quarantine, usage => tokenUsage = usage,
                    selectedContext: selection is null ? null : context, selection: selection,
                    historyContext: HasHostedHistory(context) ? context : null);
                using var sdkHttp = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                using var sdk = ClaudeSdkClient.Create(sdkHttp, account.Credential, turnLifetime: turnLifetime);
                using var chat = sdk.AsIChatClient(profile.Model.Value, outputLimit);
                ChatResponse response;
                try
                {
                    response = await chat.GetResponseAsync(CreateMessages(context, selection),
                        CreateChatOptions(profile, outputLimit), token).ConfigureAwait(false);
                }
                catch (Exception) when (handler.Denied) { return new(ModelTurnOutcome.DeniedEgress); }
                if (response.FinishReason != ChatFinishReason.Stop || response.Messages.Count != 1
                    || response.Messages[0].Role != ChatRole.Assistant
                    || response.Messages[0].Contents.Any(content => content is not TextContent))
                { throw new InvalidDataException("SDK returned unsupported answer content."); }
                var answer = response.Text;
                if (string.IsNullOrWhiteSpace(answer) || Encoding.UTF8.GetByteCount(answer) > ModelContextEnvelope.MaximumOutputUtf8Bytes)
                { throw new InvalidDataException("Claude final answer is empty or exceeds its byte bound."); }
                token.ThrowIfCancellationRequested();
                return await Live(token).ConfigureAwait(false)
                    ? new(ModelTurnOutcome.Succeeded, answer) { TokenUsage = tokenUsage }
                    : new(ModelTurnOutcome.DeniedEgress) { TokenUsage = tokenUsage };
            }
            finally { ClaudeAnswerStream.DisposeOwned(http, Quarantine); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(ModelTurnOutcome.Cancelled) { TokenUsage = tokenUsage }; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or KeyNotFoundException or FormatException or Anthropic.Exceptions.AnthropicException)
        {
            RequestFailed(logger, exception.GetType().Name);
            return new(ModelTurnOutcome.Unknown) { TokenUsage = tokenUsage };
        }
        finally { operation.Release(); }
    }

    /// <summary>The original context is current; the owning turn's host waits pause, and check-ins remove, its expiry.</summary>
    private bool ContextLive(ModelContextEnvelope context, ModelTurnLifetime? turnLifetime) =>
        context.CreatedAt <= time.GetUtcNow()
        && time.GetUtcNow() < (turnLifetime?.Extend(context.ExpiresAt) ?? context.ExpiresAt);

    private static ChatOptions CreateChatOptions(ProviderProfileTuple profile, int outputLimit) => new()
    {
        ModelId = profile.Model.Value, MaxOutputTokens = outputLimit,
        RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = profile.Model.Value, MaxTokens = outputLimit, Messages = [],
            Thinking = new ThinkingConfigDisabled(),
            OutputConfig = profile.Reasoning.Value is "disabled" ? null
                : new OutputConfig { Effort = profile.Reasoning.Value },
        },
    };

    private static bool HasHostedHistory(ModelContextEnvelope context) => !context.Evidence.IsEmpty
        && Kora.Core.Conversation.HostedConversationHistoryContext.Supports(context);

    private static List<ChatMessage> CreateMessages(ModelContextEnvelope context, SelectedHostedDefinition? selection = null)
    {
        List<ChatMessage> messages = [new(ChatRole.System, context.SystemPolicy)];
        if (selection is not null) { messages.Add(new(ChatRole.User, selection.SourceText)); }
        messages.Add(new(ChatRole.User, HasHostedHistory(context)
            ? Encoding.UTF8.GetString(context.Serialize()) : context.UserRequest));
        return messages;
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
        ValidateTextBlocks(content);
        var answer = new StringBuilder();
        foreach (var block in content.EnumerateArray()) { answer.Append(block.GetProperty("text").GetString()); }
        var text = answer.ToString();
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > ModelContextEnvelope.MaximumOutputUtf8Bytes)
        { throw new InvalidDataException("Claude final answer is empty or exceeds its byte bound."); }
        return text;
    }

    internal static void ValidateTextBlocks(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() is < 1 or > 64)
        { throw new InvalidDataException("Claude final text blocks are missing or oversized."); }
        foreach (var block in content.EnumerateArray())
        {
            if (block.GetProperty("type").GetString() is not "text" || block.EnumerateObject().Any(property =>
                property.Name is not ("type" or "text" or "citations"))
                || block.TryGetProperty("citations", out var citations) && citations.ValueKind != JsonValueKind.Null
                    && (citations.ValueKind != JsonValueKind.Array || citations.GetArrayLength() != 0))
            { throw new InvalidDataException("Claude returned unsupported tools, thinking, citations or authority blocks."); }
            if (block.GetProperty("text").ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("Missing final text."); }
        }
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
        _ = ReadTokenUsage(root.GetProperty("usage"), outputLimit, inputLimit);
    }

    internal static ModelTokenUsage ReadTokenUsage(byte[] bytes, int outputLimit, int inputLimit)
    {
        using var json = Parse(bytes);
        return ReadTokenUsage(json.RootElement.GetProperty("usage"), outputLimit, inputLimit);
    }

    private static ModelTokenUsage ReadTokenUsage(JsonElement usage, int outputLimit, int inputLimit)
    {
        if (usage.EnumerateObject().Any(property => property.Name is not
                ("input_tokens" or "output_tokens" or "cache_creation_input_tokens" or "cache_read_input_tokens"))
            || !usage.GetProperty("output_tokens").TryGetInt32(out var output) || output <= 0 || output > outputLimit
            || !usage.GetProperty("input_tokens").TryGetInt32(out var input) || input <= 0 || input > inputLimit
            || usage.TryGetProperty("cache_creation_input_tokens", out var created) && created.GetInt32() != 0
            || usage.TryGetProperty("cache_read_input_tokens", out var cached) && cached.GetInt32() != 0)
        { throw new InvalidDataException("Claude token usage exceeds the exact profile."); }
        return new(input, output, input);
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
