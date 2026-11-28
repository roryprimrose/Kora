using System.Text;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.AI;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    public async Task<ClaudeTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token)
    {
        var turnLifetime = ModelTurnLifetime.For(provenance.Request);
        using var deadline = new ModelTurnDeadline(turnLifetime, TimeSpan.FromMinutes(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, lifetime.Token);
        token = linked.Token;
        await operation.WaitAsync(token).ConfigureAwait(false);
        HttpClient? http = null;
        ModelTokenUsage? tokenUsage = null;
        void Quarantine() => quarantined = true;
        try
        {
            if (disposed || quarantined || profile.Service != ProviderService.Claude
                || provenance.Request.Origin != RequestOrigin.LocalUi || provenance.Request.InvocationId is not null
                || !ReferenceEquals(context.Request, provenance.Request) || context.ToolMode != ModelToolMode.None
                || !context.Evidence.IsEmpty || context.McpTool is not null || context.WebPages is not null
                || context.Constraints is not null || !await current(token).ConfigureAwait(false))
            { return new(ModelTurnOutcome.DeniedEgress); }
            context.RequireCompleteInputLimit();
            var account = await accounts.ResolveAsync(profile, token).ConfigureAwait(false);
            if (account is null) { return new(ModelTurnOutcome.Unavailable); }
            using var revoked = CancellationTokenSource.CreateLinkedTokenSource(token, account.Revocation);
            token = revoked.Token;
            await using var renewal = HostedLeaseRenewal.Start(turnLifetime, () => account.Catalogue.ValidUntil,
                ct => accounts.RenewAsync(account, ct), time, exception => LeaseRenewalFailed(logger, exception.GetType().Name), token);
            var model = ProviderProfileRules.Resolve(profile, account.Catalogue, time.GetUtcNow());
            if (model?.Selection != provenance.Selection
                || model.Context.Single(option => option.Id == profile.Context).InputTokenLimit is not { } inputLimit
                || !account.OutputLimits.TryGetValue(profile.Model, out var outputLimit)
                || (!account.Approval.HasCurrentDevelopmentPreview
                    && !account.Approval.TextStreams.Contains(new(profile.Model, profile.Reasoning, profile.Context, inputLimit, outputLimit))))
            { return new(ModelTurnOutcome.Unavailable); }
            async ValueTask<bool> Live(CancellationToken ct) => !disposed && !quarantined && !ct.IsCancellationRequested
                && (!account.Approval.IsDevelopmentPreview || account.Approval.HasCurrentDevelopmentPreview)
                && ContextLive(context, turnLifetime)
                && await accounts.CurrentAsync(account, ct).ConfigureAwait(false)
                && await current(ct).ConfigureAwait(false)
                && !disposed && !quarantined && !ct.IsCancellationRequested
                && (!account.Approval.IsDevelopmentPreview || account.Approval.HasCurrentDevelopmentPreview)
                && ContextLive(context, turnLifetime);
            http = create();
            var sender = new ClaudeFinalRequestHandler(http, accounts);
            using var handler = new ClaudeSdkRequestHandler(sender, account, provenance, profile, inputLimit,
                outputLimit, admit, Live, Quarantine, usage => tokenUsage = usage, streamAnswer: true);
            try
            {
                using var sdkHttp = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                using var sdk = ClaudeSdkClient.Create(sdkHttp, account.Credential, turnLifetime: turnLifetime);
                using var chat = sdk.AsIChatClient(profile.Model.Value, outputLimit);
                var answer = new StringBuilder();
                ChatFinishReason? finishReason = null;
                await foreach (var update in chat.GetStreamingResponseAsync(
                    [new ChatMessage(ChatRole.System, context.SystemPolicy), new ChatMessage(ChatRole.User, context.UserRequest)],
                    CreateChatOptions(profile, outputLimit), token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (update.Role != ChatRole.Assistant
                        || !string.Equals(update.ModelId, profile.Model.Value, StringComparison.Ordinal)
                        || update.FinishReason is { } reason && reason != ChatFinishReason.Stop
                        || update.Contents.Any(content => content is not (TextContent or UsageContent)))
                    { throw new InvalidDataException("SDK returned unsupported streaming answer content."); }
                    finishReason = update.FinishReason ?? finishReason;
                    if (string.IsNullOrEmpty(update.Text)) { continue; }
                    answer.Append(update.Text);
                    if (Encoding.UTF8.GetByteCount(answer.ToString()) > ModelContextEnvelope.MaximumOutputUtf8Bytes)
                    { throw new InvalidDataException("SDK answer exceeds its byte budget."); }
                    if (!await Live(token).ConfigureAwait(false))
                    { throw new InvalidDataException("Claude streaming authority changed before provisional publication."); }
                    var publication = new ModelAnswerUpdate(provenance, answer.ToString());
                    publication.Validate();
                    await publish(publication, token).ConfigureAwait(false);
                }
                if (!handler.StreamCompleted || finishReason != ChatFinishReason.Stop
                    || string.IsNullOrWhiteSpace(answer.ToString()))
                { throw new InvalidDataException("SDK stream did not produce a complete normal text answer."); }
                await handler.DisposeStreamAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return await Live(token).ConfigureAwait(false)
                    ? new(ModelTurnOutcome.Succeeded, answer.ToString()) { TokenUsage = tokenUsage }
                    : new(ModelTurnOutcome.DeniedEgress) { TokenUsage = tokenUsage };
            }
            catch (Exception) when (handler.Denied) { return new(ModelTurnOutcome.DeniedEgress); }
            finally { await handler.DisposeStreamAsync().ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(ModelTurnOutcome.Cancelled) { TokenUsage = tokenUsage }; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
            or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException
            or EncoderFallbackException or DecoderFallbackException or Anthropic.Exceptions.AnthropicException)
        {
            RequestFailed(logger, exception.GetType().Name);
            return new(ModelTurnOutcome.Unknown) { TokenUsage = tokenUsage };
        }
        finally
        {
            try { ClaudeAnswerStream.DisposeOwned(http, Quarantine); }
            finally { operation.Release(); }
        }
    }
}
