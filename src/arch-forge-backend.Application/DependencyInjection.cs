using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.Queries;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace arch_forge_backend.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // The seven rules of D4.6. Registration order is deliberately not the execution order:
        // the analyzer runs every Producer before every Consumer (D5.2), so reordering this list
        // cannot break the escalation of MISSING_RESILIENCE.
        services.AddScoped<IArchitectureRule, SpofRule>();                  // Producer
        services.AddScoped<IArchitectureRule, CycleRule>();                 // Producer
        services.AddScoped<IArchitectureRule, RetryAmplificationRule>();    // Producer
        services.AddScoped<IArchitectureRule, SyncDependencyChainRule>();   // Consumer
        services.AddScoped<IArchitectureRule, MissingResilienceRule>();     // Consumer
        services.AddScoped<IArchitectureRule, HighCouplingRule>();          // Consumer
        services.AddScoped<IArchitectureRule, MissingCacheRule>();          // Consumer
        services.AddScoped<ArchitectureAnalyzer>();

        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ApiConfiguration>(NodeType.Api));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<DatabaseConfiguration>(NodeType.Database));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ServiceConfiguration>(NodeType.Service));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<QueueConfiguration>(NodeType.Queue));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<CacheConfiguration>(NodeType.Cache));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<GatewayConfiguration>(NodeType.Gateway));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<LoadBalancerConfiguration>(NodeType.LoadBalancer));
        services.AddSingleton<NodeConfigurationRegistry>();

        services.AddScoped<IValidator<CreateArchitectureCommand>, CreateArchitectureCommandValidator>();
        services.AddScoped<IValidator<UpdateArchitectureCommand>, UpdateArchitectureCommandValidator>();

        services.AddScoped<CreateArchitectureCommandHandler>();
        services.AddScoped<UpdateArchitectureCommandHandler>();
        services.AddScoped<GetArchitectureByShareTokenQueryHandler>();
        services.AddScoped<AnalyzeArchitectureQueryHandler>();

        return services;
    }
}
