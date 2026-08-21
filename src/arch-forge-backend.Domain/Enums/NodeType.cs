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
    ObjectStorage,

    /// <summary>
    /// V8 §6.2. Somebody else's service: the payment provider, the mail relay, the maps API.
    ///
    /// It is a type of its own and not a <see cref="Service"/> with a different name because it has
    /// two properties no other component has - <strong>you cannot scale it</strong> (instances mean
    /// nothing on it) and <strong>you cannot fix it</strong> - and those two are precisely what make
    /// a fallback the only answer. Drawn as an ordinary Service, "put it behind a queue" and "give
    /// it more instances" look like the same move to the evaluator, when in real life one of the two
    /// is not available to you.
    ///
    /// It earns its place under V2 D3.2 the way every type has to: <c>EXTERNAL_NO_FALLBACK</c> reads
    /// it, and <c>UNDERSIZED_COMPONENT</c> deliberately does not.
    /// </summary>
    ExternalService
}
