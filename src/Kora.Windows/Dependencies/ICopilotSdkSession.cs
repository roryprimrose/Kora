namespace Kora.Windows.Dependencies;

internal interface ICopilotSdkSession
{
    string SessionId { get; }
    Task<string?> SendAsync(string prompt, TimeSpan timeout, CancellationToken token);
}
