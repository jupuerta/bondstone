using arch_forge_backend.Domain.Entities;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.x. A cycle goes to Coupling, not Resilience: a mutual dependency is coupling in its
/// purest form. It is detected, never rejected on save (D3.4) - turning the most instructive
/// pathology in architecture into a form error would forbid exactly what the product teaches.
///
/// V13 D1.2. The suggestion is chosen, not constant. A cycle has no single protagonist type the way
/// a SPOF does, so the axis is the two things that actually change the answer: whether the loop
/// passes through a store two components are both writing to, and whether every hop of it is
/// synchronous. "Invert a dependency or put a queue in between" was being said to all three cases,
/// and it is the wrong advice for the first one - the queue would make the deadlock go away and
/// leave the shared ownership exactly where it was.
/// </summary>
public class CycleRule : IArchitectureRule
{
    public string Code => RuleCodes.Cycle;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.CYCLE.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.CYCLE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // V14 B9. Nothing to check on a graph made only of instrumentation - and that is silence,
        // not a clean bill of health (D1.4).
        if (!graph.Connections.Any(edge => !NodeRoles.IsTelemetryEdge(graph, edge)))
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var cycle in FindCycles(graph))
        {
            // A synchronous cycle is a deadlock waiting to happen; with a queue in between it is
            // a debatable but workable pattern (D4.x).
            var severity = cycle.All(edge => edge.Synchronous) ? Severity.Critical : Severity.Warning;

            foreach (var edge in cycle)
                graph.Facts.Add(new CyclicConnectionFact(edge.Id));

            var names = cycle.Select(edge => graph.NodesById[edge.SourceNodeId].Name).ToList();

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                // The walk is not translatable text: it is the names the user typed, joined by an
                // arrow, and it travels as one argument rather than a list so the catalogue does
                // not have to know how to join anything (V7 D1.1).
                LocalizedText.Of("finding.CYCLE.detected.message",
                    ("count", cycle.Count),
                    ("walk", string.Join(" → ", names) + $" → {names[0]}")),
                LocalizedText.Of("finding.CYCLE.detected.explanation"),
                Suggest(graph, cycle),
                Interleave(cycle)));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// V13 D1.2, on <see cref="UndersizedComponentRule"/>'s pattern - here keyed on what the cycle
    /// is made of rather than on one node's type, because every component in it is equally the
    /// subject of the finding.
    /// </summary>
    private static LocalizedText Suggest(ArchitectureGraph graph, IReadOnlyList<Connection> cycle)
    {
        var participants = cycle
            .Select(edge => graph.NodesById.GetValueOrDefault(edge.SourceNodeId))
            .Where(node => node is not null)
            .ToList();

        // Two components looping through the same store are not badly wired, they are both writing
        // somebody else's data. Rewiring does not fix that, so it is said first (D1.2).
        if (participants.Any(node => NodeRoles.IsDataStore(node!.Type)))
            return LocalizedText.Of("finding.CYCLE.detected.suggestion.sharedData");

        // Every hop blocking is the deadlock case, and there the async hop *is* the fix.
        return LocalizedText.Of(cycle.All(edge => edge.Synchronous)
            ? "finding.CYCLE.detected.suggestion.synchronous"
            // It already has one, so the deadlock is gone and the mutual dependency is not.
            : "finding.CYCLE.detected.suggestion.decouple");
    }

    // The walk, closing back on the node it started from (D2.2).
    private static List<FindingElement> Interleave(IReadOnlyList<Connection> cycle)
    {
        var elements = new List<FindingElement> { FindingElement.Node(cycle[0].SourceNodeId) };

        foreach (var edge in cycle)
        {
            elements.Add(FindingElement.Connection(edge.Id));
            elements.Add(FindingElement.Node(edge.TargetNodeId));
        }

        return elements;
    }

    /// <summary>
    /// DFS with a recursion stack: every edge that closes back onto the current path is a cycle.
    /// Linear, and deterministic because nodes and edges are walked in id order - findings have to
    /// come out in the same order on every run (D2.1, rule 4).
    /// </summary>
    private static List<List<Connection>> FindCycles(ArchitectureGraph graph)
    {
        var cycles = new List<List<Connection>>();
        var reported = new HashSet<string>();
        var visited = new HashSet<Guid>();
        var path = new List<Connection>();
        var onPath = new HashSet<Guid>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
            Walk(node.Id);

        return cycles;

        void Walk(Guid nodeId)
        {
            if (!visited.Add(nodeId))
                return;

            onPath.Add(nodeId);

            foreach (var edge in graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                // V14 B9. Sending your metrics and being scraped back is not a mutual dependency,
                // it is one component instrumented twice - and it is the most ordinary drawing
                // there is. `Api → Obs` plus `Obs → Api` came out a Critical telling the student to
                // break the loop with an asynchronous hop, over a pair of arrows where neither side
                // waits on the other's work. The walk does not traverse instrumentation, so it
                // cannot close a loop through it.
                if (NodeRoles.IsTelemetryEdge(graph, edge))
                    continue;

                if (onPath.Contains(edge.TargetNodeId))
                {
                    var cycle = path.SkipWhile(e => e.SourceNodeId != edge.TargetNodeId).Append(edge).ToList();
                    if (cycle[0].SourceNodeId == edge.TargetNodeId)
                        Record(cycle);

                    continue;
                }

                path.Add(edge);
                Walk(edge.TargetNodeId);
                path.RemoveAt(path.Count - 1);
            }

            onPath.Remove(nodeId);
        }

        void Record(List<Connection> cycle)
        {
            // Canonical form: always start at the lowest id of the cycle. Without it the same
            // cycle would produce as many findings as it has nodes, and the order of findings
            // would stop being deterministic (D4.x).
            var start = cycle.Select((edge, index) => (edge.SourceNodeId, index))
                .OrderBy(entry => entry.SourceNodeId)
                .First().index;

            var canonical = cycle.Skip(start).Concat(cycle.Take(start)).ToList();

            if (reported.Add(string.Join("|", canonical.Select(e => e.Id))))
                cycles.Add(canonical);
        }
    }
}
