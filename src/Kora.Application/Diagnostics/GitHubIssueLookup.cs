using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

using Kora.Core.Maintenance;

namespace Kora.Application.Diagnostics;

public sealed class GitHubIssueLookup(HttpClient http) : IGitHubIssueLookup
{
    public const int MaximumResponseBytes = 1_048_576;
    public const int MaximumMatches = 20;

    public async Task<GitHubIssueLookupResult> FindAsync(
        string fingerprint, Func<bool> admitted, CancellationToken cancellationToken)
    {
        if (fingerprint.Length != 64 || fingerprint.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw new InvalidDataException("A canonical exception fingerprint is required.");
        }
        var marker = "Kora error fingerprint: v1:" + fingerprint;
        var query = "repo:" + CanonicalRelease.Repository + " is:issue in:body \"" + marker + "\"";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/search/issues?per_page=20&q=" + Uri.EscapeDataString(query));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Kora", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        RequireAdmission(admitted, cancellationToken);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        RequireAdmission(admitted, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException("GitHub issue search exceeded its response bound.");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            RequireAdmission(admitted, cancellationToken);
            if (bytes.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException("GitHub issue search exceeded its response bound.");
            }
            await bytes.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("incomplete_results", out var incomplete) || incomplete.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("total_count", out var total) || !total.TryGetInt32(out var count)
            || count < 0 || count > MaximumMatches
            || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() != count)
        {
            throw new InvalidDataException("GitHub issue search is incomplete or malformed; no new-issue claim is available.");
        }
        var matches = new List<GitHubIssueMatch>();
        foreach (var item in items.EnumerateArray())
        {
            var number = item.GetProperty("number").GetInt32();
            var title = item.GetProperty("title").GetString();
            var state = item.GetProperty("state").GetString();
            var body = item.GetProperty("body").GetString();
            var expected = CanonicalRelease.RepositoryUrl + "/issues/" + number.ToString(CultureInfo.InvariantCulture);
            if (number <= 0 || string.IsNullOrWhiteSpace(title) || title.Length > 256 || title.Any(char.IsControl)
                || state is not ("open" or "closed") || body is null
                || !body.Split('\n').Any(line => string.Equals(line.TrimEnd('\r'), marker, StringComparison.Ordinal))
                || !string.Equals(item.GetProperty("html_url").GetString(), expected, StringComparison.Ordinal)
                || !string.Equals(item.GetProperty("repository_url").GetString(), CanonicalRelease.ApiRoot, StringComparison.Ordinal)
                || item.TryGetProperty("pull_request", out _) || matches.Any(match => match.Number == number))
            {
                throw new InvalidDataException("GitHub returned an unrelated or malformed issue; duplicate detection is unavailable.");
            }
            matches.Add(new(number, title, string.Equals(state, "closed", StringComparison.Ordinal)));
        }
        RequireAdmission(admitted, cancellationToken);
        return new(matches.AsReadOnly());
    }

    private static void RequireAdmission(Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("GitHub lookup retired by host privacy or network policy."); }
    }
}
