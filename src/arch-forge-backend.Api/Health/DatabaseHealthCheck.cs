using bondstone_backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace bondstone_backend.Api.Health;

// The only dependency worth asking about. The process answering at all is what the HTTP layer
// proves; what it cannot prove is that the connection behind it is still there — and an API that
// answers 200 on every route while every one of them throws is exactly the failure a healthcheck
// exists to catch.
//
// `CanConnectAsync` and not a query: this runs every few seconds forever, and the question is
// whether the connection is alive, not whether any particular table is.
public sealed class DatabaseHealthCheck(BondstoneDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}
