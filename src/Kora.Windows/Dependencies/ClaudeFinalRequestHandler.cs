using System.Net;
using System.Net.Http.Headers;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

/// <summary>One immutable, reviewed physical POST. No redirects, retries or caller-selected headers.</summary>
internal sealed class ClaudeFinalRequestHandler(HttpClient http, ClaudeAccountBroker accounts)
{
    internal async Task<byte[]?> SendAsync(ClaudeTransmission transmission, ClaudeAccount account,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (http.DefaultRequestHeaders.Any())
        { throw new InvalidDataException("Ambient HTTP headers cannot enter a Claude transmission."); }
        if (!ClaudeTransmission.AllowedDestination(transmission.Destination)
            || !await accounts.CurrentAsync(account, token).ConfigureAwait(false)
            || !await current(token).ConfigureAwait(false))
        { return null; }
        using var request = new HttpRequestMessage(HttpMethod.Post, transmission.Destination)
        {
            Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(transmission.Body.ToArray()),
        };
        ClaudeAccountBroker.AddHeaders(request, account.Credential, account.Approval, account.Lease);
        request.Headers.Host = transmission.Destination.Authority;
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Content.Headers.ContentLength = transmission.Body.Length;
        var actual = request.Headers.Where(header => !header.Key.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
            .Concat(request.Content.Headers).ToDictionary(header => header.Key,
                header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);
        if (actual.Count != transmission.Headers.Count || actual.Any(header =>
            !transmission.Headers.TryGetValue(header.Key, out var disclosed) || !string.Equals(disclosed, header.Value, StringComparison.Ordinal)))
        { throw new InvalidDataException("Physical Claude headers do not match the complete disclosure."); }
        if (!await admit(transmission, token).ConfigureAwait(false)
            || !await current(token).ConfigureAwait(false) || !await accounts.CurrentAsync(account, token).ConfigureAwait(false))
        { return null; }
        token.ThrowIfCancellationRequested();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { throw new HttpRequestException("Claude POST did not return a successful response."); }
        return await ClaudeAccountBroker.ReadBoundedAsync(response.Content, 262144, token).ConfigureAwait(false);
    }

    internal static ClaudeTransmission Prepare(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple profile,
        ClaudeAccount account, string path, byte[] body)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic-version"] = account.Approval.ApiVersion,
            ["Host"] = "api.anthropic.com",
            ["anthropic-workspace-id"] = account.Lease.Workspace,
            ["Accept"] = "application/json",
            ["Connection"] = "close",
            ["Content-Type"] = "application/json; charset=utf-8",
            ["Content-Length"] = body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return new(provenance, profile, new Uri("https://api.anthropic.com" + path), body, headers);
    }
}
