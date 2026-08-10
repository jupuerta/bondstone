namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 VS1: adapted to the new contract, still on V1's semantics - shared dependency (pattern A)
/// with the "sibling of the same type sharing a dependent" redundancy heuristic. VS3 replaces
/// both: pattern B (single point of passage) and redundancy as an explicit relation through
/// LoadBalancer (D4.x).
/// </summary>
public class SpofRule : IArchitectureRule
{
    private const int SharedDependencyMin = 2;
    private const int CriticalDependents = 3;

    public string Code => RuleCodes.Spof;
    public Area Area => Area.Availability;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Producer;

    public string PassedMessage => "Ningún componente concentra el funcionamiento del sistema.";

    public string PassedExplanation =>
        "Se ha comprobado que ningún componente compartido deja el sistema sin alternativa si deja de funcionar.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var dependentsByTarget = graph.Connections
            .GroupBy(c => c.TargetNodeId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.SourceNodeId).Distinct().ToList());

        // Nothing to check if no node has dependents at all.
        if (dependentsByTarget.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes)
        {
            if (!dependentsByTarget.TryGetValue(node.Id, out var dependents) || dependents.Count < SharedDependencyMin)
                continue;

            var hasRedundantSibling = graph.Nodes.Any(other =>
                other.Id != node.Id &&
                other.Type == node.Type &&
                dependentsByTarget.TryGetValue(other.Id, out var otherDependents) &&
                otherDependents.Intersect(dependents).Any());

            if (hasRedundantSibling)
                continue;

            // Severity by blast radius (D4.x): what the finding costs depends on how much stops
            // working with the node, not on which rule found it.
            var severity = dependents.Count >= CriticalDependents ? Severity.Critical : Severity.Warning;

            // MISSING_RESILIENCE escalates only towards a Critical SPOF, so the fact carries the
            // severity and not just the node (D5.1).
            graph.Facts.Add(new SinglePointOfFailureFact(node.Id, severity));

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                $"'{node.Name}' es un punto único de fallo: {dependents.Count} componentes dependen de él sin redundancia.",
                "No existe ningún componente equivalente que pueda asumir su trabajo. Cualquier incidencia en él afecta a todo lo que hay detrás.",
                "Añade un LoadBalancer con al menos dos instancias equivalentes, o replica el componente para que sus consumidores tengan alternativa.",
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }
}
