using System.Net;

namespace ErrandRuns.Api;

public static class ClientIpAddressResolver
{
    public static IPAddress Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ForwardedHeadersMiddleware has already replaced RemoteIpAddress when
        // the immediate sender is a configured trusted proxy.
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
            throw new ArgumentException("A public client IP address could not be determined.");

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (!IsPublic(address))
            throw new ArgumentException("IP location requires a public client IP address.");

        return address;
    }

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] != 10
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && bytes[0] != 127
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] >= 224);
        }

        return !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && (bytes[0] & 0xfe) != 0xfc;
    }
}
