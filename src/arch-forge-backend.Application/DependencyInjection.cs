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
        services.AddScoped<IArchitectureRule, SpofRule>();
        services.AddScoped<IArchitectureRule, CycleRule>();
        services.AddScoped<IArchitectureRule, RetryAmplificationRule>();
        services.AddScoped<IArchitectureRule, SyncDependencyChainRule>();
        services.AddScoped<IArchitectureRule, MissingResilienceRule>();
        services.AddScoped<IArchitectureRule, HighCouplingRule>();
        services.AddScoped<IArchitectureRule, MissingCacheRule>();

        services.AddScoped<IArchitectureRule, OrphanNodeRule>();
        services.AddScoped<IArchitectureRule, QueueNoDlqRule>();
        services.AddScoped<IArchitectureRule, TimeoutTooShortRule>();
        services.AddScoped<IArchitectureRule, UndersizedComponentRule>();
        services.AddScoped<IArchitectureRule, NoRateLimitRule>();
        services.AddScoped<IArchitectureRule, ExposedDatabaseRule>();
        services.AddScoped<IArchitectureRule, UnauthenticatedEntryRule>();
        services.AddScoped<IArchitectureRule, NoObservabilityRule>();

        services.AddScoped<IArchitectureRule, ExternalNoFallbackRule>();

        services.AddScoped<IArchitectureRule, ProtocolMismatchRule>();

        services.AddScoped<IArchitectureRule, QueueNoConsumerRule>();
        services.AddScoped<IArchitectureRule, CacheAsStoreRule>();
        services.AddScoped<IArchitectureRule, SharedDatabaseRule>();

        services.AddScoped<IArchitectureRule, TrafficRejectedRule>();
        services.AddScoped<IArchitectureRule, MissingCdnRule>();

        services.AddScoped<IArchitectureRule, ImplausibleDependencyRule>();

        services.AddScoped<IArchitectureRule, TimeoutTooLongRule>();

        services.AddScoped<IArchitectureRule, OverprovisionedRule>();

        services.AddScoped<IArchitectureRule, AsyncNoQueueRule>();
        services.AddScoped<ArchitectureAnalyzer>();

        services.AddScoped<SimulationEngine>();

        services.AddSingleton<ChallengeCatalog>();


        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ApiConfiguration>(NodeType.Api));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<DatabaseConfiguration>(NodeType.Database));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ServiceConfiguration>(NodeType.Service));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<QueueConfiguration>(NodeType.Queue));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<CacheConfiguration>(NodeType.Cache));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<GatewayConfiguration>(NodeType.Gateway));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<LoadBalancerConfiguration>(NodeType.LoadBalancer));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ObservabilityConfiguration>(NodeType.Observability));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<WorkerConfiguration>(NodeType.Worker));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ObjectStorageConfiguration>(NodeType.ObjectStorage));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ExternalServiceConfiguration>(NodeType.ExternalService));

        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ClientConfiguration>(NodeType.Client));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<CdnConfiguration>(NodeType.Cdn));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<SchedulerConfiguration>(NodeType.Scheduler));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<IdentityProviderConfiguration>(NodeType.IdentityProvider));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<TopicConfiguration>(NodeType.Topic));
        services.AddSingleton<NodeConfigurationRegistry>();

        services.AddScoped<IValidator<CreateArchitectureCommand>, CreateArchitectureCommandValidator>();
        services.AddScoped<IValidator<UpdateArchitectureCommand>, UpdateArchitectureCommandValidator>();

        services.AddScoped<CreateArchitectureCommandHandler>();
        services.AddScoped<UpdateArchitectureCommandHandler>();
        services.AddScoped<DeleteArchitectureCommandHandler>();
        services.AddScoped<GetArchitectureByShareTokenQueryHandler>();
        services.AddScoped<GetArchitectureByEditTokenQueryHandler>();
        services.AddScoped<AnalyzeArchitectureQueryHandler>();
        services.AddScoped<SimulateArchitectureQueryHandler>();
        services.AddScoped<MapArchitectureFragilityQueryHandler>();
        services.AddScoped<SweepArchitectureLoadQueryHandler>();
        services.AddScoped<ForkArchitectureCommandHandler>();
        services.AddScoped<StartChallengeCommandHandler>();
        services.AddScoped<OpenChallengeSolutionCommandHandler>();
        services.AddScoped<PreviewChallengeSolutionQueryHandler>();
        services.AddScoped<EvaluateChallengeQueryHandler>();
        services.AddScoped<GetArchitectureHistoryQueryHandler>();

        return services;
    }
}
