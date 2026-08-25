using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V14 §4.3, and the debt of §3.3: the rule that justifies the <see cref="NodeType.Cdn"/> the way
/// V2 D3.2 demands - a type earns its place only if some rule looks at it, and until v7 the CDN was
/// read only by the load model. MISSING_CACHE's twin, one layer out: that one asks for a cache
/// between the logic and the store, this one for the edge between the outside and the door.
///
/// <strong>Only above <see cref="RuleThresholds.CdnWorthTraffic"/></strong>: below it, one door
/// handles the traffic and a CDN is an optimisation nobody should be scolded into; above it, the
/// edge absorbing its hit ratio is the cheapest relief there is - the same lesson the Cdn type was
/// added to teach (QA §3.2).
///
/// <strong>A Warning that asks, never a Critical</strong>: an API serving pure dynamic traffic has
/// nothing for an edge cache to absorb, and the rule cannot see what the responses are made of.
/// And any CDN upstream of the door satisfies it - the rule asks whether the edge exists, not
/// whether it is drawn on the one true path.
/// </summary>
public class MissingCdnRule : IArchitectureRule
{
    public string Code => RuleCodes.MissingCdn;
    public Area Area => Area.Scalability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.MISSING_CDN.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.MISSING_CDN.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable("rule.na.MISSING_CDN.noLoad");

        // The doors the outside actually reaches (B6's notion, not this rule's own), carrying
        // enough traffic for the edge to be the lesson rather than a nicety.
        var busy = EntryPoints.PublicFacing(graph)
            .Where(door => !graph.OrphanNodes.Contains(door.Id))
            .Where(door => load.LoadOf(door.Id) >= RuleThresholds.CdnWorthTraffic)
            .ToList();

        if (busy.Count == 0)
            // The number comes from the threshold and not from the sentence: the prose says "this
            // much traffic" once, in the catalogue, and the figure it names is the one the rule
            // actually compares against.
            return RuleResult.NotApplicable(LocalizedText.Of(
                "rule.na.MISSING_CDN.lowTraffic", ("traffic", RuleThresholds.CdnWorthTraffic)));

        var findings = busy
            .Where(door => !HasCdnUpstream(graph, door))
            .Select(door => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.MISSING_CDN.direct.message",
                    ("node", door.Name),
                    ("loadRps", load.LoadOf(door.Id))),
                LocalizedText.Of("finding.MISSING_CDN.direct.explanation", ("node", door.Name)),
                LocalizedText.Of("finding.MISSING_CDN.direct.suggestion"),
                [FindingElement.Node(door.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }

    /// <summary>Anything on the way in that is an edge cache - telemetry wires excluded, as everywhere (B9).</summary>
    private static bool HasCdnUpstream(ArchitectureGraph graph, Node door)
    {
        var seen = new HashSet<Guid> { door.Id };
        var pending = new Queue<Guid>([door.Id]);

        while (pending.TryDequeue(out var nodeId))
        {
            foreach (var edge in graph.IncomingByTarget[nodeId])
            {
                if (NodeRoles.IsTelemetryEdge(graph, edge) || !seen.Add(edge.SourceNodeId))
                    continue;

                if (graph.NodesById.TryGetValue(edge.SourceNodeId, out var source) && source.Type == NodeType.Cdn)
                    return true;

                pending.Enqueue(edge.SourceNodeId);
            }
        }

        return false;
    }
}
