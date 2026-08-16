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
    LoadBalancer,

    /// <summary>
    /// V4 D5.3. Logs, metrics and traces as one component rather than three: what the rules ask is
    /// whether the system can be observed at all, and three boxes would be three ways of answering
    /// the same question. It splits the day a rule tells them apart.
    /// </summary>
    Observability,

    /// <summary>V4 D5.3. Background processing - what a queue delivers to.</summary>
    Worker,

    /// <summary>V4 D5.3. Files that do not belong in a database.</summary>
    ObjectStorage
}
