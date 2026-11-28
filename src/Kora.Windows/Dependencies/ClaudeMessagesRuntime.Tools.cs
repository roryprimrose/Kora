using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Microsoft.Extensions.AI;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    public async Task<IModelToolTransport?> OpenToolTurnAsync(ModelTurnProvenance provenance,
        ProviderProfileTuple profile, ModelContextEnvelope context,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        await operation.WaitAsync(token).ConfigureAwait(false);
        var turnLifetime = ModelTurnLifetime.For(provenance.Request);
        var transferred = false;
        try
        {
            if (disposed || quarantined || profile.Service != ProviderService.Claude
                || !ReferenceEquals(context.Request, provenance.Request)
                || context.ToolMode is not (ModelToolMode.ReadOnly or ModelToolMode.HostedClarification)
                || !ClaudeObservationTools.Supports(context)
                || !ContextLive(context, turnLifetime)
                || !await current(token).ConfigureAwait(false))
            { return null; }
            context.RequireCompleteInputLimit();
            var account = await accounts.ResolveAsync(profile, token).ConfigureAwait(false);
            if (account is null) { return null; }
            var model = ProviderProfileRules.Resolve(profile, account.Catalogue, time.GetUtcNow());
            if (model?.Selection != provenance.Selection
                || model.Context.Single(option => option.Id == profile.Context).InputTokenLimit is not { } inputLimit
                || !account.OutputLimits.TryGetValue(profile.Model, out var outputLimit))
            { return null; }
            var transport = new ToolTransport(this, provenance, profile, context, account,
                inputLimit, outputLimit, admit, current, turnLifetime, token);
            transferred = true;
            return transport;
        }
        finally { if (!transferred) { operation.Release(); } }
    }

    private sealed class ToolTransport : IModelToolTransport
    {
        private readonly ClaudeMessagesRuntime owner;
        private readonly ModelTurnProvenance provenance;
        private readonly ProviderProfileTuple profile;
        private readonly ModelContextEnvelope context;
        private readonly ClaudeAccount account;
        private readonly int inputLimit;
        private readonly int outputLimit;
        private readonly Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit;
        private readonly Func<CancellationToken, ValueTask<bool>> current;
        private readonly ModelTurnLifetime? turnLifetime;
        private readonly HostedLeaseRenewal? renewal;
        private readonly CancellationTokenSource cancellation;
        private readonly HttpClient http;
        private readonly SemaphoreSlim exchange = new(1, 1);
        private readonly Lock disposalGate = new();
        private Task? disposal;
        private int nextRound = 1;
        private volatile bool disposed;

        internal ToolTransport(ClaudeMessagesRuntime owner, ModelTurnProvenance provenance,
            ProviderProfileTuple profile, ModelContextEnvelope context, ClaudeAccount account,
            int inputLimit, int outputLimit, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, ModelTurnLifetime? turnLifetime, CancellationToken token)
        {
            this.turnLifetime = turnLifetime;
            this.owner = owner; this.provenance = provenance; this.profile = profile;
            this.context = context; this.account = account; this.inputLimit = inputLimit; this.outputLimit = outputLimit;
            this.admit = admit; this.current = current;
            http = owner.create();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.lifetime.Token, account.Revocation);
            renewal = HostedLeaseRenewal.Start(turnLifetime, () => account.Catalogue.ValidUntil,
                ct => owner.accounts.RenewAsync(account, ct), owner.time,
                exception => LeaseRenewalFailed(owner.logger, exception.GetType().Name), cancellation.Token);
        }

        private async ValueTask<bool> Live(CancellationToken token) =>
            !disposed && !owner.disposed && !owner.quarantined && !token.IsCancellationRequested
            && owner.ContextLive(context, turnLifetime)
            && await owner.accounts.CurrentAsync(account, token).ConfigureAwait(false)
            && await current(token).ConfigureAwait(false)
            && !disposed && !owner.disposed && !owner.quarantined && !token.IsCancellationRequested
            && owner.ContextLive(context, turnLifetime);

        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellation.Token);
            var token = linked.Token;
            await exchange.WaitAsync(token).ConfigureAwait(false);
            ModelTokenUsage? tokenUsage = null;
            try
            {
                if (disposed || !ReferenceEquals(request.Provenance, provenance) || request.Profile != profile
                    || request.Round != nextRound++
                    || request.ClarificationAllowed != (context.ToolMode == ModelToolMode.HostedClarification)
                    || !request.Context.Wire.AsSpan().SequenceEqual(context.Serialize())
                    || !await Live(token).ConfigureAwait(false))
                { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                // Revalidate complete serial history before translating any provider-native bytes.
                _ = ModelToolProtocol.CreateRequest(provenance, profile, request.Context, request.Messages,
                    request.Round, request.Budget, request.ClarificationAllowed);
                var sender = new ClaudeFinalRequestHandler(http, owner.accounts);
                using var handler = new ClaudeSdkRequestHandler(sender, account, provenance, profile, inputLimit,
                    outputLimit, admit, Live, () => owner.quarantined = true, usage => tokenUsage = usage,
                    toolRequest: request);
                using var sdkHttp = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                // The qualified legacy tool envelope omits the SDK-required caller field; strict raw host validation is authoritative.
                using var sdk = ClaudeSdkClient.Create(sdkHttp, account.Credential, validateResponse: false, turnLifetime: turnLifetime);
                using var chat = sdk.AsIChatClient(profile.Model.Value, outputLimit);
                var options = CreateChatOptions(profile, outputLimit);
                options.Tools = ClaudeToolMessages.CreateTools(request);
                ChatResponse response;
                try
                {
                    response = await chat.GetResponseAsync(ClaudeToolMessages.CreateMessages(request),
                        options, token).ConfigureAwait(false);
                }
                catch (Exception) when (handler.Denied)
                { return new(ModelTurnOutcome.DeniedEgress, provenance) { TokenUsage = tokenUsage }; }
                var assistant = ClaudeToolMessages.Parse(response, request.Context.Tools,
                    request.ClarificationAllowed && request.Messages.IsEmpty);
                token.ThrowIfCancellationRequested();
                if (!await Live(token).ConfigureAwait(false))
                { return new(ModelTurnOutcome.DeniedEgress, provenance) { TokenUsage = tokenUsage }; }
                var chargedOutput = Math.Max(ModelToolProtocol.SerializeMessage(assistant).Length,
                    handler.PhysicalOutputUtf8Bytes);
                if (chargedOutput > request.Budget.OutputUtf8Bytes)
                { return new(ModelTurnOutcome.DeniedEgress, provenance) { TokenUsage = tokenUsage }; }
                return new(ModelTurnOutcome.Succeeded, provenance, assistant, new(handler.ChargedInputUtf8Bytes, chargedOutput))
                { TokenUsage = tokenUsage };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return new(ModelTurnOutcome.Cancelled, provenance) { TokenUsage = tokenUsage }; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException
                or InvalidOperationException or KeyNotFoundException or FormatException or Anthropic.Exceptions.AnthropicException)
            {
                RequestFailed(owner.logger, exception.GetType().Name);
                return new(ModelTurnOutcome.Unknown, provenance) { TokenUsage = tokenUsage };
            }
            finally { exchange.Release(); }
        }

        public ValueTask DisposeAsync()
        {
            lock (disposalGate) { return new(disposal ??= CloseAsync()); }
        }

        private async Task CloseAsync()
        {
            disposed = true;
            Exception? stopFailure = null;
            try { await cancellation.CancelAsync().ConfigureAwait(false); }
            catch (Exception exception) { owner.quarantined = true; stopFailure = exception; }
            await exchange.WaitAsync().ConfigureAwait(false);
            try
            {
                if (renewal is not null) { await renewal.DisposeAsync().ConfigureAwait(false); }
                http.Dispose();
                if (stopFailure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopFailure).Throw(); }
            }
            catch { owner.quarantined = true; throw; }
            finally { cancellation.Dispose(); exchange.Release(); exchange.Dispose(); owner.operation.Release(); }
        }
    }
}
