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
        services.AddScoped<IArchitectureRule, SpofRule>();
        services.AddScoped<IArchitectureRule, SyncDependencyChainRule>();
        services.AddScoped<ArchitectureAnalyzer>();

        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ApiConfiguration>(NodeType.Api));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<DatabaseConfiguration>(NodeType.Database));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<ServiceConfiguration>(NodeType.Service));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<QueueConfiguration>(NodeType.Queue));
        services.AddSingleton<INodeConfigurationProvider>(new NodeConfigurationProvider<CacheConfiguration>(NodeType.Cache));
        services.AddSingleton<NodeConfigurationRegistry>();

        services.AddScoped<IValidator<CreateArchitectureCommand>, CreateArchitectureCommandValidator>();
        services.AddScoped<IValidator<UpdateArchitectureCommand>, UpdateArchitectureCommandValidator>();

        services.AddScoped<CreateArchitectureCommandHandler>();
        services.AddScoped<UpdateArchitectureCommandHandler>();
        services.AddScoped<GetArchitectureByIdQueryHandler>();
        services.AddScoped<AnalyzeArchitectureQueryHandler>();

        return services;
    }
}
