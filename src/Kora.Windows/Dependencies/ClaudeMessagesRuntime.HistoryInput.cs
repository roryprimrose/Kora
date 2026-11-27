using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Microsoft.Extensions.AI;

namespace Kora.Windows.Dependencies;

public sealed partial class ClaudeMessagesRuntime
{
    public async Task<int> MeasureHistoryInputAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        ModelContextEnvelope context, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, lifetime.Token);
        token = linked.Token;
        await operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (disposed || quarantined || !HostedConversationHistoryContext.Supports(context))
            { throw new InvalidOperationException("Claude history input preparation is unavailable."); }
            context.RequireCompleteInputLimit();
            var account = await accounts.ResolveAsync(profile, token).ConfigureAwait(false);
            if (account is null || ProviderProfileRules.Resolve(profile, account.Catalogue, time.GetUtcNow())?.Selection != selection
                || !account.OutputLimits.TryGetValue(profile.Model, out var outputLimit))
            { throw new InvalidOperationException("The exact Claude history preparation account/model is unavailable."); }
            using var handler = new HistoryInputPreparationHandler();
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var sdk = ClaudeSdkClient.Create(http, account.Credential);
            using var chat = sdk.AsIChatClient(profile.Model.Value, outputLimit);
            try
            {
                await chat.GetResponseAsync(CreateMessages(context), CreateChatOptions(profile, outputLimit), token).ConfigureAwait(false);
            }
            catch (HistoryInputPreparedException prepared)
            {
                token.ThrowIfCancellationRequested();
                if (!await accounts.CurrentAsync(account, token).ConfigureAwait(false))
                { throw new InvalidOperationException("Claude history preparation account changed.", prepared); }
                return prepared.Utf8Bytes;
            }
            throw new InvalidDataException("Local Claude input preparation unexpectedly returned an answer.");
        }
        finally { operation.Release(); }
    }

    private sealed class HistoryInputPreparedException(int utf8Bytes) : Exception
    {
        internal int Utf8Bytes { get; } = utf8Bytes;
    }

    private sealed class HistoryInputPreparationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri != new Uri("https://api.anthropic.com/v1/messages")
                || request.Content is null)
            { throw new InvalidDataException("Only local SDK Messages input preparation is supported."); }
            var body = await ClaudeAccountBroker.ReadBoundedAsync(request.Content, ClaudeTransmission.MaximumBodyBytes, token)
                .ConfigureAwait(false);
            using var json = Parse(body);
            throw new HistoryInputPreparedException(checked(body.Length + ClaudeSdkRequestHandler.CreateCountBody(json.RootElement).Length));
        }
    }
}
