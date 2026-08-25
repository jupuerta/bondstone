using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V14 §4.1. <see cref="ProtocolMismatchRule"/>'s idea applied to the <em>ends</em> of the arrow
/// rather than to its protocol. That rule asks whether <c>Sql</c> into a Queue describes anything;
/// this one asks whether <c>Cdn → Database</c> does - and until V14 nothing did, so a database that
/// called the users, a cron that fired requests at a CDN and a CDN that ran SQL all scored in the
/// high nineties and were labelled solid. The analyzer accepted the vocabulary and only corrected
/// the grammar.
///
/// <strong>Four sentences, not a 15×15 matrix.</strong> Each is general architectural knowledge
/// rather than technology, and together they catch nearly everything in the sweep's table:
///
/// <list type="number">
/// <item>A system of record does not originate calls: a Database or an ObjectStorage is a
/// destination. An arrow leaving one is almost always an arrow drawn backwards.</item>
/// <item>The outside is not a destination: nothing of yours calls the Client. An outgoing
/// webhook is drawn as an ExternalService.</item>
/// <item>A CDN serves content; it talks to an origin - a door, a balancer, a service, a bucket or
/// somebody else's - and to nothing that holds data or messages.</item>
/// <item>A clock triggers work: a Worker, a Service, an Api, a third party, a Queue or a Topic, a
/// Gateway in front of one. It does not call the users, the CDN or a store.</item>
/// </list>
///
/// <strong>What is deliberately allowed.</strong> A Cache calling anything is fine - read-through
/// to its origin is the shape <c>CACHE_AS_STORE</c> <em>asks for</em>, so the store list is the
/// systems of record and not <see cref="NodeRoles.IsDataStore"/>. Instrumentation is not read at
/// all (<see cref="NodeRoles.IsTelemetryEdge"/>, V14 B9): a database pushing its metrics is not a
/// database originating a call. And a third party's own calls are skipped (V14 B10): where Stripe
/// sends its traffic is Stripe's drawing.
///
/// <strong>The severity is how unambiguous the mistake is</strong>, which is PROTOCOL_MISMATCH's
/// doctrine and the only one that makes sense for a rule about drawings. Three of the four ask:
/// triggers and change-data-capture exist, server push to a browser exists, a cron hitting an
/// endpoint through something not on the list exists. The CDN case asserts, because there is no
/// reading in which an edge cache speaks to a database or a broker.
/// </summary>
public class ImplausibleDependencyRule : IArchitectureRule
{
    public string Code => RuleCodes.ImplausibleDependency;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.IMPLAUSIBLE_DEPENDENCY.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.IMPLAUSIBLE_DEPENDENCY.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var edges = graph.Connections
            .Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge) && !NodeRoles.IsCallByThirdParty(graph, edge))
            .ToList();

        if (edges.Count == 0)
            return RuleResult.NotApplicable("rule.na.IMPLAUSIBLE_DEPENDENCY.noCalls");

        var findings = new List<RuleFinding>();

        foreach (var edge in edges.OrderBy(edge => edge.Id))
        {
            if (Implausible(graph, edge) is not { } mismatch)
                continue;

            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            findings.Add(new RuleFinding(
                Code,
                Area,
                mismatch.Severity,
                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.message",
                    ("source", source),
                    ("target", target)),

                // A whole sentence per case and never a shared stem (V7 D1.1): the four say
                // different things about different boxes.
                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.explanation",
                    ("source", source),
                    ("target", target)),
                // V15 A1. The two names, here too. Three of the four suggestions name a box -
                // "let that be what talks to '{target}'", "it is the user who calls '{source}'" -
                // and this call passed no arguments at all, so the catalogue rendered the
                // placeholder and the student read a literal `{target}` in the panel and in the
                // exported document. `LocalizedText` says the key/placeholder pairing is checked
                // at build time; it was not, and the check that now does it is the second half of
                // this fix (check-catalogs.mjs).
                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.suggestion",
                    ("source", source),
                    ("target", target)),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static (string Variant, Severity Severity)? Implausible(ArchitectureGraph graph, Connection edge)
    {
        // An edge hanging off a component that is not there is the graph's problem, not a
        // sentence about what two types can say to each other.
        if (!graph.NodesById.TryGetValue(edge.SourceNodeId, out var source)
            || !graph.NodesById.TryGetValue(edge.TargetNodeId, out var target))
            return null;

        // A self-loop is CYCLE's finding (QA B7), and "a database calling itself" is not the
        // lesson this rule teaches.
        if (source.Id == target.Id)
            return null;

        // 1. A system of record is where calls end. Warning: triggers, change-data-capture and
        //    replication streams are real, so the card asks whether the arrow is backwards
        //    rather than asserting it.
        if (NodeRoles.IsSystemOfRecord(source.Type))
            return ("store", Severity.Warning);

        // 2. Nothing of yours calls the users. Warning: server push exists, and the honest
        //    reading of most of these arrows is "I drew the response".
        if (NodeRoles.IsTrafficOrigin(target.Type))
            return ("client", Severity.Warning);

        // 3. A CDN talks to an origin. Critical: there is no reading in which an edge cache runs
        //    SQL, publishes to a broker or reads another cache - it is the one line of the four
        //    with nothing innocent behind it, and the one the sweep's table opens with.
        if (source.Type == NodeType.Cdn && !IsOrigin(target.Type))
            return ("cdn", Severity.Critical);

        // 4. A clock fires work. Warning: a cron curling an endpoint through something not on
        //    the list is odd rather than impossible, so the card asks.
        if (source.Type == NodeType.Scheduler && !IsWork(target.Type))
            return ("scheduler", Severity.Warning);

        return null;
    }

    /// <summary>
    /// What a CDN can stand in front of: a door, what fronts a door, the bucket it serves, or
    /// somebody else's origin.
    ///
    /// <strong>V15 C3.</strong> <see cref="NodeType.ExternalService"/> was missing, and its absence
    /// made the one <em>Critical</em> of the four fire on an ordinary drawing: an edge in front of a
    /// provider's origin is what half the CDNs in the world are doing. The severity of this variant
    /// is justified as "there is no reading in which…", so a type it cannot argue about has no
    /// business in it.
    /// </summary>
    private static bool IsOrigin(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.LoadBalancer
             or NodeType.Service or NodeType.ObjectStorage or NodeType.ExternalService;

    /// <summary>
    /// What a clock can trigger: something that runs logic, or the broker that hands it to something
    /// that does.
    ///
    /// <strong>V15 C3.</strong> Somebody else's service runs logic too - which is the argument
    /// <see cref="NodeRoles.IsLogicComponent"/> already makes for <c>SYNC_CHAIN</c>
    /// (<em>"a hop out of your process, however you draw it"</em>) and the one
    /// <see cref="NodeRoles.ProcessesMessages"/> makes for a queue's consumers. A cron reconciling
    /// against the payment provider every night is the most ordinary shape those two types have
    /// together, and it was coming out as work fired at nothing.
    /// </summary>
    private static bool IsWork(NodeType type) =>
        type is NodeType.Worker or NodeType.Service or NodeType.Api
             or NodeType.Queue or NodeType.Topic or NodeType.Gateway or NodeType.ExternalService;
}
