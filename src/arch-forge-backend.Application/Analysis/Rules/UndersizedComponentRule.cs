using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 and bloque DT. <strong>The MISSING_CACHE of traffic</strong>: the rule that turns the
/// new axis into advice before the user simulates anything.
///
/// It only has something to say once somebody declares how much traffic comes in the front door
/// (DT.1), and that is deliberate - with no declared load there is no utilisation, so there is
/// nothing to be under-sized for. Every architecture drawn before V4 sees this rule as
/// "not applicable", which is the truth rather than a courtesy.
/// </summary>
public class UndersizedComponentRule : IArchitectureRule
{
    public string Code => RuleCodes.UndersizedComponent;
    public Area Area => Area.Scalability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Ningún componente está cerca de su capacidad con la carga declarada.";
    public string PassedExplanation =>
        "Cada componente aguanta lo que le llega con margen, así que un pico no lo tumba.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            var utilization = load.UtilizationOf(node.Id);
            if (utilization < CapacityModel.SaturationWarning)
                continue;

            // Over capacity it is not "close to the limit", it is already rejecting requests, and
            // the two deserve different colours: one is a risk, the other is an outage in progress.
            var overloaded = utilization > 1m;

            findings.Add(new RuleFinding(
                Code,
                Area,
                overloaded ? Severity.Critical : Severity.Warning,
                overloaded
                    ? $"'{node.Name}' recibe más peticiones de las que aguanta."
                    : $"'{node.Name}' está al límite de su capacidad.",
                $"Le llegan {load.LoadOf(node.Id):0} rps y aguanta {load.CapacityOf(node.Id):0}, un {utilization * 100:0} % de su capacidad. " +
                (overloaded
                    ? "Lo que no cabe se rechaza, y lo que sí cabe espera más."
                    : "Cualquier pico por encima de lo declarado lo tumba."),
                Suggest(graph, node),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    private static string Suggest(ArchitectureGraph graph, Domain.Entities.Node node) => node.Type switch
    {
        Domain.Enums.NodeType.Database =>
            "Pon una caché delante para que no le llegue todo, añade réplicas de lectura, o saca de la petición el trabajo que pueda esperar.",
        Domain.Enums.NodeType.Queue =>
            "Añade consumidores: la cola aguanta, lo que no da abasto es quien la vacía.",
        _ when graph.IncomingByTarget[node.Id].Any(c => c.Synchronous) =>
            "Súbele instancias, o reparte su trabajo entre varios componentes.",
        _ => "Súbele instancias."
    };
}
