namespace arch_forge_backend.Domain.Enums;

public enum NodeType
{
    Api,
    Database,
    Service,
    Queue,
    Cache,
    // V2 D3.2: a node type earns its place only if some rule looks at it. LoadBalancer is how
    // redundancy becomes explicit for SPOF; Gateway is what lets a finding name the gateway.
    Gateway,
    LoadBalancer
}
