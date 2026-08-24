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

        // V4 D5.2: the debt, saldada. The analyzer goes from 7 rules to 15, and Security and
        // Observability stop being two areas that travel in the contract with a dash on them.
        services.AddScoped<IArchitectureRule, OrphanNodeRule>();            // Consumer
        services.AddScoped<IArchitectureRule, QueueNoDlqRule>();            // Consumer
        services.AddScoped<IArchitectureRule, TimeoutTooShortRule>();       // Consumer
        services.AddScoped<IArchitectureRule, UndersizedComponentRule>();   // Consumer
        services.AddScoped<IArchitectureRule, NoRateLimitRule>();           // Consumer
        services.AddScoped<IArchitectureRule, ExposedDatabaseRule>();       // Consumer
        services.AddScoped<IArchitectureRule, UnauthenticatedEntryRule>();  // Consumer
        services.AddScoped<IArchitectureRule, NoObservabilityRule>();       // Consumer

        // V8 §6.2: the sixteenth, and the one that turns ExternalService into a component. A
        // Producer since QA B8: it tells MISSING_RESILIENCE which calls it has already covered.
        services.AddScoped<IArchitectureRule, ExternalNoFallbackRule>();    // Producer

        // V13 D3.1: the seventeenth, and the one that turns ProtocolType into a field somebody
        // reads. It checks the drawing rather than the architecture, which is why it is worth
        // reading before the other sixteen.
        services.AddScoped<IArchitectureRule, ProtocolMismatchRule>();      // Consumer

        // QA §4: three lessons the model could already teach. Twenty rules.
        services.AddScoped<IArchitectureRule, QueueNoConsumerRule>();       // Consumer
        services.AddScoped<IArchitectureRule, CacheAsStoreRule>();          // Consumer
        services.AddScoped<IArchitectureRule, SharedDatabaseRule>();        // Consumer

        // V14 §4.3: the two that balance the areas B13 found lopsided. TRAFFIC_REJECTED gives
        // Availability its second rule; MISSING_CDN gives the Cdn type the rule that justifies it.
        services.AddScoped<IArchitectureRule, TrafficRejectedRule>();       // Consumer
        services.AddScoped<IArchitectureRule, MissingCdnRule>();            // Consumer

        // V14 §4.1: with them, twenty-three - and the second one and the second one about the drawing rather than the
        // architecture. PROTOCOL_MISMATCH asks whether the protocol fits the far end; this one asks
        // whether the two ends can talk at all.
        services.AddScoped<IArchitectureRule, ImplausibleDependencyRule>(); // Consumer
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
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ObservabilityConfiguration>(NodeType.Observability));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<WorkerConfiguration>(NodeType.Worker));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ObjectStorageConfiguration>(NodeType.ObjectStorage));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ExternalServiceConfiguration>(NodeType.ExternalService));

        // QA §3: the four types of the QA round, each schema arriving with the mechanism that
        // reads it - Client and Scheduler feed the load model, Cdn absorbs, IdentityProvider is
        // read by UNAUTHENTICATED_ENTRY through the wiring rather than through a field.
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
        services.AddScoped<ForkArchitectureCommandHandler>();
        services.AddScoped<StartChallengeCommandHandler>();
        services.AddScoped<OpenChallengeSolutionCommandHandler>();
        services.AddScoped<EvaluateChallengeQueryHandler>();
        services.AddScoped<GetArchitectureHistoryQueryHandler>();

        return services;
    }
}
