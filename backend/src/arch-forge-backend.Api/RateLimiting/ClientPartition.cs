using System.Net;
using System.Net.Sockets;

namespace bondstone_backend.Api.RateLimiting;

/// <summary>Which bucket a caller falls in. Its own type so it can be tested without a request.</summary>
public static class ClientPartition
{
    /// <summary>
    /// **IPv6 is grouped by /64, not by address.** Every limit here assumes an address is something
    /// a caller has a few of. That holds for IPv4, where one more costs money. It is false for
    /// IPv6: the smallest block anybody is handed is a /64 — 18 446 744 073 709 551 616 addresses —
    /// so a per-address limit is not a limit, it is an invitation to walk the block. Grouping by
    /// /64 puts one subscriber back in one bucket, which is what the number always meant.
    ///
    /// This matters before it bites: the domain has no AAAA record yet, so nothing reaches the API
    /// over IPv6 and the hole is shut by accident. The day that record goes in, the accident stops
    /// protecting anything.
    ///
    /// With no address there is nothing to partition by, so everything anonymous shares one bucket —
    /// the safe direction to be wrong in.
    /// </summary>
    public static string Of(IPAddress? address)
    {
        if (address is null)
            return "unknown";

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        // An IPv4 address arriving mapped into IPv6 (::ffff:203.0.113.1) is an IPv4 caller and has
        // to keep counting as one: masking it to /64 would drop every IPv4 client on the planet
        // into a single shared bucket.
        if (address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().ToString();

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();

        return new IPAddress(bytes).ToString() + "/64";
    }
}
