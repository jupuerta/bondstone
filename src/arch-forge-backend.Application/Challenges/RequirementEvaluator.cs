using arch_forge_backend.Application.Analysis;
using arch_forge_backend.Application.Configuration.Schemas;
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
            RequirementKinds.NoFinding => NoFinding(requirement, context),
            RequirementKinds.HasFinding => HasFinding(requirement, context),
            RequirementKinds.NoSeverity => NoSeverity(requirement, context),
            RequirementKinds.UnderScenario => UnderScenario(requirement, context),

            // Unreachable: the catalogue refuses to load an unknown kind (D1.5). Answering "not
            // met" rather than throwing keeps one malformed requirement from taking down the
            // evaluation of the other four.
            _ => ChallengeRequirementResult.Fail(requirement, $"'{requirement.Kind}' no es un requisito reconocido.")
        };

    // ---- Estructural -------------------------------------------------------

    private static ChallengeRequirementResult ContainsNode(ChallengeRequirement requirement, EvaluationContext context)
    {
        var min = requirement.Min ?? 1;
        var found = context.Graph.Nodes.Where(n => n.Type == requirement.NodeType).ToList();

        if (found.Count >= min)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(requirement, min == 1
            ? $"Tu arquitectura no tiene ningún componente de tipo {requirement.NodeType}."
            : $"Tienes {found.Count} de tipo {requirement.NodeType} y hacen falta {min}.");
    }

    private static ChallengeRequirementResult AbsentNode(ChallengeRequirement requirement, EvaluationContext context)
    {
        var found = context.Graph.Nodes.Where(n => n.Type == requirement.NodeType).ToList();

        if (found.Count == 0)
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            $"Sigue habiendo {Count(found.Count, "componente", "componentes")} de tipo {requirement.NodeType}: {Names(found)}.",
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
            $"'{worst.Node.Name}' llama directamente a {worst.FanOut} componentes; el límite de este reto es {max}.",
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

        var mechanism = MechanismName(requirement.Mechanism!);
        var detail = offenders.Count == edges.Count
            ? $"Ninguna de las {edges.Count} llamadas a un {requirement.NodeType} tiene {mechanism}."
            : $"{offenders.Count} de las {edges.Count} llamadas a un {requirement.NodeType} no tienen {mechanism}: {Calls(offenders, context.Graph)}.";

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
            $"No hay ningún camino desde un {requirement.FromType} hasta un {requirement.ToType}.");
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
                ? $"Ningún punto de entrada declara tráfico, y el reto se plantea sobre {requirement.Min} rps."
                : $"Los puntos de entrada declaran {declared:0} rps y el reto se plantea sobre {requirement.Min}.",
            context.Graph.RootNodes.Select(n => FindingElement.Node(n.Id)).ToList());
    }

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
            found.Count == 1 ? found[0].Message : $"{found.Count} avisos siguen abiertos. {found[0].Message}",
            found.SelectMany(f => f.Elements).Distinct().ToList());
    }

    private static ChallengeRequirementResult HasFinding(ChallengeRequirement requirement, EvaluationContext context) =>
        Problems(context, requirement.Code!).Count > 0
            ? ChallengeRequirementResult.Pass(requirement)
            : ChallengeRequirementResult.Fail(requirement, "El analizador no detecta esa situación en tu arquitectura.");

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
            $"Quedan {Count(found.Count, "aviso", "avisos")} de nivel {requirement.Severity} o superior. {found[0].Message}",
            found.SelectMany(f => f.Elements).Distinct().ToList());
    }

    // ---- Simulada ----------------------------------------------------------

    private static ChallengeRequirementResult UnderScenario(ChallengeRequirement requirement, EvaluationContext context)
    {
        if (!context.TryResolve(requirement.Faults!, out var scenario, out var missing))
            return ChallengeRequirementResult.Fail(
                requirement,
                $"No hay ningún componente llamado '{missing}' en tu arquitectura, así que este escenario no se puede plantear.");

        var simulation = context.Simulate(scenario);

        // The engine drops faults pointing at elements that are gone, on purpose: a link shared
        // before the architecture was edited has to keep opening (V3 D3.6). For a requirement that
        // rule is backwards - a scenario that lost its fault runs over a healthy architecture and
        // <em>passes</em>, so "it survives Payments going down" would be met by an architecture
        // that never posed the question. Anything discarded fails, loudly.
        if (simulation.DiscardedFaults.Count > 0)
            return ChallengeRequirementResult.Fail(
                requirement,
                "Este escenario no se ha podido plantear sobre tu arquitectura: alguno de los componentes que menciona ya no está.");

        var actual = Read(simulation.Metrics, requirement.Metric!);

        if (actual is null)
            return ChallengeRequirementResult.Fail(
                requirement,
                "No queda ningún punto de entrada en pie, así que no hay latencia que medir.",
                Affected(simulation));

        if (RequirementOperators.Compare(actual.Value, requirement.Operator!, requirement.Value!.Value))
            return ChallengeRequirementResult.Pass(requirement);

        return ChallengeRequirementResult.Fail(
            requirement,
            $"{MetricName(requirement.Metric!)}: {Format(requirement.Metric!, actual.Value)}, " +
            $"y el reto pide {RequirementOperators.Describe(requirement.Operator!)} {Format(requirement.Metric!, requirement.Value!.Value)}.",
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

    private static string MechanismName(string mechanism) => mechanism switch
    {
        RequirementMechanisms.Timeout => "timeout",
        RequirementMechanisms.Retries => "reintentos",
        RequirementMechanisms.CircuitBreaker => "circuit breaker",
        RequirementMechanisms.Fallback => "respuesta alternativa",
        _ => mechanism
    };

    private static string MetricName(string metric) => metric switch
    {
        RequirementMetrics.ComponentsDown => "Componentes caídos",
        RequirementMetrics.ComponentsDegraded => "Componentes degradados",
        RequirementMetrics.BlastRadius => "Alcance del fallo",
        RequirementMetrics.EntryPointsAffected => "Puntos de entrada afectados",
        RequirementMetrics.NominalLatencyMs => "Latencia sin fallos",
        RequirementMetrics.FaultedLatencyMs => "Latencia bajo el fallo",
        RequirementMetrics.RequestAmplification => "Amplificación de peticiones",
        RequirementMetrics.RequestsAffected => "Peticiones perdidas",
        RequirementMetrics.BacklogRps => "Trabajo acumulándose en las colas",
        RequirementMetrics.LoadAmplification => "Amplificación de carga",
        _ => metric
    };

    private static string Format(string metric, decimal value) => metric switch
    {
        RequirementMetrics.BlastRadius or RequirementMetrics.RequestsAffected => $"{value * 100:0.#} %",
        RequirementMetrics.NominalLatencyMs or RequirementMetrics.FaultedLatencyMs => $"{value:0} ms",
        RequirementMetrics.RequestAmplification => $"×{value:0.##}",
        _ => $"{value:0.##}"
    };

    private static string Count(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    private static string Names(IEnumerable<Node> nodes) =>
        string.Join(", ", nodes.Select(n => $"'{n.Name}'"));

    private static string Calls(IEnumerable<Connection> edges, ArchitectureGraph graph) =>
        string.Join(", ", edges.Select(c =>
            $"'{graph.NodesById[c.SourceNodeId].Name}' → '{graph.NodesById[c.TargetNodeId].Name}'"));
}
