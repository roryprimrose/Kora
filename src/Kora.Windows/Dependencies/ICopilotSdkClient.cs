using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;

namespace Kora.Windows.Dependencies;

internal interface ICopilotSdkClient : IAsyncDisposable
{
    Task<GetAuthStatusResponse> AuthenticateAsync(CancellationToken token);
    Task<IList<Model>> ModelsAsync(CancellationToken token);
    Task<ICopilotSdkSession> CreateAsync(CreateSessionOptions configuration, CancellationToken token);
    Task StopAsync();
}
