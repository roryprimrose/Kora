using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal interface ICopilotSdkSession
{
    string SessionId { get; }
    Task<string?> SendAsync(string prompt, TimeSpan timeout, CancellationToken token);
    Task<string?> SendWithAnswerAsync(string prompt, TimeSpan timeout,
        Func<string, CancellationToken, ValueTask> publish, CancellationToken token);
    ModelTokenUsage? GetTokenUsage(int modelCalls) => null;
}
