using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal interface ICopilotSdkSession
{
    string SessionId { get; }
    Task<string?> SendAsync(string prompt, CancellationToken token);
    ModelTokenUsage? GetTokenUsage(int modelCalls) => null;
}
