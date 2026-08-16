namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2, a debt V2 wrote down and V3 pushed twice. A component nobody calls and that calls
/// nobody is not part of the architecture: it is either a leftover from a draft or the thing you
/// forgot to wire, and both are worth saying out loud before anything else is analysed - every
/// other rule will simply ignore it.
/// </summary>
public class OrphanNodeRule : IArchitectureRule
{
    public string Code => RuleCodes.OrphanNode;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Todos los componentes están conectados a algo.";
    public string PassedExplanation =>
        "Un componente sin llamadas entrantes ni salientes no participa en ningún flujo, así que ninguna otra regla puede decir nada sobre él.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // With a single component there is nothing to be disconnected from: a one-box architecture
        // is unfinished, not wrong, and saying so on the first thing anybody drags in would make
        // the analyzer noise from the very first second.
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable;

        var findings = graph.Nodes
            .Where(node => !graph.OutgoingBySource[node.Id].Any() && !graph.IncomingByTarget[node.Id].Any())
            .OrderBy(node => node.Id)
            .Select(node => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                $"'{node.Name}' no está conectado a nada.",
                "No recibe llamadas ni hace ninguna, así que no forma parte de ningún flujo de la arquitectura.",
                "Conéctalo con lo que deba llamarlo o lo que deba llamar, o bórralo si sobra.",
                [FindingElement.Node(node.Id)])
            {
                // D5.6. Only one of the two ways out is automatable: *deleting* it is unambiguous,
                // *wiring* it is the decision - to what, and in which direction - so the suggestion
                // offers both and the button only does the one that has a single answer.
                Fix = new AppliedFix(
                    FixKinds.RemoveNode,
                    FindingElement.Node(node.Id),
                    $"Borrar '{node.Name}'")
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
