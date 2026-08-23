using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.1. Fan-out, not fan-in: that many components depend on a node is already covered by SPOF
/// from Availability, and counting it again as coupling would penalize the same fact twice.
///
/// The threshold is fixed and not relative to the size of the graph. A relative one ("twice the
/// average") always flags somebody, even in a healthy architecture, and cannot be explained on the
/// finding's card. A fixed one can be wrong, but it can be understood and argued with.
///
/// V13 D1.2. <strong>The advice is the component's.</strong> A Gateway in front of seven services is
/// a gateway; a Service with seven dependencies is a god-service, and "split it by bounded context"
/// is not something a router has any way to do.
///
/// V13 D2.2. And so is the bar. D1.2 left the threshold deliberately blind to the type and said so;
/// this closes it. A router is measured against a doubled threshold, and a LoadBalancer is measured
/// in kinds of thing rather than in instances - because ten replicas behind a balancer are the
/// redundancy this analyzer recommends, and counting them as ten dependencies made the rule punish
/// its own advice.
/// </summary>
public class HighCouplingRule : IArchitectureRule
{
    public string Code => RuleCodes.HighCoupling;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.HIGH_COUPLING.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.HIGH_COUPLING.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A Client fanning out is the users using several doors, not a component accumulating
        // dependencies (QA §3.1).
        // And the telemetry is neither a dependency nor a dependant (QA §4, coverage): the edge
        // to the metrics sink is instrumentation, and a telemetry box scraping nine components is
        // doing its job, not growing responsibilities. Without this, wiring what NO_OBSERVABILITY
        // asks for would have pushed a three-dependency service over the bar.
        var coupled = graph.Nodes
            .Where(node => !NodeRoles.IsTrafficOrigin(node.Type) && !NodeRoles.IsTelemetry(node.Type))
            .Select(node => (Node: node, Edges: graph.OutgoingBySource[node.Id]
                .Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge))
                .ToList()))
            .Where(entry => entry.Edges.Count > 0)
            .ToList();

        if (coupled.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var (node, edges) in coupled)
        {
            var dependencies = FanOutOf(graph, node, edges);

            // V13 D2.2. The bar depends on what the component is for. Everything else about the
            // rule - one finding per node, the edges inside it, the message - does not.
            var router = NodeRoles.IsRouter(node.Type);
            var warning = router ? RuleThresholds.RouterCouplingWarning : RuleThresholds.CouplingWarning;
            var critical = router ? RuleThresholds.RouterCouplingCritical : RuleThresholds.CouplingCritical;

            if (dependencies < warning)
                continue;

            var severity = dependencies >= critical ? Severity.Critical : Severity.Warning;

            // One finding per coupled node with all its edges inside, never one per dependency
            // (D4.5, granularity).
            // The edges keep the order the graph hands them in - by source and target name (V6
            // D1.1) - instead of being re-sorted by Id here. The list is read as a list of
            // dependencies, and Id order reads as no order at all.
            List<FindingElement> elements = [FindingElement.Node(node.Id)];
            elements.AddRange(edges.Select(e => FindingElement.Connection(e.Id)));

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                LocalizedText.Of("finding.HIGH_COUPLING.fanOut.message",
                    ("node", node.Name),
                    ("count", dependencies)),
                LocalizedText.Of("finding.HIGH_COUPLING.fanOut.explanation"),
                Suggest(node),
                elements));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// How many things the component depends on. Distinct components, not edges: depending twice on
    /// the same thing is one dependency.
    ///
    /// <strong>A LoadBalancer counts kinds, not copies</strong> (V13 D2.2). Ten instances of the
    /// same Api behind a balancer is <em>one</em> dependency drawn ten times - it is redundancy, the
    /// thing this analyzer spends its time recommending, and counting it as ten would have made the
    /// rule punish the fix. <c>SPOF</c> has said the same about a balancer since V2 D4.x when it
    /// exempts "the redundancy mechanism itself"; this is that sentence applied to fan-out. A
    /// Gateway is not given the same treatment: two Services behind a gateway are two routes, not
    /// two copies of anything.
    /// </summary>
    private static int FanOutOf(ArchitectureGraph graph, Node node, IReadOnlyList<Connection> edges)
    {
        var targets = edges.Select(e => e.TargetNodeId).Distinct().ToList();

        if (node.Type != NodeType.LoadBalancer)
            return targets.Count;

        return targets
            .Select(id => graph.NodesById.GetValueOrDefault(id))
            .Where(target => target is not null)
            .Select(target => target!.Type)
            .Distinct()
            .Count();
    }

    /// <summary>
    /// V13 D1.2, on <see cref="UndersizedComponentRule"/>'s pattern. Three sentences, because there
    /// are three genuinely different things to say and the old constant said the middle one to
    /// everybody.
    /// </summary>
    private static LocalizedText Suggest(Node node) => LocalizedText.Of(node.Type switch
    {
        // Routing to many is the job. What is worth checking is whether logic leaked into the door.
        _ when NodeRoles.IsRouter(node.Type) => "finding.HIGH_COUPLING.fanOut.suggestion.route",

        NodeType.Api or NodeType.Service or NodeType.Worker => "finding.HIGH_COUPLING.fanOut.suggestion.split",

        // A store or somebody else's service originating four dependencies is unusual enough that
        // the honest sentence is "look at this", not a prescription.
        _ => "finding.HIGH_COUPLING.fanOut.suggestion.review"
    });
}
