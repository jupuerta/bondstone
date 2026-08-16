using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2. A database traffic reaches without passing through anything that could stop it.
///
/// This is the structural half of the Security area, and the reason it is worth having next to
/// <see cref="UnauthenticatedEntryRule"/>: that one asks whether the door is locked, this one asks
/// whether there is a door at all. A database hanging straight off a balancer, or drawn as an entry
/// point, is not a configuration mistake - it is a shape mistake, and no field can fix it.
/// </summary>
public class ExposedDatabaseRule : IArchitectureRule
{
    public string Code => RuleCodes.ExposedDatabase;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Las bases de datos están detrás de algo.";
    public string PassedExplanation =>
        "Para llegar a los datos hay que pasar antes por una API o un gateway, que es donde se puede comprobar quién llama.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var databases = graph.Nodes.Where(n => n.Type == NodeType.Database).ToList();
        if (databases.Count == 0)
            return RuleResult.NotApplicable;

        // The frontier is everything traffic reaches before meeting an Api or a Gateway. A database
        // in there is a database anybody can reach.
        var frontier = EntryPoints.Frontier(graph).Select(n => n.Id).ToHashSet();

        var findings = databases
            .Where(db => frontier.Contains(db.Id))
            .OrderBy(db => db.Id)
            .Select(db => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                $"Se llega a '{db.Name}' sin pasar por ninguna API.",
                "El tráfico de fuera alcanza la base de datos directamente, así que no hay ningún sitio donde comprobar quién llama ni qué pide.",
                "Pon una API o un gateway delante, y deja que solo él hable con la base de datos.",
                [FindingElement.Node(db.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
