using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Analysis.Rules;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

public class SimulationEngine
{
    public SimulationResult Run(ArchitectureGraph graph, SimulationScenario scenario)
    {
        var (applicable, discarded) = scenario.Resolve(graph);
        var faults = new InjectedFaults(applicable);

        var load = LoadModel.Of(graph);
        var queueing = load.QueueingFactors;

        var nominal = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = queueing });

        var probe = CallCost.Of(graph, faults.AsExpiryProbe() with { QueueingFactors = queueing });

        var elements = new PropagationState(graph, faults);

        RunPhaseA(graph, faults, probe, elements);

        if (applicable.HasDownFault)
            RunPhaseB(graph, faults, elements);

        ApplySaturation(graph, load, elements);

        var faulted = CallCost.Of(graph, faults.AsCostInputs(elements.NodeStates()) with { QueueingFactors = queueing });

        var live = LiveRoots(graph, faults);

        return new SimulationResult(
            SimulationResult.CurrentSimulationVersion,
            elements.ToOrderedList(graph, load),
            Measure(graph, faults, elements, load, nominal, faulted, live),
            discarded,
            faulted.CriticalPathFrom(live));
    }

    private static IReadOnlyList<Node> LiveRoots(ArchitectureGraph graph, InjectedFaults faults) =>
        graph.EntryNodes.Where(n => !faults.IsDown(n.Id)).ToList();


    private static void RunPhaseA(
        ArchitectureGraph graph,
        InjectedFaults faults,
        CallCost probe,
        PropagationState elements)
    {
        var connections = graph.Connections.OrderBy(c => c.Id).ToList();
        var nodes = graph.Nodes.OrderBy(n => n.Id).ToList();

        var maxRounds = graph.Nodes.Count + graph.Connections.Count + 1;

        for (var round = 1; round <= maxRounds; round++)
        {
            var previous = elements.Snapshot();
            var outcomes = new Dictionary<Guid, EdgeOutcome>();
            var changed = false;

            foreach (var edge in connections)
            {
                var outcome = OutcomeOf(graph, faults, probe, previous, edge);
                outcomes[edge.Id] = outcome;

                if (faults.IsSevered(edge.Id))
                    continue;

                changed |= elements.Raise(
                    FindingElement.Connection(edge.Id),
                    outcome.State,
                    round,
                    outcome.Contains,
                    null,
                    outcome.Reason,
                    CauseOfEdge(edge, outcome),
                    ContextOf(graph, probe, edge, outcome));
            }

            foreach (var node in nodes)
            {
                foreach (var contribution in ContributionsOf(graph, node, outcomes))
                {
                    changed |= elements.Raise(
                        FindingElement.Node(node.Id),
                        contribution.Outcome.State,
                        round,
                        contribution.Outcome.Contains,
                        contribution.Outcome.Mechanism,
                        contribution.Outcome.Reason,
                        FindingElement.Connection(contribution.Edge.Id),
                        ContextOf(graph, probe, contribution.Edge, contribution.Outcome));
                }
            }

            if (!changed)
                return;
        }
    }

    private static EdgeOutcome OutcomeOf(
        ArchitectureGraph graph,
        InjectedFaults faults,
        CallCost probe,
        IReadOnlyDictionary<Guid, (HealthState State, bool Contains)> previous,
        Connection edge)
    {
        var severed = faults.IsSevered(edge.Id);
        var target = previous[edge.TargetNodeId];

        return EdgeEffect.Of(
            edge.Synchronous,
            graph.EffectiveResilienceOf(edge),
            severed ? HealthState.Down : target.State,
            !severed && target.Contains,
            probe.Of(edge).Expires);
    }

    private static IEnumerable<(EdgeOutcome Outcome, Connection Edge)> ContributionsOf(
        ArchitectureGraph graph,
        Node node,
        IReadOnlyDictionary<Guid, EdgeOutcome> outcomes)
    {
        // A push to observability carries no request: the collector going down is not the pusher
        // serving worse. The rules already skip telemetry edges; the propagation did not (QA 30-08,
        // §4.1), so losing the telemetry read as every entry point affected.
        var outgoing = graph.OutgoingBySource[node.Id]
            .Where(c => !NodeRoles.IsTelemetryEdge(graph, c))
            .OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id)
            .ToList();

        if (node.Type != NodeType.LoadBalancer)
            return outgoing.Select(edge => (outcomes[edge.Id], edge));

        var healthCheck = NodeSettings.HasHealthCheck(node);
        var contributions = new List<(EdgeOutcome, Connection)>();

        foreach (var group in outgoing.GroupBy(c => graph.NodesById[c.TargetNodeId].Type).OrderBy(g => g.Key))
        {
            var members = group.ToList();

            if (members.Count < 2)
            {
                contributions.AddRange(members.Select(edge => (outcomes[edge.Id], edge)));
                continue;
            }

            var folded = EdgeEffect.OfBalancedGroup(healthCheck, members.Select(m => outcomes[m.Id]).ToList());

            var culprit = members.FirstOrDefault(m => outcomes[m.Id].State == HealthState.Down) ?? members[0];
            contributions.Add((folded, culprit));
        }

        return contributions;
    }


    private static void RunPhaseB(ArchitectureGraph graph, InjectedFaults faults, PropagationState elements)
    {
        var routing = Reachable(graph, faults);

        foreach (var (element, cause, distance) in DistancesFromCuts(graph, faults, routing))
        {
            if (element.Type == ElementType.Connection)
            {
                elements.Raise(element, HealthState.Down, distance, false, null, EffectReason.Unreachable, cause,
                    new ReasonContext(Cause: NameOf(graph, cause)));
                continue;
            }

            var onlyAsync = graph.IncomingByTarget[element.Id].Any() &&
                            graph.IncomingByTarget[element.Id].All(c => !c.Synchronous);

            elements.Raise(
                element,
                onlyAsync ? HealthState.Degraded : HealthState.Down,
                distance,
                false,
                null,
                onlyAsync ? EffectReason.NoWork : EffectReason.Unreachable,
                cause,
                new ReasonContext(Cause: NameOf(graph, cause)));
        }
    }

    // Seeded from where requests enter, not from the roots by the letter: a door the telemetry
    // consults is nobody's root, and with the collector down every box behind it came out stranded.
    // A loose box has no edges, so nothing walks into it: leaving it out of the seeds costs nothing.
    private static HashSet<Guid> Reachable(ArchitectureGraph graph, InjectedFaults faults)
    {
        var reachable = new HashSet<Guid>();
        var pending = new Queue<Guid>(graph.EntryNodes.OrderBy(n => n.Id).Select(n => n.Id));

        while (pending.TryDequeue(out var nodeId))
        {
            if (!reachable.Add(nodeId))
                continue;

            if (faults.IsDown(nodeId))
                continue;

            foreach (var edge in graph.OutgoingBySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                if (!faults.IsSevered(edge.Id))
                    pending.Enqueue(edge.TargetNodeId);
            }
        }

        return reachable;
    }

    private static List<(FindingElement Element, FindingElement Cause, int Distance)> DistancesFromCuts(
        ArchitectureGraph graph,
        InjectedFaults faults,
        HashSet<Guid> reachable)
    {
        var assigned = new List<(FindingElement, FindingElement, int)>();
        var settled = new HashSet<Guid>();
        var seenEdges = new HashSet<Guid>();
        var queue = new Queue<(Guid NodeId, FindingElement Cause, int Distance)>();

        foreach (var nodeId in faults.DownNodes.OrderBy(id => id))
        {
            settled.Add(nodeId);
            queue.Enqueue((nodeId, FindingElement.Node(nodeId), 0));
        }

        foreach (var edge in graph.Connections.Where(c => faults.IsSevered(c.Id)).OrderBy(c => c.Id))
            Strand(edge.TargetNodeId, FindingElement.Connection(edge.Id), 1);

        while (queue.TryDequeue(out var current))
        {
            foreach (var edge in graph.OutgoingBySource[current.NodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                if (!faults.IsSevered(edge.Id) && seenEdges.Add(edge.Id))
                    assigned.Add((FindingElement.Connection(edge.Id), current.Cause, current.Distance + 1));

                Strand(edge.TargetNodeId, current.Cause, current.Distance + 1);
            }
        }

        return assigned;

        void Strand(Guid nodeId, FindingElement cause, int distance)
        {
            if (reachable.Contains(nodeId) || !settled.Add(nodeId))
                return;

            assigned.Add((FindingElement.Node(nodeId), cause, distance));
            queue.Enqueue((nodeId, cause, distance));
        }
    }


    private static void ApplySaturation(ArchitectureGraph graph, LoadModel load, PropagationState elements)
    {
        if (load.IsIdle)
            return;

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            var utilization = load.UtilizationOf(node.Id);
            if (utilization < CapacityModel.SaturationWarning)
                continue;

            var (loadRps, capacityRps) = load.BindingOf(node.Id);

            elements.Raise(
                FindingElement.Node(node.Id),
                HealthState.Degraded,
                0,
                false,
                null,
                utilization > 1m ? EffectReason.Overloaded : EffectReason.Saturated,
                null,
                new ReasonContext(
                    LoadRps: loadRps,
                    CapacityRps: capacityRps,
                    Utilization: utilization));
        }
    }

    private static SimulationMetrics Measure(
        ArchitectureGraph graph,
        InjectedFaults faults,
        PropagationState elements,
        LoadModel load,
        CallCost nominal,
        CallCost faulted,
        IReadOnlyList<Node> live)
    {
        var states = elements.NodeStates();
        var down = states.Values.Count(s => s == HealthState.Down);
        var degraded = states.Values.Count(s => s == HealthState.Degraded);
        var total = graph.Nodes.Count;

        var affected = graph.EntryNodes.Count(n => states[n.Id] != HealthState.Healthy);

        var yield = RequestYield.Of(graph, load, new YieldInputs(
            states.Where(s => s.Value == HealthState.Down).Select(s => s.Key).ToHashSet(),
            graph.Connections.Where(c => faulted.Of(c).Expires).Select(c => c.Id).ToHashSet()));

        var (requestsAffected, bottleneck) = yield.Impact();

        return new SimulationMetrics(
            ComponentsDown: down,
            ComponentsDegraded: degraded,
            ComponentsTotal: total,
            BlastRadius: total == 0 ? 0m : Math.Round((decimal)(down + degraded) / total, 4),
            EntryPointsAffected: affected,
            EntryPointsTotal: graph.EntryNodes.Count,
            NominalLatencyMs: nominal.WorstFrom(graph.EntryNodes) ?? 0,
            FaultedLatencyMs: live.Count == 0 ? null : faulted.WorstFrom(live),
            RequestAmplification: faulted.WorstAmplificationFrom(live),
            RequestsAffected: requestsAffected,
            Bottleneck: bottleneck,
            BacklogRps: Math.Round(load.BacklogRps, 2),
            LoadAmplification: load.LoadAmplification);
    }


    private static FindingElement? CauseOfEdge(Connection edge, EdgeOutcome outcome) =>
        outcome.State == HealthState.Healthy ? null : FindingElement.Node(edge.TargetNodeId);

    private static ReasonContext ContextOf(ArchitectureGraph graph, CallCost cost, Connection edge, EdgeOutcome outcome) =>
        new(
            Target: graph.NodesById[edge.TargetNodeId].Name,
            CallMs: cost.Of(edge).RawLatencyMs,
            TimeoutMs: (graph.ResilienceOf(edge).TimeoutSeconds ?? 0) * 1_000,
            Contained: outcome.Contains);

    private static string NameOf(ArchitectureGraph graph, FindingElement element) =>
        element.Type == ElementType.Node && graph.NodesById.TryGetValue(element.Id, out var node)
            ? node.Name
            : graph.Connections.Where(c => c.Id == element.Id)
                .Select(c => $"{graph.NodesById[c.SourceNodeId].Name} → {graph.NodesById[c.TargetNodeId].Name}")
                .FirstOrDefault() ?? "";
}
