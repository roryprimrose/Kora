using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal sealed class CopilotSdkClient(CopilotClientOptions options) : ICopilotSdkClient
{
    private readonly CopilotClient client = new(options);
    public Task<GetAuthStatusResponse> AuthenticateAsync(CancellationToken token) => client.GetAuthStatusAsync(token);
    public Task<IList<Model>> ModelsAsync(CancellationToken token) => client.ListModelsAsync(token);
    public async Task<ICopilotSdkSession> CreateAsync(CreateSessionOptions configuration, CancellationToken token) =>
        new Session(await client.CreateSessionAsync(configuration, token).ConfigureAwait(false), configuration.Model!);

    public Task StopAsync() => client.ForceStopAsync();
    public ValueTask DisposeAsync() => client.DisposeAsync();

    private sealed class Session(CopilotSession session, string model) : ICopilotSdkSession
    {
        private readonly Usage usage = new(model);
        public string SessionId => session.SessionId;
        public async Task<string?> SendAsync(string prompt, TimeSpan timeout, CancellationToken token)
        {
            using var subscription = session.On<AssistantUsageEvent>(usage.Observe);
            return (await session.SendAndWaitAsync(prompt, timeout, token).ConfigureAwait(false))?.Data.Content;
        }
        public async Task<string?> SendWithAnswerAsync(string prompt, TimeSpan timeout,
            Func<string, CancellationToken, ValueTask> publish, CancellationToken token)
        {
            using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var stream = new CopilotAnswerStream();
            using var usageSubscription = session.On<AssistantUsageEvent>(usage.Observe);
            using var answerSubscription = session.On<AssistantMessageDeltaEvent>(stream.Observe);
            var publication = PublishAndCancelOnFailureAsync();
            var send = SendAndCompleteAsync();
            await Task.WhenAll(send, publication).ConfigureAwait(false);
            var answer = await send.ConfigureAwait(false);
            stream.RequireFinal(answer);
            return answer;

            async Task<string?> SendAndCompleteAsync()
            {
                try { return (await session.SendAndWaitAsync(prompt, timeout, sendCancellation.Token).ConfigureAwait(false))?.Data.Content; }
                finally { stream.Complete(); }
            }

            async Task PublishAndCancelOnFailureAsync()
            {
                var completed = false;
                try
                {
                    await stream.PublishAsync(publish, token).ConfigureAwait(false);
                    completed = true;
                }
                finally
                {
                    if (!completed) { await sendCancellation.CancelAsync().ConfigureAwait(false); }
                }
            }
        }
        public ModelTokenUsage? GetTokenUsage(int modelCalls) => usage.Snapshot(modelCalls);
    }

    internal sealed class Usage(string model)
    {
        private readonly Lock gate = new();
        private readonly HashSet<Guid> events = [];
        private ModelTokenUsage? total;
        private InvalidDataException? failure;

        internal void Observe(AssistantUsageEvent observation)
        {
            lock (gate)
            {
                if (!string.IsNullOrEmpty(observation.AgentId) || !events.Add(observation.Id))
                { return; }
                try
                {
                    if (!string.Equals(observation.Data.Model, model, StringComparison.Ordinal))
                    { throw new InvalidDataException("SDK usage does not match the admitted model."); }
                    var data = observation.Data;
                    var usage = new ModelTokenUsage(data.InputTokens, data.OutputTokens, data.InputTokens);
                    usage.Validate();
                    total = total is null ? usage : usage with
                    {
                        InputTokens = checked(total.InputTokens + usage.InputTokens),
                        OutputTokens = checked(total.OutputTokens + usage.OutputTokens),
                    };
                }
                catch (InvalidDataException exception) { failure = exception; }
                catch (OverflowException exception)
                { failure = new InvalidDataException("SDK token totals exceed the supported count range.", exception); }
            }
        }

        internal ModelTokenUsage? Snapshot(int modelCalls)
        {
            lock (gate)
            {
                if (failure is not null) { throw new InvalidDataException("SDK token observations are invalid.", failure); }
                // Missing per-call events cannot be represented as complete totals or a known latest request.
                return modelCalls > 0 && events.Count == modelCalls ? total : null;
            }
        }
    }
}
