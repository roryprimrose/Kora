using Anthropic;

namespace Kora.Windows.Dependencies;

internal static class ClaudeSdkClient
{
    // A host turn clock replaces the fixed request timeout so blocking final-byte reviews do not consume it.
    internal static AnthropicClient Create(HttpClient http, string credential, bool validateResponse = true,
        Kora.Core.Dependencies.ModelTurnLifetime? turnLifetime = null) => new(new()
    {
        HttpClient = http, ApiKey = credential,
        BaseUrl = "https://api.anthropic.com", MaxRetries = 0,
        Timeout = turnLifetime is null ? TimeSpan.FromMinutes(2) : Timeout.InfiniteTimeSpan, OpenTelemetry = false,
        ResponseValidation = validateResponse,
    });
}
