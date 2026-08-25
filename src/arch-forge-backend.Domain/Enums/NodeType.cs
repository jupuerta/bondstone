namespace arch_forge_backend.Domain.Enums;

public enum NodeType
{
    Api,
    Database,
    Service,
    Queue,
    Cache,
    Gateway,
    LoadBalancer,

    Observability,

    Worker,

    ObjectStorage,

    ExternalService,


    Client,

    Cdn,

    Scheduler,

    IdentityProvider,

    Topic
}
