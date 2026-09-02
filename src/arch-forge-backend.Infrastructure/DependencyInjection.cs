using arch_forge_backend.Application.Common.Interfaces;
using arch_forge_backend.Infrastructure.Persistence;
using arch_forge_backend.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace arch_forge_backend.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Refusing to start beats starting against whatever a default happened to name. The old
        // default was a working localhost credential, so a deployment that forgot to configure the
        // database still came up — pointed somewhere nobody chose, with a password in the repo.
        //
        // Read inside the lambda, not here: configuration sources are still being added while this
        // method runs (WebApplicationFactory adds the test container's string after Program has
        // registered services), and an eager read would see a half-built configuration. Startup
        // still fails, one statement later — Program resolves the context to migrate.
        services.AddDbContext<ArchForgeDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("ArchForge");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "No ArchForge connection string. Set ConnectionStrings__ArchForge in the environment " +
                    "(docker compose reads POSTGRES_* from .env; see .env.example).");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IArchitectureRepository, ArchitectureRepository>();

        return services;
    }
}
