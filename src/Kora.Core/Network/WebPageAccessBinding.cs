using System.Security.Cryptography;
using System.Text;
using Kora.Core.Authorization;

namespace Kora.Core.Network;

public static class WebPageAccessBinding
{
    public const string ActionId = "network.get-web-page";

    public static string DestinationDigest(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri
            || address.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(address.UserInfo)
            || !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException(
                "Web-page access requires an absolute HTTP or HTTPS address without credentials or a fragment.",
                nameof(address));
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri)))
            .ToLowerInvariant();
    }

    public static bool Matches(HostOperationProposal proposal, Uri address)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return proposal.Effect == HostOperationEffect.BoundedRead
            && string.Equals(proposal.Binding.ActionId, ActionId, StringComparison.Ordinal)
            && string.Equals(
                proposal.Binding.DestinationDigest,
                DestinationDigest(address),
                StringComparison.Ordinal);
    }
}
