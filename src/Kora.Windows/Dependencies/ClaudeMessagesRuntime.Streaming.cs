using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    public async Task<ClaudeTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, lifetime.Token);
        token = linked.Token;
        await operation.WaitAsync(token).ConfigureAwait(false);
        HttpClient? http = null;
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
            var model = ProviderProfileRules.Resolve(profile, account.Catalogue, time.GetUtcNow());
            if (model?.Selection != provenance.Selection
                || model.Context.Single(option => option.Id == profile.Context).InputTokenLimit is not { } inputLimit
                || !account.OutputLimits.TryGetValue(profile.Model, out var outputLimit)
                || !account.Approval.TextStreams.Contains(new(profile.Model, profile.Reasoning, profile.Context, inputLimit, outputLimit)))
            { return new(ModelTurnOutcome.Unavailable); }
            async ValueTask<bool> Live(CancellationToken ct) => !disposed && !quarantined && !ct.IsCancellationRequested
                && context.CreatedAt <= time.GetUtcNow() && context.ExpiresAt > time.GetUtcNow()
                && await accounts.CurrentAsync(account, ct).ConfigureAwait(false)
                && await current(ct).ConfigureAwait(false)
                && !disposed && !ct.IsCancellationRequested && context.CreatedAt <= time.GetUtcNow()
                && context.ExpiresAt > time.GetUtcNow();
            var countBody = Serialize(context, profile, null);
            var messageBody = Serialize(context, profile, outputLimit, streamAnswer: true);
            if (checked(countBody.Length + messageBody.Length) > ModelContextEnvelope.MaximumInputUtf8Bytes)
            { return new(ModelTurnOutcome.DeniedEgress); }
            http = create();
            var sender = new ClaudeFinalRequestHandler(http, accounts);
            var counted = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                "/v1/messages/count_tokens", countBody), account, admit, Live, token,
                read: (content, ct) => ClaudeAnswerStream.ReadCountAsync(content, Quarantine, ct),
                quarantine: Quarantine).ConfigureAwait(false);
            if (counted is null) { return new(ModelTurnOutcome.DeniedEgress); }
            using var count = Parse(counted);
            if (count.RootElement.EnumerateObject().Count() != 1
                || !count.RootElement.GetProperty("input_tokens").TryGetInt32(out var input)
                || input <= 0 || input > inputLimit || counted.Length >= ModelContextEnvelope.MaximumOutputUtf8Bytes)
            { return new(ModelTurnOutcome.DeniedEgress); }
            var response = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                "/v1/messages", messageBody), account, admit, Live, token,
                read: (content, ct) => ClaudeAnswerStream.ReadAsync(content, profile, inputLimit, outputLimit,
                    ModelContextEnvelope.MaximumOutputUtf8Bytes - counted.Length, async answer =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!await Live(ct).ConfigureAwait(false))
                        { throw new InvalidDataException("Claude streaming authority changed before provisional publication."); }
                        var update = new ModelAnswerUpdate(provenance, answer);
                        update.Validate();
                        await publish(update, ct).ConfigureAwait(false);
                    }, Quarantine, ct), quarantine: Quarantine).ConfigureAwait(false);
            if (response is null) { return new(ModelTurnOutcome.DeniedEgress); }
            var answer = ParseAnswer(response, profile, outputLimit, inputLimit);
            token.ThrowIfCancellationRequested();
            return await Live(token).ConfigureAwait(false)
                ? new(ModelTurnOutcome.Succeeded, answer) : new(ModelTurnOutcome.DeniedEgress);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return new(ModelTurnOutcome.Cancelled); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
            or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException
            or System.Text.EncoderFallbackException or System.Text.DecoderFallbackException)
        {
            RequestFailed(logger, exception.GetType().Name);
            return new(ModelTurnOutcome.Unknown);
        }
        finally
        {
            try { ClaudeAnswerStream.DisposeOwned(http, Quarantine); }
            finally { operation.Release(); }
        }
    }
}
