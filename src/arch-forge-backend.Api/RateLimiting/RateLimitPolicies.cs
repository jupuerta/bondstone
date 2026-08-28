using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace arch_forge_backend.Api.RateLimiting;

// Two limits, because there are two different things to protect against and one number cannot do
// both jobs.
//
// **The global one is a backstop, not a policy.** This editor is chatty by design: the autosave
// debounces at 1 000 ms and the live analysis at 800, and a session in Simulate also asks for the
// simulation, the fragility map and the load curve. A frantic minute is a couple of hundred
// requests from one honest person, so the ceiling is set where no human reaches it and a script
// does.
//
// **The mint one is the policy.** Four responses create an architecture — V9 D1.1 named three
// (create, start a challenge, fork) and `POST /challenges/{slug}/solution` joined them later. The
// product has no accounts and the database is shared by everyone, so those four are the only
// requests whose cost is a row nobody deletes. `/solution` is the worst of them: it returns a share
// token and no edit token, so what it leaves cannot be deleted by the person who created it.
//
// V20's browser pass left 21 stray rows in one afternoon just by visiting `/c/{slug}` at seven
// widths. That is one person being careless; a script would not stop at 21.
public static class RateLimitPolicies
{
    public const string Mint = "mint";

    public static void AddArchForgeRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientOf(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.GlobalPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(Mint, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientOf(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.MintPermits,
                        Window = TimeSpan.FromMinutes(limits.MintWindowMinutes),
                        QueueLimit = 0,
                    }));

            // The frontend reads `message` off every error body it does not have a typed shape for,
            // so a refusal says something a person can read rather than "status 429".
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Too many requests. Wait a moment and try again." },
                    cancellationToken);
            };
        });
    }

    // Behind the proxy this is the real client, because `UseForwardedHeaders` runs first and
    // `X-Forwarded-For` has already been applied by the time any limiter is consulted. Without a
    // remote address there is nothing to partition by, and everything anonymous shares one bucket —
    // which is the safe direction to be wrong in.
    private static string ClientOf(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
