using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace bondstone_backend.Api.RateLimiting;

// Three limits, because there are three different things to protect against and one number cannot
// do all three jobs.
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
//
// **The health one is a muzzle.** `/api/health` is public through the nginx and every call asks the
// database whether it is still there. Its callers are machines with known rhythms — a 10s container
// probe, an occasional `check:stack` — so it gets a ceiling sized for them instead of the exemption
// it used to have, which also exempted it from the global backstop.
public static class RateLimitPolicies
{
    public const string Mint = "mint";

    public const string Health = "health";

    public static void AddBondstoneRateLimiting(this IServiceCollection services, IConfiguration configuration)
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

            // Sized for machines, not people: a 10s container probe is 6 a minute and check:stack
            // adds a handful. Anything an order of magnitude above that is not a health check.
            options.AddPolicy(Health, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientOf(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.HealthPermitsPerMinute,
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
                // The endpoint name and not the path: on this API the path is where the tokens
                // live, and a refusal log that collects capability tokens is a worse problem than
                // the one it observes. This line is how abuse becomes visible at all — the
                // limiter otherwise refuses in silence and the graphs just look quiet.
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(RateLimitPolicies).FullName!)
                    .LogWarning("Rate limit hit by {Client} on {Method} {Endpoint}.",
                        ClientOf(context.HttpContext),
                        context.HttpContext.Request.Method,
                        context.HttpContext.GetEndpoint()?.DisplayName ?? "unknown endpoint");

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

    // Behind the declared proxy this is the real client, because `UseForwardedHeaders` runs first
    // and has applied `X-Forwarded-For` by the time any limiter is consulted — and only the proxy
    // named in `ForwardedHeaders:KnownProxies` gets to be believed, so a direct caller cannot mint
    // itself a fresh partition per request. Without a remote address there is nothing to partition
    // by, and everything anonymous shares one bucket — which is the safe direction to be wrong in.
    private static string ClientOf(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
