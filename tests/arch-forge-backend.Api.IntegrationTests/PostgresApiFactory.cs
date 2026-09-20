using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

public class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("bondstone")
        .WithUsername("bondstone")
        .WithPassword("bondstone")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Bondstone"] = _postgres.GetConnectionString(),

                // The suite has no client IP, so every test shares one bucket, and it already mints
                // 18 architectures against a production ceiling of 20. Lifted rather than switched
                // off: the limiter stays in the pipeline, so a route that stopped being reachable
                // through it would still fail here.
                ["RateLimiting:MintPermits"] = "100000",
                ["RateLimiting:GlobalPermitsPerMinute"] = "100000",
            });
        });
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync();
}
