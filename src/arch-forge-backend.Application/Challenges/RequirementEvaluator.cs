using bondstone_backend.Application.Analysis;
using bondstone_backend.Domain.Enums;
using bondstone_backend.Application.Analysis.Rules;
using bondstone_backend.Application.Configuration.Schemas;
using bondstone_backend.Application.Localization;
using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Entities;

namespace bondstone_backend.Application.Challenges;

public static class RequirementEvaluator
{
    public static ChallengeRequirementResult Evaluate(ChallengeRequirement requirement, EvaluationContext context) =>
        requirement.Kind switch
        {
            RequirementKinds.ContainsNode => ContainsNode(requirement, context),
            RequirementKinds.AbsentNode => AbsentNode(requirement, context),
            RequirementKinds.MaxFanOut => MaxFanOut(requirement, context),
            RequirementKinds.AllEdgesTo => AllEdgesTo(requirement, context),
            RequirementKinds.PathExists => PathExists(requirement, context),
            RequirementKinds.DeclaresLoad => DeclaresLoad(requirement, context),
            RequirementKinds.MaxNodes => MaxNodes(requirement, context),
            RequirementKinds.MaxInstances => MaxInstances(requirement, context),
            RequirementKinds.MaxCost => MaxCost(requirement, context),
            RequirementKinds.Edge => Edge(requirement, context),
            RequirementKinds.SpansZones => SpansZones(requirement, context),
            RequirementKinds.NoFinding => NoFinding(requirement, context),
            RequirementKinds.HasFinding => HasFinding(requirement, context),
            RequirementKinds.NoSeverity => NoSeverity(requirement, context),
            RequirementKinds.UnderScenario => UnderScenario(requirement, context),

            _ => ChallengeRequirementResult.Fail(requirement,
                LocalizedText.Of("challenge.requirement.unknownKind", ("kind", requirement.Kind)))
        };


    private static ChallengeRequirementResult ContainsNode(ChallengeRequirement requirement, EvaluationContext context)
    {
        var min = requirement.Min ?? 1;
        var found = context.Graph.Nodes.Where(n => n.Type == requirement.NodeType).ToList();

        if (found.Count >= min)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(requirement, min == 1
            ? LocalizedText.Of("challenge.requirement.containsNode.none", ("nodeType", requirement.NodeType!))
            : LocalizedText.Of("challenge.requirement.containsNode.tooFew",
                ("nodeType", requirement.NodeType!),
                ("count", found.Count),
                ("min", min)));
    }

