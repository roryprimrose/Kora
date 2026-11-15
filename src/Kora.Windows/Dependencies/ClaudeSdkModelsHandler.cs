using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Kora.Windows.Dependencies;

/// <summary>One bounded, host-authorized Models inventory; SDK headers and pagination defaults confer no authority.</summary>
internal sealed class ClaudeSdkModelsHandler(HttpClient http, string credential, ClaudeDeploymentApproval approval,
    ClaudeCredentialLease lease, Func<CancellationToken, ValueTask<bool>> current) : HttpMessageHandler
{
    private int pages;
    private string? cursor;
    internal bool HasMore { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (pages >= 64 || pages > 0 && !HasMore || request.Method != HttpMethod.Get || request.Content is not null
            || request.RequestUri is not { IsAbsoluteUri: true } uri
            || uri.GetLeftPart(UriPartial.Path) is not "https://api.anthropic.com/v1/models" || uri.Fragment.Length != 0)
        { throw new InvalidDataException("Only bounded read-only SDK Models pages are admitted."); }
        string[] expected = cursor is null ? ["limit=64", "lifecycle[]=active"]
            : ["limit=64", "lifecycle[]=active", "after_id=" + cursor];
        var actual = uri.Query.TrimStart('?').Split('&').Select(Uri.UnescapeDataString);
        if (!actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || http.DefaultRequestHeaders.Any())
        { throw new InvalidDataException("SDK Models parameters exceed the admitted active-catalogue profile."); }
        token.ThrowIfCancellationRequested();
        if (!await current(token).ConfigureAwait(false))
        { throw new InvalidOperationException("Claude setup approval changed before Models dispatch."); }
        token.ThrowIfCancellationRequested();
        using var physical = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        ClaudeAccountBroker.AddHeaders(physical, credential, approval, lease);
        pages++;
        using var response = await http.SendAsync(physical, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        { throw new HttpRequestException("Claude Models authentication/catalogue was not confirmed."); }
        var bytes = await ClaudeAccountBroker.ReadBoundedAsync(response.Content, 262144, token).ConfigureAwait(false);
        using var json = ClaudeMessagesRuntime.Parse(bytes);
        var root = json.RootElement;
        var data = root.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 64)
        { throw new InvalidDataException("Incomplete Models page."); }
        HasMore = root.GetProperty("has_more").GetBoolean();
        if (HasMore)
        {
            var last = root.GetProperty("last_id").GetString();
            if (data.GetArrayLength() == 0 || last is null || string.Equals(last, cursor, StringComparison.Ordinal)
                || !string.Equals(data[data.GetArrayLength() - 1].GetProperty("id").GetString(), last, StringComparison.Ordinal))
            { throw new InvalidDataException("Incomplete pagination cursor."); }
            cursor = last;
        }
        return new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
    }
}
