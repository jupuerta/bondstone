using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Configuration.Schemas;
using arch_forge_backend.Application.Localization;
using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Challenges;

/// <summary>
/// V4 D3.1 and D1.2, the whole evaluator as one pure function over three inputs.
///
/// <strong>It calculates nothing.</strong> The structural family walks the graph; the analytical one
/// reads an <see cref="AnalysisResult"/>; the simulated one reads a
/// <see cref="SimulationResult"/>. Both of those contracts already exist, are already deterministic
/// and are already covered by tests, which is what turns "evaluate a challenge" into a
/// <c>switch</c> rather than a third engine next to the analyzer and the simulator.
///
/// <strong>No branch reads <see cref="AnalysisResult.Score"/>, and that is D1.4 made mechanical.</strong>
/// V4 lands seven rules and two areas that scored nothing start scoring, so every score moves; a
/// challenge pinned to a number would fail solutions nobody touched. There is a test that walks
/// this file's dependencies to keep it that way.
/// </summary>
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
            RequirementKinds.Edge => Edge(requirement, context),
            RequirementKinds.NoFinding => NoFinding(requirement, context),
            RequirementKinds.HasFinding => HasFinding(requirement, context),
            RequirementKinds.NoSeverity => NoSeverity(requirement, context),
            RequirementKinds.UnderScenario => UnderScenario(requirement, context),

            // Unreachable: the catalogue refuses to load an unknown kind (D1.5). Answering "not
            // met" rather than throwing keeps one malformed requirement from taking down the
            // evaluation of the other four.
            _ => ChallengeRequirementResult.Fail(requirement,
                LocalizedText.Of("challenge.requirement.unknownKind", ("kind", requirement.Kind)))
        };

    // ---- Estructural -------------------------------------------------------

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

    /// <summary>
    /// Synchronous edges only, and that is V3 D4.2 rather than a shortcut: on an asynchronous edge
    /// nobody waits for an answer, so there is no error for a timeout or a fallback to catch.
    ///
    /// An architecture with no such edge at all passes vacuously. That is the standard reading and
    /// it is not a hole: a challenge that also wants the component to exist says so with its own
    /// <see cref="RequirementKinds.ContainsNode"/>, which is one line and reads better in the
    /// checklist than a compound requirement would.
    /// </summary>
    private static ChallengeRequirementResult AllEdgesTo(ChallengeRequirement requirement, EvaluationContext context)
    {
        var edges = context.Graph.Connections
            .Where(c => c.Synchronous)
            .Where(c => context.Graph.NodesById.TryGetValue(c.TargetNodeId, out var target) && target.Type == requirement.NodeType)
            .ToList();

        var offenders = edges.Where(c => !Has(context.Graph.ResilienceOf(c), requirement.Mechanism!)).ToList();

        if (offenders.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        // The mechanism's name is a nested key rather than a word pasted in: "respuesta
        // alternativa" is "fallback" in English, and a sentence that interpolates an already
        // translated fragment can only ever be right in the language it was written in.
        var mechanism = LocalizedText.Of($"mechanism.{requirement.Mechanism!}");

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
        // A path of at least one hop: the sources seed the walk but do not count as arrivals, so
        // `pathExists` from a type to itself asks for two components and not for one.
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

    /// <summary>
    /// V4 VS4. Traffic is a property of the architecture (DT.1), so the user can edit it - and
    /// "everything under 80 % at 300 rps" would otherwise be solved by declaring 1 rps. It is the
    /// same shape of anti-cheese as pointing at components by name: the challenge checks that the
    /// question is still being asked.
    /// </summary>
    private static ChallengeRequirementResult DeclaresLoad(ChallengeRequirement requirement, EvaluationContext context)
    {
        var declared = context.Graph.RootNodes.Sum(node => NodeSettings.DeclaredRps(node));

        if (declared >= requirement.Min!.Value)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            declared <= 0
                ? LocalizedText.Of("challenge.requirement.declaresLoad.none", ("min", requirement.Min!.Value))
                : LocalizedText.Of("challenge.requirement.declaresLoad.tooLittle",
                    ("declared", declared),
                    ("min", requirement.Min!.Value)),
            context.Graph.RootNodes.Select(n => FindingElement.Node(n.Id)).ToList());
    }

    /// <summary>
    /// V8 §6.1, the anti-cheese that was missing. <see cref="DeclaresLoad"/> keeps the question
    /// being asked; this keeps the answer from being "buy more of everything".
    /// </summary>
    private static ChallengeRequirementResult MaxNodes(ChallengeRequirement requirement, EvaluationContext context)
    {
        var max = requirement.Max!.Value;
        var count = context.Graph.Nodes.Count;

        if (count <= max)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(requirement,
            LocalizedText.Of("challenge.requirement.maxNodes.exceeded", ("count", count), ("max", max)));
    }

    /// <summary>
    /// A cap on copies rather than on components. It reads the same
    /// <see cref="NodeSettings.Instances"/> the capacity model reads, so a database's replicas count
    /// as copies here exactly as they count as capacity there - which is the whole point: the
    /// challenge caps the lever that would otherwise solve it.
    /// </summary>
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

    /// <summary>
    /// V8 §6.4. One call, named by its two ends.
    ///
    /// Several calls between the same pair are treated as one requirement over all of them: two
    /// components can be wired twice, and passing "the charge does not block" while one of the two
    /// copies still blocks would be a checklist that lies. Missing entirely is its own failure and
    /// its own sentence - that is the anti-cheese, and it has to say so rather than pass vacuously
    /// the way <see cref="AllEdgesTo"/> does over a type nobody drew.
    /// </summary>
    private static ChallengeRequirementResult Edge(ChallengeRequirement requirement, EvaluationContext context)
    {
        var edges = context.Graph.Connections
            .Where(c => Named(context, c.SourceNodeId, requirement.From!) && Named(context, c.TargetNodeId, requirement.To!))
            .ToList();

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
            // Only a synchronous call can carry one (V3 D4.2): on an asynchronous edge nobody is
            // waiting, so there is no error for the mechanism to catch and asking for it would be
            // asking for a checkbox.
            var wrong = edges.Where(c => c.Synchronous && !Has(context.Graph.ResilienceOf(c), mechanism)).ToList();

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

    private static bool Named(EvaluationContext context, Guid nodeId, string name) =>
        context.Graph.NodesById.TryGetValue(nodeId, out var node) &&
        string.Equals(node.Name, name, StringComparison.Ordinal);

    // ---- Analítica ---------------------------------------------------------

    /// <summary>
    /// <c>Good</c> findings are excluded on purpose, and this is not a detail: the analyzer
    /// <em>synthesizes</em> a finding with the rule's own code when the rule passes (V2 D1.4).
    /// Counting those, <c>noFinding: SPOF</c> would be unsatisfiable - it would fail precisely when
    /// SPOF is happy.
    /// </summary>
    private static List<RuleFinding> Problems(EvaluationContext context, string code) =>
        context.Analysis.Findings
            .Where(f => f.Severity != Severity.Good && string.Equals(f.Code, code, StringComparison.Ordinal))
            .ToList();

    private static ChallengeRequirementResult NoFinding(ChallengeRequirement requirement, EvaluationContext context)
    {
        var found = Problems(context, requirement.Code!);

        if (found.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        // The finding's own message is already written to be read - reproducing it here in worse
        // words would be the same sentence twice in the same panel.
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

    /// <summary>
    /// <c>Severity</c> <em>or worse</em>: asking for "no warnings" while a critical slipped past
    /// would be a checklist that lies, and the lattice already orders them.
    /// </summary>
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

    // ---- Simulada ----------------------------------------------------------

    private static ChallengeRequirementResult UnderScenario(ChallengeRequirement requirement, EvaluationContext context)
    {
        if (!context.TryResolve(requirement.Faults!, out var scenario, out var missing))
            return ChallengeRequirementResult.Fail(
                requirement,
                LocalizedText.Of("challenge.requirement.underScenario.missingNode", ("node", missing!)));

        var simulation = context.Simulate(scenario);

        // The engine drops faults pointing at elements that are gone, on purpose: a link shared
        // before the architecture was edited has to keep opening (V3 D3.6). For a requirement that
        // rule is backwards - a scenario that lost its fault runs over a healthy architecture and
        // <em>passes</em>, so "it survives Payments going down" would be met by an architecture
        // that never posed the question. Anything discarded fails, loudly.
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

    /// <summary>What to light up: everything the scenario did not leave healthy.</summary>
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

    // ---- Textos ------------------------------------------------------------

    private static bool Has(ResilienceConfiguration resilience, string mechanism) => mechanism switch
    {
        RequirementMechanisms.Timeout => resilience.TimeoutSeconds is not null,
        RequirementMechanisms.Retries => resilience.Retries is > 0,
        RequirementMechanisms.CircuitBreaker => resilience.CircuitBreaker,
        RequirementMechanisms.Fallback => resilience.Fallback,
        _ => false
    };

    // MechanismName and MetricName used to live here, turning a catalogue value into a Spanish
    // word. They are now keys - `mechanism.timeout`, `metric.blastRadius` - resolved wherever the
    // sentence around them is resolved, which is the only place that knows the language.

    /// <summary>
    /// A metric's value with its unit. Each metric owns a second key - <c>metric.blastRadius.value</c>
    /// - holding the shape of its own figure, because the unit is part of the sentence and a
    /// percentage does not read the same in every language as <c>43.5%</c>.
    /// </summary>
    private static LocalizedText Format(string metric, decimal value) =>
        LocalizedText.Of($"metric.{metric}.value", ("value", value));

    private static string Names(IEnumerable<Node> nodes) =>
        string.Join(", ", nodes.Select(n => $"'{n.Name}'"));

    private static string Calls(IEnumerable<Connection> edges, ArchitectureGraph graph) =>
        string.Join(", ", edges.Select(c =>
            $"'{graph.NodesById[c.SourceNodeId].Name}' → '{graph.NodesById[c.TargetNodeId].Name}'"));
}
