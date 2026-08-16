using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Simulation;

/// <summary>
/// V3 D5.3. Not a list of pieces registered like <c>IArchitectureRule</c> - an algorithm with a
/// fixed pipeline and a pure function where all of block 4's semantics lives:
///
/// <code>
/// architecture + scenario
///    ↓  apply faults          ids that no longer exist are dropped and announced
///    ↓  nominal cost          the walk with no scenario at all
///    ↓  phase A               monotone fixed point towards dependents, double buffered, ≤ V rounds
///    ↓  phase B               reachability, only if some `down` fault was injected
///    ↓  cost under fault      the same walk, now with states and injected latencies
///    ↓  metrics · canonical order
/// </code>
///
/// It reads <see cref="ArchitectureGraph"/> without touching a line of it and
/// <strong>without reading <c>Facts</c></strong>, which is a property rather than an oversight
/// (D5.2): <c>Facts</c> is the producer-to-consumer channel between rules, and a simulator that
/// read from it would give a different answer depending on which rules happen to be registered.
/// Adding a rule in V4 would silently change what killing a node does.
/// </summary>
public class SimulationEngine
{
    public SimulationResult Run(ArchitectureGraph graph, SimulationScenario scenario)
    {
        var (applicable, discarded) = scenario.Resolve(graph);
        var faults = new InjectedFaults(applicable);

        // 0. V4 DT.1: the load axis, computed over the architecture and before any fault. It is a
        //    property of what was drawn, not of the experiment - which is what lets the two axes
        //    compose at the end by taking the worse of the two states, with neither knowing about
        //    the other. With nothing declared every factor is 1 and the rest of this method is the
        //    V3 pipeline, digit for digit.
        var load = LoadModel.Of(graph);
        var queueing = load.QueueingFactors;

        // 1. What the architecture costs when nothing is broken - under its declared load, which is
        //    the half of the comparison the panel shows.
        var nominal = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = queueing });

        // 2. Which calls end up costing more than their own timeout. Phase A needs that answer
        //    before it can compute a single state (D4.1), and it has to be measured over the work
        //    actually done - see AsExpiryProbe.
        //
        //    This is where the two axes meet without EdgeEffect learning anything: a component at
        //    1.725 % of its capacity makes its call cost 6,9 s, and 6,9 s against a 5 s timeout
        //    expires. Saturation kills nobody directly (DT.5) and still takes down an architecture,
        //    through the path V3 already built.
        var probe = CallCost.Of(graph, faults.AsExpiryProbe() with { QueueingFactors = queueing });

        var elements = new PropagationState(graph, faults);

        RunPhaseA(graph, faults, probe, elements);

        // 3. Phase B only exists because a component also stops serving when it is left with no way
        //    in, and that travels the opposite way (plan técnico §6.2).
        if (applicable.HasDownFault)
            RunPhaseB(graph, faults, elements);

        // 4. DT.5: saturation composes with propagation by taking the worse of the two, so the
        //    lattice of V3 D1.3 never gains a fourth value and EdgeEffect keeps its table intact.
        //    Capped at Degraded, always - a component at 150 % serves 67 % of what reaches it, and
        //    calling that Down turns "it rejects a third" into "everything is red" (plan técnico
        //    §2, corrección 1).
        ApplySaturation(graph, load, elements);

        // 5. Now the states are final, the cost can be measured for real.
        var faulted = CallCost.Of(graph, faults.AsCostInputs(elements.NodeStates()) with { QueueingFactors = queueing });

        return new SimulationResult(
            SimulationResult.CurrentSimulationVersion,
            elements.ToOrderedList(graph, load),
            Measure(graph, faults, elements, load, nominal, faulted),
            discarded);
    }

    // -------------------------------------------------------------------------------------------
    // Phase A - the monotone fixed point of D1.5, propagating towards dependents.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Each round, an element's state is the maximum of its current one and the ones its
    /// dependencies impose after the mechanisms of each edge have been applied. Nothing can ever
    /// come down, so over a finite lattice the iteration converges in ≤ V rounds and
    /// <strong>cycles resolve themselves</strong> - which is not an optimisation but the property
    /// that lets the simulator exist over the graphs the product itself encourages you to draw.
    ///
    /// The price, made explicit: a mechanism cannot <em>heal</em> anybody already marked, only stop
    /// the fall from escalating. In practice that is the right semantics - a fallback does not
    /// resurrect anyone, it stops you being dragged down.
    /// </summary>
    private static void RunPhaseA(
        ArchitectureGraph graph,
        InjectedFaults faults,
        CallCost probe,
        PropagationState elements)
    {
        var connections = graph.Connections.OrderBy(c => c.Id).ToList();
        var nodes = graph.Nodes.OrderBy(n => n.Id).ToList();

        // At most one round per element, and it converges long before that.
        var maxRounds = graph.Nodes.Count + graph.Connections.Count + 1;

        for (var round = 1; round <= maxRounds; round++)
        {
            // Double buffer (D5.6): round n+1 is computed over the photograph of round n, never in
            // place, so the order nodes are walked in within a round is irrelevant by construction.
            var previous = elements.Snapshot();
            var outcomes = new Dictionary<Guid, EdgeOutcome>();
            var changed = false;

            foreach (var edge in connections)
            {
                var outcome = OutcomeOf(graph, faults, probe, previous, edge);
                outcomes[edge.Id] = outcome;

                // An edge with an injected fault keeps saying so: it is the element the user
                // pointed at, and "injected here" beats any derived explanation.
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
        // A partition and a dead target are the same call, from the caller's side.
        var severed = faults.IsSevered(edge.Id);
        var target = previous[edge.TargetNodeId];

        return EdgeEffect.Of(
            edge.Synchronous,
            graph.ResilienceOf(edge),
            severed ? HealthState.Down : target.State,
            !severed && target.Contains,
            probe.Of(edge).Expires);
    }

    /// <summary>
    /// What each dependency imposes on a node. For everything but a balancer that is one entry per
    /// outgoing edge; for a LoadBalancer, the instances of one balanced group are folded into a
    /// single entry first, because they are interchangeable rather than each one necessary (D4.8).
    /// </summary>
    private static IEnumerable<(EdgeOutcome Outcome, Connection Edge)> ContributionsOf(
        ArchitectureGraph graph,
        Node node,
        IReadOnlyDictionary<Guid, EdgeOutcome> outcomes)
    {
        var outgoing = graph.OutgoingBySource[node.Id].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id).ToList();

        if (node.Type != NodeType.LoadBalancer)
            return outgoing.Select(edge => (outcomes[edge.Id], edge));

        var healthCheck = NodeSettings.HasHealthCheck(node);
        var contributions = new List<(EdgeOutcome, Connection)>();

        // The same notion of an equivalent sibling SpofRule already counts: nodes of the same type
        // the balancer spreads over, with at least two of them (D3.4).
        foreach (var group in outgoing.GroupBy(c => graph.NodesById[c.TargetNodeId].Type).OrderBy(g => g.Key))
        {
            var members = group.ToList();

            if (members.Count < 2)
            {
                contributions.AddRange(members.Select(edge => (outcomes[edge.Id], edge)));
                continue;
            }

            var folded = EdgeEffect.OfBalancedGroup(healthCheck, members.Select(m => outcomes[m.Id]).ToList());

            // The cause points at the edge towards the instance that went down, which is the one
            // worth drawing - not at some arbitrary member of the group.
            var culprit = members.FirstOrDefault(m => outcomes[m.Id].State == HealthState.Down) ?? members[0];
            contributions.Add((folded, culprit));
        }

        return contributions;
    }

    // -------------------------------------------------------------------------------------------
    // Phase B - reachability. The half of the model that was missing (plan técnico §2 and §6.2).
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Phase A propagates from whoever fails towards whoever depends on them. But killing the
    /// <c>Gateway</c> of <c>worst-case</c> propagates nothing - nobody depends on a gateway - and
    /// used to leave nine components green, in the very architecture where <c>SpofRule</c> shouts
    /// critical SPOF. Analyzer and simulator would have contradicted each other in the product's
    /// flagship example.
    ///
    /// A breadth-first walk from <c>RootNodes</c> that <strong>only stops at elements with an
    /// injected <c>down</c></strong>, never at anything merely Down: a service that is down because
    /// its database fell <em>still routes requests</em> - it just fails them - and phase A already
    /// dealt with that. Confusing "does not serve" with "does not route" would tint half an
    /// architecture red per fault, which is exactly what D4.9 exists to avoid.
    ///
    /// There is no feedback into phase A: if a dependent of X is reachable then so is X through it,
    /// so every dependent of an unreachable node is unreachable too and a second pass changes
    /// nothing.
    /// </summary>
    private static void RunPhaseB(ArchitectureGraph graph, InjectedFaults faults, PropagationState elements)
    {
        var routing = Reachable(graph, faults);

        // The propagation step is the depth of the walk, counted from the element that did the
        // cutting. It is not cosmetic: with a flat step, killing the gateway paints nine components
        // red at once and the wave of D6.3 stops being a wave - it is a flash. With the depth, the
        // fault travels inwards in three steps and you see where it is coming in.
        foreach (var (element, cause, distance) in DistancesFromCuts(graph, faults, routing))
        {
            if (element.Type == ElementType.Connection)
            {
                elements.Raise(element, HealthState.Down, distance, false, null, EffectReason.Unreachable, cause,
                    new ReasonContext(Cause: NameOf(graph, cause)));
                continue;
            }

            // D4.7: a consumer whose only work arrives asynchronously is alive and idle, not dead.
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

    /// <summary>Nodes traffic can still get to, entering through the roots and stopping at the injected cuts.</summary>
    private static HashSet<Guid> Reachable(ArchitectureGraph graph, InjectedFaults faults)
    {
        var reachable = new HashSet<Guid>();
        var pending = new Queue<Guid>(graph.RootNodes.OrderBy(n => n.Id).Select(n => n.Id));

        while (pending.TryDequeue(out var nodeId))
        {
            if (!reachable.Add(nodeId))
                continue;

            // Reached, but it does not forward: this is the only kind of element that cuts.
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

    /// <summary>
    /// Hops from each cut to everything it left stranded, so the wave travels inwards instead of
    /// arriving all at once. Breadth-first, so the distance is the minimum and the step is unique
    /// per element and deterministic.
    /// </summary>
    private static List<(FindingElement Element, FindingElement Cause, int Distance)> DistancesFromCuts(
        ArchitectureGraph graph,
        InjectedFaults faults,
        HashSet<Guid> reachable)
    {
        var assigned = new List<(FindingElement, FindingElement, int)>();
        var settled = new HashSet<Guid>();
        var seenEdges = new HashSet<Guid>();
        var queue = new Queue<(Guid NodeId, FindingElement Cause, int Distance)>();

        // Seeds, enqueued in order of distance so a plain FIFO still walks the graph by levels and
        // every element ends up at its minimum distance - which is what makes the step unique.
        foreach (var nodeId in faults.DownNodes.OrderBy(id => id))
        {
            settled.Add(nodeId);
            queue.Enqueue((nodeId, FindingElement.Node(nodeId), 0));
        }

        // A severed edge cuts too, and what it strands starts one hop from the edge itself.
        foreach (var edge in graph.Connections.Where(c => faults.IsSevered(c.Id)).OrderBy(c => c.Id))
            Strand(edge.TargetNodeId, FindingElement.Connection(edge.Id), 1);

        while (queue.TryDequeue(out var current))
        {
            foreach (var edge in graph.OutgoingBySource[current.NodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                // Everything in this queue is either a cut or already stranded, so none of it
                // routes: its outgoing calls do not happen, whatever they point at.
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

    // -------------------------------------------------------------------------------------------
    // Metrics.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// V4 DT.5. A component at or over 80 % of its capacity is <see cref="HealthState.Degraded"/>,
    /// and <strong>never worse, however far over it goes</strong>.
    ///
    /// That ceiling is the first correction of plan técnico §2, and it is not a nicety: feeding a
    /// saturation-induced <c>Down</c> into <see cref="EdgeEffect"/> turns <c>minimal-api-db</c> at
    /// 300 rps from "the database rejects a third of the requests" into a wholly red architecture,
    /// because the caller has no fallback and inherits it. What is rejected travels on the load
    /// axis, where it can be measured, instead of on the state axis, where it can only tint.
    ///
    /// Runs after both phases so it composes rather than propagates: saturation says how a component
    /// is doing, not what it does to whoever calls it.
    /// </summary>
    private static void ApplySaturation(ArchitectureGraph graph, LoadModel load, PropagationState elements)
    {
        if (load.IsIdle)
            return;

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            var utilization = load.UtilizationOf(node.Id);
            if (utilization < CapacityModel.SaturationWarning)
                continue;

            elements.Raise(
                FindingElement.Node(node.Id),
                HealthState.Degraded,
                0,
                false,
                null,
                utilization > 1m ? EffectReason.Overloaded : EffectReason.Saturated,
                null,
                new ReasonContext(
                    LoadRps: load.LoadOf(node.Id),
                    CapacityRps: load.CapacityOf(node.Id),
                    Utilization: utilization));
        }
    }

    private static SimulationMetrics Measure(
        ArchitectureGraph graph,
        InjectedFaults faults,
        PropagationState elements,
        LoadModel load,
        CallCost nominal,
        CallCost faulted)
    {
        var states = elements.NodeStates();
        var down = states.Values.Count(s => s == HealthState.Down);
        var degraded = states.Values.Count(s => s == HealthState.Degraded);
        var total = graph.Nodes.Count;

        // D3.2: any entry point that does not end up Healthy. A degraded one is serving worse, and
        // saying it is unaffected would be saying nothing happens to the user.
        var affected = graph.RootNodes.Count(n => states[n.Id] != HealthState.Healthy);

        // An entry point with a fault injected on it is not an entry point any more: there is no
        // path left to measure, which is a different thing from a path that costs zero.
        var live = graph.RootNodes.Where(n => !faults.IsDown(n.Id)).ToList();

        // DT.7 and plan técnico §5.4: the *complete* yield - capacity, expiring calls and fault
        // states - which is a different one from the capacity-only yield that fed the retry
        // amplification inside LoadModel. Keeping them apart is what stops the circularity coming
        // back through another door.
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
            EntryPointsTotal: graph.RootNodes.Count,
            NominalLatencyMs: nominal.WorstFrom(graph.RootNodes) ?? 0,
            FaultedLatencyMs: live.Count == 0 ? null : faulted.WorstFrom(live),
            RequestAmplification: faulted.WorstAmplificationFrom(live),
            RequestsAffected: requestsAffected,
            Bottleneck: bottleneck,
            BacklogRps: Math.Round(load.BacklogRps, 2),
            LoadAmplification: load.LoadAmplification);
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A call's cause is whatever it was calling. Chaining it with the node's - whose cause is the
    /// edge - is what reconstructs the propagation path back to the injected fault, and the chain
    /// always terminates: an edge's state in round n comes from node states of round n-1, so every
    /// two hops the step drops strictly, cycles or no cycles.
    /// </summary>
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
