using System.Net;

namespace Kora.Core.Network;

public static class WebPageNetworkPolicy
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is not (0 or 10 or 127 or >= 224)
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }
        if (IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal
            || address.Equals(IPAddress.IPv6None)
            || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }
        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xe0) == 0x20
            && !(ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8)
            && !(ipv6[0] == 0x20 && ipv6[1] == 0x02);
    }
}
