using GitHub.Copilot;

namespace Kora.Windows.Dependencies;

#pragma warning disable GHCP001 // Account setup is never allowed to perform inference or open an inference socket.
internal sealed class CopilotSetupRequestHandler(HttpClient http) : CopilotRequestHandler(http)
{
    protected override Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CopilotRequestContext ctx) =>
        Task.FromException<HttpResponseMessage>(new NotSupportedException("Inference is not admitted during account setup."));
    protected override Task<CopilotWebSocketHandler> OpenWebSocketAsync(CopilotRequestContext ctx) =>
        Task.FromException<CopilotWebSocketHandler>(new NotSupportedException("Sockets are not admitted during account setup."));
}
#pragma warning restore GHCP001
