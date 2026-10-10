using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Network;

namespace Kora.Tools.Network;

public sealed class WebPageGet(
    IWebPageTransport transport,
    ISecurityAuditLog audit)
{
    public async Task<WebPageGetResult> ExecuteAsync(
        WebPageGetRequest request,
        SecurityAuditInitiator initiator,
        Func<Uri, CancellationToken, ValueTask<bool>> authorize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorize);
        _ = WebPageAccessBinding.DestinationDigest(request.Address);
        var current = request.Address;
        var auditEvent = new SecurityAuditEvent(
            Guid.NewGuid(),
            SecurityAuditCategory.ProtectedOperation,
            "network.get-web-page",
            SecurityAuditOutcome.Requested,
            initiator,
            "network.remote");
        audit.Write(auditEvent);
        using var activity = HostActivity.Current is null
            ? null
            : HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(WebPageCapability.TimeoutSeconds));

        try
        {
            for (var redirects = 0; ; redirects++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!await authorize(current, timeout.Token).ConfigureAwait(false))
                {
                    return Complete(
                        new(WebPageGetOutcome.Denied, "address-not-authorized", current, RedirectCount: redirects),
                        auditEvent,
                        activity);
                }

                var addresses = await transport.ResolveAsync(current.IdnHost, timeout.Token).ConfigureAwait(false);
                if (addresses.Count == 0 || addresses.Any(address => !WebPageNetworkPolicy.IsPublic(address)))
                {
                    return Complete(
                        new(WebPageGetOutcome.Denied, "non-public-network-destination", current, RedirectCount: redirects),
                        auditEvent,
                        activity);
                }

                var endpoint = addresses
                    .OrderBy(address => address.AddressFamily)
                    .ThenBy(address => Convert.ToHexString(address.GetAddressBytes()), StringComparer.Ordinal)
                    .First();
                var response = await transport.SendAsync(current, endpoint, timeout.Token).ConfigureAwait(false);
                try
                {
                    if (response.Address != current)
                    {
                        return Complete(
                            new(WebPageGetOutcome.Failed, "transport-address-mismatch", current, RedirectCount: redirects),
                            auditEvent,
                            activity);
                    }
                    if (response.StatusCode is >= 300 and <= 399)
                    {
                        if (response.RedirectAddress is null)
                        {
                            return Complete(
                                new(WebPageGetOutcome.Failed, "redirect-location-missing", current, RedirectCount: redirects),
                                auditEvent,
                                activity);
                        }
                        if (redirects == WebPageCapability.MaximumRedirects)
                        {
                            return Complete(
                                new(WebPageGetOutcome.Failed, "redirect-limit-exceeded", current, RedirectCount: redirects),
                                auditEvent,
                                activity);
                        }
                        current = CanonicalRedirect(current, response.RedirectAddress);
                        continue;
                    }
                    if (response.StatusCode is < 200 or > 299)
                    {
                        return Complete(
                            new(WebPageGetOutcome.Failed, "http-status-" + response.StatusCode, current, RedirectCount: redirects),
                            auditEvent,
                            activity);
                    }
                    if (response.ContentEncodings.Count != 0)
                    {
                        return Complete(
                            new(WebPageGetOutcome.UnsupportedContent, "encoded-content-not-supported", current, RedirectCount: redirects),
                            auditEvent,
                            activity);
                    }
                    var mediaType = response.MediaType?.ToLowerInvariant();
                    if (mediaType is not ("text/html" or "text/plain"))
                    {
                        return Complete(
                            new(WebPageGetOutcome.UnsupportedContent, "unsupported-content-type", current, mediaType, RedirectCount: redirects),
                            auditEvent,
                            activity);
                    }
                    if (response.CharacterSet is not null
                        && !string.Equals(response.CharacterSet, "utf-8", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(response.CharacterSet, "utf8", StringComparison.OrdinalIgnoreCase))
                    {
                        return Complete(
                            new(WebPageGetOutcome.UnsupportedContent, "unsupported-character-set", current, mediaType, RedirectCount: redirects),
                            auditEvent,
                            activity);
                    }
                    if (response.ContentLength > WebPageCapability.MaximumContentBytes)
                    {
                        return Complete(
                            new(WebPageGetOutcome.TooLarge, "content-length-exceeded", current, mediaType, RedirectCount: redirects),
                            auditEvent,
                            activity);
                    }

                    var bytes = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    var source = new UTF8Encoding(false, true).GetString(bytes);
                    var text = string.Equals(mediaType, "text/html", StringComparison.Ordinal)
                        ? ExtractHtmlText(source)
                        : NormalizeText(source);
                    var bounded = BoundOutput(text);
                    return Complete(
                        new(
                            WebPageGetOutcome.Succeeded,
                            "retrieved",
                            current,
                            mediaType,
                            bounded.Text,
                            bounded.Truncated,
                            redirects),
                        auditEvent,
                        activity);
                }
                finally
                {
                    DisposeResponse(response);
                }
            }
        }
        catch (DecoderFallbackException)
        {
            return Complete(
                new(WebPageGetOutcome.UnsupportedContent, "invalid-utf8", current),
                auditEvent,
                activity);
        }
        catch (InvalidDataException exception)
            when (string.Equals(
                exception.Message,
                "The web-page response exceeded its content limit.",
                StringComparison.Ordinal))
        {
            return Complete(
                new(WebPageGetOutcome.TooLarge, "content-limit-exceeded", current),
                auditEvent,
                activity);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Complete(
                new(WebPageGetOutcome.Failed, "deadline-exceeded", current),
                auditEvent,
                activity);
        }
        catch (OperationCanceledException)
        {
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled, "cancelled"));
            activity?.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            audit.Write(auditEvent.WithOutcome(SecurityAuditOutcome.Failed, "unhandled-failure"));
            activity?.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private WebPageGetResult Complete(
        WebPageGetResult result,
        SecurityAuditEvent auditEvent,
        HostActivity? activity)
    {
        audit.Write(auditEvent.WithOutcome(
            result.Outcome == WebPageGetOutcome.Succeeded
                ? SecurityAuditOutcome.Succeeded
                : result.Outcome == WebPageGetOutcome.Denied
                    ? SecurityAuditOutcome.Denied
                    : SecurityAuditOutcome.Failed,
            result.Reason));
        activity?.Complete(result.Outcome == WebPageGetOutcome.Succeeded
            ? HostOperationOutcome.Completed
            : HostOperationOutcome.Failed);
        return result;
    }

    private static Uri CanonicalRedirect(Uri current, Uri redirect)
    {
        var resolved = redirect.IsAbsoluteUri ? redirect : new Uri(current, redirect);
        _ = WebPageAccessBinding.DestinationDigest(resolved);
        return resolved;
    }

    [SuppressMessage(
        "Usage",
        "VSTHRD103:Call async methods when in an async method",
        Justification = "WebPageResponse.Dispose only releases owned stream and HTTP response resources; the async compiler continuation is not measurable by coverage.")]
    private static void DisposeResponse(WebPageResponse response) => response.Dispose();

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }
            if (output.Length + read > WebPageCapability.MaximumContentBytes)
            {
                throw new InvalidDataException("The web-page response exceeded its content limit.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static (string Text, bool Truncated) BoundOutput(string text)
    {
        var maximum = WebPageCapability.MaximumTextUtf8Bytes;
        if (Encoding.UTF8.GetByteCount(text) <= maximum)
        {
            return (text, false);
        }
        var length = Math.Min(text.Length, maximum);
        while (Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > maximum)
        {
            length--;
        }
        return (text[..length], true);
    }

    private static string ExtractHtmlText(string html)
    {
        var output = new StringBuilder(html.Length);
        var tag = new StringBuilder();
        var inTag = false;
        var suppressed = 0;
        for (var index = 0; index < html.Length; index++)
        {
            var character = html[index];
            if (!inTag && character == '<')
            {
                inTag = true;
                tag.Clear();
                continue;
            }
            if (inTag)
            {
                if (character != '>')
                {
                    if (tag.Length < 32)
                    {
                        tag.Append(character);
                    }
                    continue;
                }
                inTag = false;
                var name = tag.ToString().TrimStart();
                var closing = name.StartsWith('/');
                name = name.TrimStart('/').Split([' ', '\t', '\r', '\n'], 2)[0].ToLowerInvariant();
                if (string.Equals(name, "script", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
                {
                    suppressed += closing ? -1 : 1;
                    suppressed = Math.Max(0, suppressed);
                }
                if (suppressed == 0 && name is "p" or "br" or "div" or "li" or "h1" or "h2" or "h3")
                {
                    output.Append('\n');
                }
                continue;
            }
            if (suppressed == 0)
            {
                output.Append(character);
            }
        }
        return NormalizeText(WebUtility.HtmlDecode(output.ToString()));
    }

    private static string NormalizeText(string text)
    {
        var output = new StringBuilder(text.Length);
        var whitespace = false;
        var newline = false;
        foreach (var character in text.Normalize(NormalizationForm.FormC))
        {
            if (character is '\r' or '\n')
            {
                newline = output.Length > 0;
                whitespace = false;
            }
            else if (char.IsWhiteSpace(character))
            {
                whitespace = output.Length > 0;
            }
            else
            {
                if (newline)
                {
                    output.Append('\n');
                }
                else if (whitespace)
                {
                    output.Append(' ');
                }
                output.Append(character);
                newline = false;
                whitespace = false;
            }
        }
        return output.ToString().Trim();
    }
}