    private static ChallengeRequirementResult AbsentNode(ChallengeRequirement requirement, EvaluationContext context)
    {
        var found = context.Graph.Nodes.Where(n => n.Type == requirement.NodeType).ToList();

        if (found.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.absentNode.present",
                ("nodeType", requirement.NodeType!),
                ("count", found.Count),
                ("nodes", Names(found))),
            found.Select(n => FindingElement.Node(n.Id)).ToList());
    }

    private static ChallengeRequirementResult MaxFanOut(ChallengeRequirement requirement, EvaluationContext context)
    {
        var max = requirement.Max!.Value;

        var offenders = context.Graph.Nodes
            .Select(n => (Node: n, FanOut: context.Graph.OutgoingBySource[n.Id].Count()))
            .Where(x => x.FanOut > max)
            .OrderByDescending(x => x.FanOut)
            .ThenBy(x => x.Node.Id)
            .ToList();

        if (offenders.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        var worst = offenders[0];
        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.maxFanOut.exceeded",
                ("node", worst.Node.Name),
                ("count", worst.FanOut),
                ("max", max)),
            offenders.Select(x => FindingElement.Node(x.Node.Id)).ToList());
    }

    // A call that does not wait has no timeout, breaker or fallback to declare, so it cannot satisfy
    // this by unticking a box: it is an offender unless a broker sits at one of its ends, which is
    // the one legitimate way of not waiting. Until 30-08 asynchronous calls were skipped here, and
    // every mechanism the catalogue asked for was met by leaving «synchronous» blank (QA §3.3).
    private static ChallengeRequirementResult AllEdgesTo(ChallengeRequirement requirement, EvaluationContext context)
    {
        var edges = context.Graph.Connections
            .Where(c => context.Graph.NodesById.TryGetValue(c.TargetNodeId, out var target) && target.Type == requirement.NodeType)
            .Where(c => c.Synchronous || !TouchesABroker(context.Graph, c))
            .ToList();

        var unwaiting = edges.Where(c => !c.Synchronous).ToList();
        var offenders = edges.Where(c => !c.Synchronous || !Has(context.Graph.ResilienceOf(c), requirement.Mechanism!)).ToList();

        if (offenders.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        var mechanism = LocalizedText.Of($"mechanism.{requirement.Mechanism!}");

        if (offenders.Count == unwaiting.Count)
            return ChallengeRequirementResult.Fail(
                requirement,
                LocalizedText.Of("challenge.requirement.allEdgesTo.asynchronous",
                    ("nodeType", requirement.NodeType!),
                    ("count", unwaiting.Count),
                    ("total", edges.Count),
                    ("mechanism", mechanism),
                    ("calls", Calls(unwaiting, context.Graph))),
                unwaiting.Select(c => FindingElement.Connection(c.Id)).ToList());

        var detail = offenders.Count == edges.Count
            ? LocalizedText.Of("challenge.requirement.allEdgesTo.none",
                ("nodeType", requirement.NodeType!),
                ("count", edges.Count),
                ("mechanism", mechanism))
            : LocalizedText.Of("challenge.requirement.allEdgesTo.some",
                ("nodeType", requirement.NodeType!),
                ("count", offenders.Count),
                ("total", edges.Count),
                ("mechanism", mechanism),
                ("calls", Calls(offenders, context.Graph)));

        return ChallengeRequirementResult.Fail(
            requirement,
            detail,
            offenders.Select(c => FindingElement.Connection(c.Id)).ToList());
    }

    private static ChallengeRequirementResult PathExists(ChallengeRequirement requirement, EvaluationContext context)
    {
        var pending = new Queue<Guid>(context.Graph.Nodes.Where(n => n.Type == requirement.FromType).Select(n => n.Id));
        var seen = new HashSet<Guid>();

        while (pending.Count > 0)
        {
            foreach (var edge in context.Graph.OutgoingBySource[pending.Dequeue()])
            {
                if (!seen.Add(edge.TargetNodeId))
                    continue;

                if (context.Graph.NodesById.TryGetValue(edge.TargetNodeId, out var target) && target.Type == requirement.ToType)
                    return ChallengeRequirementResult.Pass(requirement);

                pending.Enqueue(edge.TargetNodeId);
            }
        }

        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.pathExists.none",
                ("fromType", requirement.FromType!),
                ("toType", requirement.ToType!)));
    }

    private static ChallengeRequirementResult DeclaresLoad(ChallengeRequirement requirement, EvaluationContext context)
    {
        // The same reading as the load model: where traffic enters, or a client. A loose box with a
        // number on it is not where anything enters (QA 30-08, 9.5), and neither is a box that
        // stopped being an entry and kept its number (§10.3 point 8).
        var declared = context.Graph.Nodes
            .Where(context.Graph.DeclaresTraffic)
            .Sum(node => NodeSettings.DeclaredRps(node));

        if (declared >= requirement.Min!.Value)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            declared <= 0
                ? LocalizedText.Of("challenge.requirement.declaresLoad.none", ("min", requirement.Min!.Value))
                : LocalizedText.Of("challenge.requirement.declaresLoad.tooLittle",
                    ("declared", declared),
                    ("min", requirement.Min!.Value)),
            context.Graph.EntryNodes.Select(n => FindingElement.Node(n.Id)).ToList());
    }

    private static ChallengeRequirementResult MaxNodes(ChallengeRequirement requirement, EvaluationContext context)
    {
        var max = requirement.Max!.Value;
        var count = context.Graph.Nodes.Count;

        if (count <= max)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(requirement,
            LocalizedText.Of("challenge.requirement.maxNodes.exceeded", ("count", count), ("max", max)));
    }

    private static ChallengeRequirementResult MaxInstances(ChallengeRequirement requirement, EvaluationContext context)
    {
        var max = requirement.Max!.Value;

        var offenders = context.Graph.Nodes
            .Where(n => requirement.NodeType is null || n.Type == requirement.NodeType)
            .Select(n => (Node: n, Instances: NodeSettings.Instances(n)))
            .Where(x => x.Instances > max)
            .OrderByDescending(x => x.Instances)
            .ThenBy(x => x.Node.Id)
            .ToList();

        if (offenders.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        var worst = offenders[0];
        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.maxInstances.exceeded",
                ("node", worst.Node.Name),
                ("count", worst.Instances),
                ("max", max)),
            offenders.Select(x => FindingElement.Node(x.Node.Id)).ToList());
    }

    // A ceiling says "you went over". A budget has to say where it goes, or it is a ceiling with
    // another name: the total, the cap, and the priciest component by its name. The verdict is
    // CostModel.Of, the single source of truth; the breakdown only picks who to point at.
    private static ChallengeRequirementResult MaxCost(ChallengeRequirement requirement, EvaluationContext context)
    {
        var max = requirement.Max!.Value;
        var total = CostModel.Of(context.Graph);

        var billed = context.Graph.Nodes
            .Where(n => NodeRoles.IsYoursToPayFor(n.Type))
            .Select(n => (Node: n, Cost: CostModel.TotalFor(n)))
            .Where(x => x.Cost > 0)
            .OrderByDescending(x => x.Cost)
            .ThenBy(x => x.Node.Id)
            .ToList();

        if (total <= max || billed.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        var worst = billed[0];

        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.maxCost.exceeded",
                ("total", total),
                ("max", max),
                ("node", worst.Node.Name),
                ("cost", worst.Cost)),
            billed.Select(x => FindingElement.Node(x.Node.Id)).ToList());
    }

    private static ChallengeRequirementResult Edge(ChallengeRequirement requirement, EvaluationContext context)
    {
        var edges = context.Graph.Connections
            .Where(c => Named(context, c.SourceNodeId, requirement.From!) && Named(context, c.TargetNodeId, requirement.To!))
            .ToList();

        // A call asked not to wait may go through a broker instead of straight: then the two hops are
        // the call, and both of them have to be asynchronous for it to count.
        if (edges.Count == 0 && requirement.Synchronous == false)
            edges = ThroughABroker(context, requirement.From!, requirement.To!);

        if (edges.Count == 0)
            return ChallengeRequirementResult.Fail(requirement,
                LocalizedText.Of("challenge.requirement.edge.missing",
                    ("from", requirement.From!),
                    ("to", requirement.To!)));

        if (requirement.Synchronous is { } synchronous)
        {
            var wrong = edges.Where(c => c.Synchronous != synchronous).ToList();

            if (wrong.Count > 0)
                return ChallengeRequirementResult.Fail(
                    requirement,
                    LocalizedText.Of(synchronous
                            ? "challenge.requirement.edge.notSynchronous"
                            : "challenge.requirement.edge.notAsynchronous",
                        ("from", requirement.From!),
                        ("to", requirement.To!)),
                    wrong.Select(c => FindingElement.Connection(c.Id)).ToList());
        }

        if (requirement.Mechanism is { } mechanism)
        {
            // A call that does not wait has nothing to declare: the mechanism asked of it is met by
            // waiting and declaring it, not by unticking the box (QA 30-08, §3.3).
            var unwaiting = edges.Where(c => !c.Synchronous).ToList();

            if (unwaiting.Count > 0)
                return ChallengeRequirementResult.Fail(
                    requirement,
                    LocalizedText.Of("challenge.requirement.edge.asynchronous",
                        ("from", requirement.From!),
                        ("to", requirement.To!),
                        ("mechanism", LocalizedText.Of($"mechanism.{mechanism}"))),
                    unwaiting.Select(c => FindingElement.Connection(c.Id)).ToList());

            var wrong = edges.Where(c => !Has(context.Graph.ResilienceOf(c), mechanism)).ToList();

            if (wrong.Count > 0)
                return ChallengeRequirementResult.Fail(
                    requirement,
                    LocalizedText.Of("challenge.requirement.edge.withoutMechanism",
                        ("from", requirement.From!),
                        ("to", requirement.To!),
                        ("mechanism", LocalizedText.Of($"mechanism.{mechanism}"))),
                    wrong.Select(c => FindingElement.Connection(c.Id)).ToList());
        }

        return ChallengeRequirementResult.Pass(requirement);
    }

    // Two copies are only two if they can go down separately, and the only thing that says where a
    // copy is, is its zone. A copy that declares none is not "somewhere else": it is unplaced, and it
    // counts against the drawing here for the same reason SpofRule stays quiet about it there — "I do
    // not know where it is" cannot pass as "it is in another zone".
    private static ChallengeRequirementResult SpansZones(ChallengeRequirement requirement, EvaluationContext context)
    {
        var min = requirement.Min ?? 2;
        var nodes = context.Graph.Nodes.Where(n => n.Type == requirement.NodeType).OrderBy(n => n.Id).ToList();
        var unplaced = nodes.Where(n => NodeSettings.Zone(n) is null).ToList();
        var zones = nodes.Select(NodeSettings.Zone).OfType<string>().Distinct(StringComparer.Ordinal).Count();

        if (zones >= min)
            return ChallengeRequirementResult.Pass(requirement);

        if (unplaced.Count > 0)
            return ChallengeRequirementResult.Fail(
                requirement,
                LocalizedText.Of("challenge.requirement.spansZones.unplaced",
                    ("nodeType", requirement.NodeType!),
                    ("count", unplaced.Count),
                    ("nodes", Names(unplaced)),
                    ("min", min)),
                unplaced.Select(n => FindingElement.Node(n.Id)).ToList());

        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.spansZones.tooFew",
                ("nodeType", requirement.NodeType!),
                ("count", zones),
                ("min", min)),
            nodes.Select(n => FindingElement.Node(n.Id)).ToList());
    }

    private static bool Named(EvaluationContext context, Guid nodeId, string name) =>
        context.Graph.NodesById.TryGetValue(nodeId, out var node) &&
        string.Equals(node.Name, name, StringComparison.Ordinal);


    private static List<RuleFinding> Problems(EvaluationContext context, string code) =>
        context.Analysis.Findings
            .Where(f => f.Severity != Severity.Good && string.Equals(f.Code, code, StringComparison.Ordinal))
            .ToList();

    private static ChallengeRequirementResult NoFinding(ChallengeRequirement requirement, EvaluationContext context)
    {
        var found = Problems(context, requirement.Code!);

        if (found.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            found.Count == 1
                ? found[0].Message
                : LocalizedText.Of("challenge.requirement.noFinding.several",
                    ("count", found.Count),
                    ("finding", found[0].Message)),
            found.SelectMany(f => f.Elements).Distinct().ToList());
    }

    private static ChallengeRequirementResult HasFinding(ChallengeRequirement requirement, EvaluationContext context) =>
        Problems(context, requirement.Code!).Count > 0
            ? ChallengeRequirementResult.Pass(requirement)
            : ChallengeRequirementResult.Fail(requirement, LocalizedText.Of("challenge.requirement.hasFinding.absent"));

    private static ChallengeRequirementResult NoSeverity(ChallengeRequirement requirement, EvaluationContext context)
    {
        var found = context.Analysis.Findings
            .Where(f => f.Severity != Severity.Good && f.Severity >= requirement.Severity)
            .ToList();

        if (found.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.noSeverity.remaining",
                ("count", found.Count),
                ("severity", requirement.Severity!),
                ("finding", found[0].Message)),
            found.SelectMany(f => f.Elements).Distinct().ToList());
    }


    private static ChallengeRequirementResult UnderScenario(ChallengeRequirement requirement, EvaluationContext context)
    {
        if (!context.TryResolve(requirement.Faults!, out var scenario, out var missing))
            return ChallengeRequirementResult.Fail(
                requirement,
                LocalizedText.Of("challenge.requirement.underScenario.missingNode", ("node", missing!)));

        var simulation = context.Simulate(scenario);

        if (simulation.DiscardedFaults.Count > 0)
            return ChallengeRequirementResult.Fail(
                requirement,
                LocalizedText.Of("challenge.requirement.underScenario.discarded"));

        var actual = Read(simulation.Metrics, requirement.Metric!);

        if (actual is null)
            return ChallengeRequirementResult.Fail(
                requirement,
                LocalizedText.Of("challenge.requirement.underScenario.noLatency"),
                Affected(simulation));

        if (RequirementOperators.Compare(actual.Value, requirement.Operator!, requirement.Value!.Value))
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            LocalizedText.Of("challenge.requirement.underScenario.metric",
                ("metric", LocalizedText.Of($"metric.{requirement.Metric!}")),
                ("actual", Format(requirement.Metric!, actual.Value)),
                ("operator", LocalizedText.Of($"operator.{requirement.Operator!}")),
                ("expected", Format(requirement.Metric!, requirement.Value!.Value))),
            Affected(simulation));
    }

    private static IReadOnlyList<FindingElement> Affected(SimulationResult simulation) =>
        simulation.States
            .Where(s => s.State != HealthState.Healthy)
            .Select(s => new FindingElement(s.Type, s.Id))
            .ToList();

    private static decimal? Read(SimulationMetrics metrics, string metric) => metric switch
    {
        RequirementMetrics.ComponentsDown => metrics.ComponentsDown,
        RequirementMetrics.ComponentsDegraded => metrics.ComponentsDegraded,
        RequirementMetrics.BlastRadius => metrics.BlastRadius,
        RequirementMetrics.EntryPointsAffected => metrics.EntryPointsAffected,
        RequirementMetrics.NominalLatencyMs => metrics.NominalLatencyMs,
        RequirementMetrics.FaultedLatencyMs => metrics.FaultedLatencyMs,
        RequirementMetrics.RequestAmplification => metrics.RequestAmplification,
        RequirementMetrics.RequestsAffected => metrics.RequestsAffected,
        RequirementMetrics.BacklogRps => metrics.BacklogRps,
        RequirementMetrics.LoadAmplification => metrics.LoadAmplification,
        _ => null
    };


    private static List<Connection> ThroughABroker(EvaluationContext context, string from, string to)
    {
        var graph = context.Graph;

        return graph.Connections
            .Where(first => Named(context, first.SourceNodeId, from))
            .Where(first => graph.NodesById.TryGetValue(first.TargetNodeId, out var broker)
                            && broker.Type is NodeType.Queue or NodeType.Topic)
            .SelectMany(first => graph.OutgoingBySource[first.TargetNodeId]
                .Where(second => Named(context, second.TargetNodeId, to))
                .SelectMany(second => new[] { first, second }))
            .Distinct()
            .ToList();
    }

    private static bool TouchesABroker(ArchitectureGraph graph, Connection edge) =>
        IsBroker(graph, edge.SourceNodeId) || IsBroker(graph, edge.TargetNodeId);

    private static bool IsBroker(ArchitectureGraph graph, Guid nodeId) =>
        graph.NodesById.TryGetValue(nodeId, out var node) && node.Type is NodeType.Queue or NodeType.Topic;

    private static bool Has(ResilienceConfiguration resilience, string mechanism) => mechanism switch
    {
        RequirementMechanisms.Timeout => resilience.TimeoutSeconds is not null,
        RequirementMechanisms.Retries => resilience.Retries is > 0,
        RequirementMechanisms.CircuitBreaker => resilience.CircuitBreaker,
        RequirementMechanisms.Fallback => resilience.Fallback,
        _ => false
    };


    private static LocalizedText Format(string metric, decimal value) =>
        LocalizedText.Of($"metric.{metric}.value", ("value", value));

    private static string Names(IEnumerable<Node> nodes) =>
        string.Join(", ", nodes.Select(n => $"'{n.Name}'"));

    private static string Calls(IEnumerable<Connection> edges, ArchitectureGraph graph) =>
        string.Join(", ", edges.Select(c =>
            $"'{graph.NodesById[c.SourceNodeId].Name}' → '{graph.NodesById[c.TargetNodeId].Name}'"));
}
