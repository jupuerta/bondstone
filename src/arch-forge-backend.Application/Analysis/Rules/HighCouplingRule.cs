namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.1. Fan-out, not fan-in: that many components depend on a node is already covered by SPOF
/// from Availability, and counting it again as coupling would penalize the same fact twice.
///
/// The threshold is fixed and not relative to the size of the graph. A relative one ("twice the
/// average") always flags somebody, even in a healthy architecture, and cannot be explained on the
/// finding's card. A fixed one can be wrong, but it can be understood and argued with.
/// </summary>
public class HighCouplingRule : IArchitectureRule
{
    public string Code => RuleCodes.HighCoupling;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Ningún componente acumula demasiadas dependencias.";

    public string PassedExplanation =>
        "Se ha contado de cuántos componentes depende cada uno y ninguno necesita tantas piezas como para ser difícil de desplegar o probar por separado.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var coupled = graph.Nodes
            .Select(node => (Node: node, Edges: graph.OutgoingBySource[node.Id].ToList()))
            .Where(entry => entry.Edges.Count > 0)
            .ToList();

        if (coupled.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var (node, edges) in coupled)
        {
            // Distinct components, not edges: depending twice on the same thing is one dependency.
            var dependencies = edges.Select(e => e.TargetNodeId).Distinct().Count();
            if (dependencies < RuleThresholds.CouplingWarning)
                continue;

            var severity = dependencies >= RuleThresholds.CouplingCritical ? Severity.Critical : Severity.Warning;

            // One finding per coupled node with all its edges inside, never one per dependency
            // (D4.5, granularity).
            List<FindingElement> elements = [FindingElement.Node(node.Id)];
            elements.AddRange(edges.OrderBy(e => e.Id).Select(e => FindingElement.Connection(e.Id)));

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                $"'{node.Name}' depende de {dependencies} componentes distintos.",
                "Un componente que necesita tantas piezas para funcionar es difícil de desplegar, probar y razonar de forma aislada, y su disponibilidad es el producto de la de todos ellos.",
                "Revisa si algunas de esas responsabilidades pertenecen al propio componente, o si parte de las llamadas puede pasar a eventos asíncronos.",
                elements));
        }

        return RuleResult.Applicable(findings);
    }
}
