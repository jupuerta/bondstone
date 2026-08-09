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
        services.AddDbContext<ArchForgeDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("ArchForge")));

        services.AddScoped<IArchitectureRepository, ArchitectureRepository>();

        return services;
    }
}
