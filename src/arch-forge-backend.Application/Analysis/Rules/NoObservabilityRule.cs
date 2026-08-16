using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 and D5.3. The rule that makes <see cref="Area.Observability"/> - empty in the contract
/// since V2 - mean something, and the reason the palette had to grow first: <strong>without a
/// component to point at there is no rule, and without a rule there is no area</strong>. It was the
/// longest chain in V4, and it is walked whole or not at all.
///
/// It asks the only question the model can answer honestly: is anything watching. Not what it
/// collects, not how - the model has no notion of either.
/// </summary>
public class NoObservabilityRule : IArchitectureRule
{
    public string Code => RuleCodes.NoObservability;
    public Area Area => Area.Observability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "La arquitectura está instrumentada.";
    public string PassedExplanation =>
        "Hay un componente de observabilidad conectado, así que cuando algo falle habrá dónde mirar.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A single component is a sketch, not an architecture: asking it for telemetry would be
        // noise on the first thing anybody drags onto the canvas.
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable;

        var connected = graph.Nodes
            .Where(n => n.Type == NodeType.Observability)
            .Where(n => graph.IncomingByTarget[n.Id].Any() || graph.OutgoingBySource[n.Id].Any())
            .ToList();

        if (connected.Count > 0)
            return RuleResult.Applicable([]);

        var hasOrphan = graph.Nodes.Any(n => n.Type == NodeType.Observability);

        return RuleResult.Applicable(new RuleFinding(
            Code,
            Area,
            DefaultSeverity,
            hasOrphan
                ? "El componente de observabilidad no está conectado a nada."
                : "No hay nada observando la arquitectura.",
            "Cuando algo falle no habrá dónde mirar: ni qué componente empezó, ni cuánto tardaba, ni cuántas peticiones se perdieron. El fallo se investiga con lo que se recuerde.",
            "Añade un componente de observabilidad y conéctale lo que quieras poder mirar cuando algo vaya mal.",
            connected.Concat(graph.Nodes.Where(n => n.Type == NodeType.Observability))
                .Select(n => FindingElement.Node(n.Id))
                .ToList()));
    }
}
