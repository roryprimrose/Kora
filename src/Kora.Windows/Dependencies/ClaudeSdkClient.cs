using Anthropic;

namespace Kora.Windows.Dependencies;

internal static class ClaudeSdkClient
{
    internal static AnthropicClient Create(HttpClient http, string credential, bool validateResponse = true) => new(new()
    {
        HttpClient = http, ApiKey = credential,
        BaseUrl = "https://api.anthropic.com", MaxRetries = 0,
        Timeout = TimeSpan.FromMinutes(2), OpenTelemetry = false,
        ResponseValidation = validateResponse,
    });
}
