namespace bondstone_backend.Api.RateLimiting;

// Bound to the `RateLimiting` section, with the production numbers as defaults so an empty
// configuration is the right configuration. Two reasons it is not a pair of constants:
//
//  1. The integration suite runs 18 requests through the mint limiter against a ceiling of 20. That
//     is two of headroom, and the nineteenth minting test somebody adds would fail the whole suite
//     with a 429 that looks like nothing at all. `PostgresApiFactory` lifts the ceiling instead.
//  2. Whoever deploys this gets to move the numbers without a rebuild — the right ones depend on
//     where it sits and who is in front of it, and that is not knowable from here.
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>The backstop, per client per minute. Sized so no honest editing session reaches it.</summary>
    public int GlobalPermitsPerMinute { get; set; } = 600;

    /// <summary>How many architectures one client may create per <see cref="MintWindowMinutes"/>.</summary>
    public int MintPermits { get; set; } = 20;

    public int MintWindowMinutes { get; set; } = 10;

    /// <summary>The health probe's own ceiling, per client per minute. High enough that no honest
    /// probe meets it, low enough that the endpoint is not a free hammer at the database.</summary>
    public int HealthPermitsPerMinute { get; set; } = 60;
}
