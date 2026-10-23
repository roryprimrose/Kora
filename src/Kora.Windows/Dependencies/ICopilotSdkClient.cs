using GitHub.Copilot;

namespace Kora.Windows.Dependencies;

internal interface ICopilotSdkClient : IAsyncDisposable
{
    Task<GetAuthStatusResponse> AuthenticateAsync(CancellationToken token);
    Task<IList<ModelInfo>> ModelsAsync(CancellationToken token);
    Task<ICopilotSdkSession> CreateAsync(SessionConfig configuration, CancellationToken token);
    Task StopAsync();
}
