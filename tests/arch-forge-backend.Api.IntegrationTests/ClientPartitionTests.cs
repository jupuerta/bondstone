using System.Net;
using bondstone_backend.Api.RateLimiting;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

// Every rate limit in this API counts per bucket, and this decides what a bucket is. Get it wrong
// for IPv6 and the numbers stop meaning anything without a single test going red anywhere else.
public class ClientPartitionTests
{
    [Fact]
    public void An_IPv4_address_is_its_own_bucket()
    {
        Assert.Equal("203.0.113.7", ClientPartition.Of(IPAddress.Parse("203.0.113.7")));
        Assert.NotEqual(
            ClientPartition.Of(IPAddress.Parse("203.0.113.7")),
            ClientPartition.Of(IPAddress.Parse("203.0.113.8")));
    }

    [Fact]
    public void Addresses_in_one_IPv6_block_share_a_bucket()
    {
        // The whole point. A /64 is what a single subscriber is handed, and it holds more addresses
        // than there are grains of sand on earth — so counting them one by one is not a limit.
        var first = ClientPartition.Of(IPAddress.Parse("2001:db8:1234:5678::1"));
        var far = ClientPartition.Of(IPAddress.Parse("2001:db8:1234:5678:ffff:ffff:ffff:ffff"));

        Assert.Equal(first, far);
        Assert.EndsWith("/64", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_IPv6_blocks_get_different_buckets()
    {
        Assert.NotEqual(
            ClientPartition.Of(IPAddress.Parse("2001:db8:1234:5678::1")),
            ClientPartition.Of(IPAddress.Parse("2001:db8:1234:9999::1")));
    }

    [Fact]
    public void An_IPv4_address_wearing_an_IPv6_costume_still_counts_as_itself()
    {
        // ::ffff:203.0.113.7 is how an IPv4 caller arrives on a dual-stack socket. Masking it to /64
        // would put every IPv4 client on earth into one bucket — the opposite mistake, and a worse
        // one, because it would deny service to honest people rather than let an attacker past.
        Assert.Equal(
            ClientPartition.Of(IPAddress.Parse("203.0.113.7")),
            ClientPartition.Of(IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Fact]
    public void No_address_means_one_shared_bucket()
    {
        // The safe direction to be wrong in: unattributable traffic is rationed together rather than
        // each request inventing itself a fresh allowance.
        Assert.Equal("unknown", ClientPartition.Of(null));
    }
}
