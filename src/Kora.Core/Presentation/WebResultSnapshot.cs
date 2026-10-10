using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Hosting;
using Kora.Core.Network;

namespace Kora.Core.Presentation;

// Only the host success-presentation route captures this type. It has no wire
// constructor; provenance is an external observation, never durable authority.
public sealed class WebResultSnapshot
{
    private WebResultSnapshot(DetailContentReference reference, WebResultProvenance provenance, string text)
    {
        Reference = reference;
        Provenance = provenance;
        Text = text;
        Source = "VOLATILE untrusted external observation; not a live page or retained session record.\n"
            + "Provenance (inert JSON; addresses are observations, not authority):\n"
            + JsonSerializer.Serialize(provenance)
            + "\n\nExact returned normalized text (may honestly be empty):\n" + text;
    }

    public DetailContentReference Reference { get; }
    public WebResultProvenance Provenance { get; }
    public string Text { get; }
    public string Source { get; }

    public static AdmittedDetailContent Capture(
        DetailContentReference reference, HostRequest originalRequest, long controlGeneration,
        Uri requestedAddress, WebPageGetResult result, DateTimeOffset retrievedAt)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        ArgumentNullException.ThrowIfNull(result);
        reference.Validate();
        if (controlGeneration <= 0 || retrievedAt == default)
        {
            throw new InvalidDataException("Web-result details require the captured host generation and retrieval time.");
        }
        if (result.Outcome != WebPageGetOutcome.Succeeded || result.Text is null || result.FinalAddress is null
            || result.MediaType is not ("text/plain" or "text/html")
            || result.RedirectCount < 0 || result.RedirectCount > WebPageCapability.MaximumRedirects)
        {
            throw new InvalidDataException("Only a complete admitted web retrieval success can become native details.");
        }
        _ = WebPageAccessBinding.DestinationDigest(requestedAddress);
        _ = WebPageAccessBinding.DestinationDigest(result.FinalAddress);
        var encoding = new UTF8Encoding(false, true);
        var bytes = encoding.GetBytes(result.Text);
        if (bytes.Length > WebPageCapability.MaximumTextUtf8Bytes)
        {
            throw new InvalidDataException("The returned normalized text exceeds its retrieval bound; nothing was truncated for details.");
        }
        var snapshot = new WebResultSnapshot(reference, new(
            originalRequest, controlGeneration, requestedAddress.AbsoluteUri, result.FinalAddress.AbsoluteUri,
            result.MediaType, result.RedirectCount, result.Truncated, retrievedAt,
            Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length,
            WebPageCapability.MaximumContentBytes, WebPageCapability.MaximumTextUtf8Bytes,
            WebPageCapability.MaximumOutputUtf8Bytes, NativeDetailProfile.MaximumUtf8Bytes), result.Text);
        return new(reference, DetailContentKind.PlainText, DetailContentOrigin.RetrievedWebResult,
            DetailSensitivity.DisclosureConfirmationRequired, "Exact retrieved web-result details",
            "Untrusted external observation; volatile host snapshot, not live content or durable session history.",
            snapshot.Source, webResult: snapshot);
    }
}
