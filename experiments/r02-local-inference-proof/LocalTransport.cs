using System.Net;
using System.Text.Json.Nodes;

namespace R02Proof;

internal sealed class LocalTransport : DelegatingHandler
{
    public List<string> Calls { get; } = [];
    public List<string> GenerationPayloads { get; } = [];

    public LocalTransport(HttpMessageHandler inner) : base(inner) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing destination.");
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port != 11434
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || uri.AbsolutePath is not ("/api/version" or "/api/tags" or "/api/show" or "/api/ps" or "/api/generate"))
        {
            throw new InvalidOperationException($"Blocked non-proof destination: {uri}");
        }

        Calls.Add($"{request.Method} {uri.AbsolutePath}");
        if (request.Method == HttpMethod.Post && uri.AbsolutePath == "/api/generate")
        {
            var originalContent = request.Content!;
            var body = JsonNode.Parse(await originalContent.ReadAsStringAsync(cancellationToken))!.AsObject();
            // CPU-only is an experiment override, not a production pin/configuration change.
            var options = body["options"]?.AsObject() ?? new JsonObject();
            options["num_gpu"] = 0;
            body["options"] = options;
            var payload = body.ToJsonString();
            GenerationPayloads.Add(payload);
            request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            originalContent.Dispose();
        }

        var response = await base.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new HttpRequestException("Redirect refused; inference is unavailable.");
        }
        return response;
    }

    public static LocalTransport Real() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(3),
    });
}

internal sealed class StubHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
