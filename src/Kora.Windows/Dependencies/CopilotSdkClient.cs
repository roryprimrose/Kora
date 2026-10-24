using GitHub.Copilot;

namespace Kora.Windows.Dependencies;

internal sealed class CopilotSdkClient(CopilotClientOptions options) : ICopilotSdkClient
{
    private readonly CopilotClient client = new(options);
    public Task<GetAuthStatusResponse> AuthenticateAsync(CancellationToken token) => client.GetAuthStatusAsync(token);
    public Task<IList<ModelInfo>> ModelsAsync(CancellationToken token) => client.ListModelsAsync(token);
    public async Task<ICopilotSdkSession> CreateAsync(SessionConfig configuration, CancellationToken token) =>
        new Session(await client.CreateSessionAsync(configuration, token).ConfigureAwait(false));

    public Task StopAsync() => client.ForceStopAsync();
    public ValueTask DisposeAsync() => client.DisposeAsync();

    private sealed class Session(CopilotSession session) : ICopilotSdkSession
    {
        public string SessionId => session.SessionId;
        public async Task<string?> SendAsync(string prompt, CancellationToken token) =>
            (await session.SendAndWaitAsync(prompt, cancellationToken: token).ConfigureAwait(false))?.Data.Content;
    }
}
