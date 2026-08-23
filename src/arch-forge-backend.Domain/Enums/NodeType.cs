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
    ExternalService,

    // QA §3 (docs/QA-analisis.md). Four types, appended - the enum travels as a string but its
    // declaration order is the canonical order of the graph (V6 D1.1), so the middle never moves.

    /// <summary>
    /// QA §3.1. The outside, drawn: the users, the browser, whoever calls the front door. It is
    /// what turns "public" from something inferred (a root of the graph) into something declared -
    /// what hangs from a Client is exposed, what does not is not. <c>EntryPoints</c> reads it as
    /// the traffic origin, SPOF refuses to grade it (you cannot replicate the internet), and its
    /// outgoing edges are the requests themselves, not calls anybody configures.
    /// </summary>
    Client,

    /// <summary>
    /// QA §3.2. The edge cache. What earns it its place is the load model: it absorbs its hit
    /// ratio of everything passing through, which is the cheapest scalability lesson there is -
    /// and the frontier walks straight through it, because a CDN cannot ask who is calling.
    /// </summary>
    Cdn,

    /// <summary>
    /// QA §3.3. The clock that triggers background work. It gives a Worker a drawn origin, and it
    /// is the declared way of saying "this flow is not user traffic": <c>EntryPoints</c> never
    /// treats it as where the outside comes in, which is what stopped a polling Worker from
    /// "exposing" its own database (QA B6).
    /// </summary>
    Scheduler,

    /// <summary>
    /// QA §3.4. Where authentication actually lives. A door wired to one has answered the question
    /// <c>UNAUTHENTICATED_ENTRY</c> asks - delegation is a declaration - and on the synchronous
    /// path of every request it is the most famous SPOF in the industry, which pattern A grades
    /// with no special case at all.
    /// </summary>
    IdentityProvider
}
