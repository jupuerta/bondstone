using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class ImplausibleDependencyRule : IArchitectureRule
{
    public string Code => RuleCodes.ImplausibleDependency;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    // V22 §11. Habla del dibujo, no de la arquitectura.
    public bool DescribesTheDrawing => true;

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

                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.explanation",
                    ("source", source),
                    ("target", target)),
                LocalizedText.Of($"finding.IMPLAUSIBLE_DEPENDENCY.{mismatch.Variant}.suggestion",
                    ("source", source),
                    ("target", target)),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static (string Variant, Severity Severity)? Implausible(ArchitectureGraph graph, Connection edge)
    {
        if (!graph.NodesById.TryGetValue(edge.SourceNodeId, out var source)
            || !graph.NodesById.TryGetValue(edge.TargetNodeId, out var target))
            return null;

        if (source.Id == target.Id)
            return null;

        if (NodeRoles.IsSystemOfRecord(source.Type))
            return ("store", Severity.Warning);

        if (NodeRoles.IsTrafficOrigin(target.Type))
            return ("client", Severity.Warning);

        if (source.Type == NodeType.Cdn && !IsOrigin(target.Type))
            return ("cdn", Severity.Critical);

        if (source.Type == NodeType.Scheduler && !IsWork(target.Type))
            return ("scheduler", Severity.Warning);

        // A gateway or a load balancer routes; it does not run SQL. Whatever queries the database is
        // a door that has to authenticate, and calling it a router was how exposed-database was
        // passed without moving a wire (QA 30-08, §1.6).
        if (NodeRoles.IsRouter(source.Type) && target.Type == NodeType.Database)
            return ("routerToDatabase", Severity.Warning);

        if (edge.Synchronous)
        {
            var resilience = graph.ResilienceOf(edge);

            if (NodeRoles.ForwardsRequest(graph, edge) && resilience.Fallback)
                return ("routerFallback", Severity.Warning);

            // A write that does not happen cannot be approximated (QA 30-08, §3.5 and §10.3 point 4):
            // the model neither serves the request with the fallback nor discounts the dependency,
            // and the button never offers one — a ticked one is the drawing promising a plan B.
            if (resilience.Fallback && resilience.DeclaresWrite)
                return ("writeFallback", Severity.Warning);

            return null;
        }

        // An arrow that says «nobody waits» where somebody has to (QA 30-08, §3.1). Unticking the box
        // does not change what the call is; it makes the drawing contradict itself, and every rule
        // that reads «synchronous» goes quiet on a dependency that is still there. Critical, not
        // a warning: the box is the single cheapest way of making a failure disappear from the model.
        if (NodeRoles.IsTrafficOrigin(source.Type))
            return ("asyncClient", Severity.Critical);

        if (NodeRoles.IsRouter(source.Type) || source.Type == NodeType.Cdn)
            return ("asyncRouter", Severity.Critical);

        if (target.Type == NodeType.IdentityProvider)
            return ("asyncIdentity", Severity.Critical);

        if (source.Type == NodeType.Cache)
            return ("asyncCacheMiss", Severity.Critical);

        // Not «declares a read» but «is not a declared write», because undeclared is the default and
        // LoadModel already counts it as a read: asking for a box to be ticked before believing the
        // arrow made the commonest drawing of all the one nobody checked (QA 30-08, §10.3 point 6).
        if (NodeRoles.IsSystemOfRecord(target.Type) && !graph.ResilienceOf(edge).DeclaresWrite)
            return ("asyncRead", Severity.Critical);

        // A door that waits for nobody. Its callers are outside, watching a screen, and every call it
        // makes says «I do not wait» — so there is no answer to give and nothing was left anywhere to
        // do later either. A door that queues the work has accepted it and can say «received», which
        // is what `backlog` teaches, so a broker among its calls is the honest version of this shape
        // (QA 30-08, §10.3 point 7).
        if (WaitsForNobody(graph, source))
            return ("asyncDoor", Severity.Critical);

        return null;
    }

    // The shape, not the arrow: every call this door makes is asynchronous and none of them leaves the
    // work in a broker. Shared by the rule and by RequestYield, which serves nothing through it.
    public static bool WaitsForNobody(ArchitectureGraph graph, Node door)
    {
        if (!NodeRoles.ReceivesFromOutside(door.Type) || !graph.EntryNodes.Any(n => n.Id == door.Id))
            return false;

        var calls = graph.OutgoingBySource[door.Id]
            .Where(edge => !NodeRoles.IsTelemetryEdge(graph, edge))
            .ToList();

        return calls.Count > 0
               && calls.All(edge => !edge.Synchronous)
               && !calls.Any(edge => graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
                                     && NodeRoles.IsBroker(target.Type));
    }

    private static bool IsOrigin(NodeType type) =>
        type is NodeType.Api or NodeType.Gateway or NodeType.LoadBalancer
             or NodeType.Service or NodeType.ObjectStorage or NodeType.ExternalService;

    private static bool IsWork(NodeType type) =>
        type is NodeType.Worker or NodeType.Service or NodeType.Api
             or NodeType.Queue or NodeType.Topic or NodeType.Gateway or NodeType.ExternalService;
}
