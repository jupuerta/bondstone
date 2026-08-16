using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2, one of the three that make <see cref="Area.Security"/> stop being an area that travels
/// in the contract with a dash on it since V2.
///
/// <strong>It only fires when there is data behind the door.</strong> An Api with a cache behind it
/// is a sketch; an Api with a database behind it is a way into somebody's data, and that is a
/// different sentence. Without that condition every new diagram would open with a red critical
/// before the user had finished drawing it - the "todo rojo" this project has spent three versions
/// avoiding.
/// </summary>
public class UnauthenticatedEntryRule : IArchitectureRule
{
    public string Code => RuleCodes.UnauthenticatedEntry;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Las puertas de entrada piden autenticación.";
    public string PassedExplanation =>
        "Quien llega desde fuera tiene que identificarse antes de que la petición alcance nada que guarde datos.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var exposed = EntryPoints.PublicFacing(graph)
            .Where(node => EntryPoints.Reaches(graph, node.Id, NodeType.Database))
            .ToList();

        if (exposed.Count == 0)
            return RuleResult.NotApplicable;

        var findings = exposed
            .Where(node => !NodeSettings.HasAuthentication(node))
            .Select(node => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                $"'{node.Name}' está expuesto sin autenticación.",
                "Es una puerta de entrada desde fuera y detrás hay una base de datos, así que cualquiera que conozca la dirección llega a los datos.",
                "Activa la autenticación en el punto de entrada, o pon delante un gateway que la haga.",
                [FindingElement.Node(node.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
