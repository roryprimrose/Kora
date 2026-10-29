using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    public async Task<IModelToolTransport?> OpenToolTurnAsync(ModelTurnProvenance provenance,
        ProviderProfileTuple profile, ModelContextEnvelope context,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        await operation.WaitAsync(token).ConfigureAwait(false);
        var transferred = false;
        try
        {
            if (disposed || quarantined || profile.Service != ProviderService.Claude
                || !ReferenceEquals(context.Request, provenance.Request)
                || context.ToolMode is not (ModelToolMode.ReadOnly or ModelToolMode.HostedClarification)
                || !ClaudeObservationTools.Supports(context)
                || context.CreatedAt > time.GetUtcNow() || context.ExpiresAt <= time.GetUtcNow()
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
                inputLimit, outputLimit, admit, current, token);
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
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            this.owner = owner; this.provenance = provenance; this.profile = profile;
            this.context = context; this.account = account; this.inputLimit = inputLimit; this.outputLimit = outputLimit;
            this.admit = admit; this.current = current;
            http = owner.create();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owner.lifetime.Token, account.Revocation);
        }

        private async ValueTask<bool> Live(CancellationToken token) =>
            !disposed && !owner.disposed && !token.IsCancellationRequested
            && context.CreatedAt <= owner.time.GetUtcNow() && context.ExpiresAt > owner.time.GetUtcNow()
            && await owner.accounts.CurrentAsync(account, token).ConfigureAwait(false)
            && await current(token).ConfigureAwait(false)
            && !disposed && !owner.disposed && !token.IsCancellationRequested
            && context.ExpiresAt > owner.time.GetUtcNow();

        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellation.Token);
            var token = linked.Token;
            await exchange.WaitAsync(token).ConfigureAwait(false);
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
                var countBody = ClaudeObservationWire.Serialize(request, null);
                var messageBody = ClaudeObservationWire.Serialize(request, outputLimit);
                var chargedInput = Math.Max(ModelToolProtocol.SerializeRequest(request).Length,
                    checked(countBody.Length + messageBody.Length));
                if (chargedInput > request.Budget.InputUtf8Bytes)
                { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                var sender = new ClaudeFinalRequestHandler(http, owner.accounts);
                var counted = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                    "/v1/messages/count_tokens", countBody), account, admit, Live, token,
                    request.Budget.OutputUtf8Bytes).ConfigureAwait(false);
                if (counted is null) { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                using var count = Parse(counted);
                if (count.RootElement.EnumerateObject().Count() != 1
                    || !count.RootElement.GetProperty("input_tokens").TryGetInt32(out var tokens)
                    || tokens <= 0 || tokens > inputLimit || counted.Length >= request.Budget.OutputUtf8Bytes)
                { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                var response = await sender.SendAsync(ClaudeFinalRequestHandler.Prepare(provenance, profile, account,
                    "/v1/messages", messageBody), account, admit, Live, token,
                    request.Budget.OutputUtf8Bytes - counted.Length).ConfigureAwait(false);
                if (response is null) { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                var assistant = ClaudeObservationWire.Parse(response, profile, outputLimit, inputLimit, request.Context.Tools,
                    request.ClarificationAllowed && request.Messages.IsEmpty);
                token.ThrowIfCancellationRequested();
                if (!await Live(token).ConfigureAwait(false)) { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                var chargedOutput = Math.Max(ModelToolProtocol.SerializeMessage(assistant).Length,
                    checked(counted.Length + response.Length));
                if (chargedOutput > request.Budget.OutputUtf8Bytes) { return new(ModelTurnOutcome.DeniedEgress, provenance); }
                return new(ModelTurnOutcome.Succeeded, provenance, assistant, new(chargedInput, chargedOutput));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { return new(ModelTurnOutcome.Cancelled, provenance); }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException
                or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                RequestFailed(owner.logger, exception.GetType().Name);
                return new(ModelTurnOutcome.Unknown, provenance);
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
                http.Dispose();
                if (stopFailure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopFailure).Throw(); }
            }
            catch { owner.quarantined = true; throw; }
            finally { cancellation.Dispose(); exchange.Release(); exchange.Dispose(); owner.operation.Release(); }
        }
    }
}
