using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.Queries;
using arch_forge_backend.Application.Challenges;
using arch_forge_backend.Application.Configuration;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Application.Simulation;
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

        // V3 D5.1: next to the analyzer and not inside it. Not a list of registered pieces either -
        // mechanisms compose over the same edge in a fixed order, with the timeout as the gate to
        // all the others, and a pipeline would have to encode that order in its registration order.
        services.AddScoped<SimulationEngine>();

        // V4 D1.5: loaded and validated once, at startup. A malformed definition stops the
        // application from starting, which is where a content mistake belongs - not in front of
        // somebody trying to pass a challenge that cannot be passed.
        services.AddSingleton<ChallengeCatalog>();

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
        services.AddScoped<DeleteArchitectureCommandHandler>();
        services.AddScoped<GetArchitectureByShareTokenQueryHandler>();
        services.AddScoped<AnalyzeArchitectureQueryHandler>();
        services.AddScoped<SimulateArchitectureQueryHandler>();
        services.AddScoped<ForkArchitectureCommandHandler>();
        services.AddScoped<StartChallengeCommandHandler>();
        services.AddScoped<EvaluateChallengeQueryHandler>();

        return services;
    }
}
