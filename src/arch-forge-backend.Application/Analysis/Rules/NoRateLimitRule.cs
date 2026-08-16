using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2, and the rule that finishes paying what V3 D3.5 wrote when it retired the field:
/// <em>"RateLimit is a defence against load, and there is no model of load"</em>. Now there is one,
/// so the field is back and this is what reads it.
///
/// Only applicable once traffic is declared, like <see cref="UndersizedComponentRule"/>: a rate
/// limit protects against a volume, and with no volume in the model there is nothing to protect
/// against and nothing honest to say.
/// </summary>
public class NoRateLimitRule : IArchitectureRule
{
    public string Code => RuleCodes.NoRateLimit;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "La entrada tiene un límite de peticiones.";
    public string PassedExplanation =>
        "El gateway rechaza en la puerta lo que pase del límite, en vez de dejar que se acumule sobre lo que hay detrás.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var gateways = EntryPoints.PublicFacing(graph)
            .Where(n => n.Type == NodeType.Gateway)
            .Where(n => NodeSettings.DeclaredRps(n) > 0)
            .ToList();

        if (gateways.Count == 0)
            return RuleResult.NotApplicable;

        var findings = gateways
            .Where(gateway => NodeSettings.RateLimitOf(gateway) is not > 0)
            .Select(gateway => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                $"'{gateway.Name}' no limita las peticiones que admite.",
                "Todo lo que llegue entra, así que un pico —o alguien empujando a propósito— se propaga entero hacia dentro y lo paga el componente más frágil.",
                "Pon un límite de peticiones acorde a lo que aguanta lo que tienes detrás: lo que sobra se rechaza en la puerta, que es donde es barato.",
                [FindingElement.Node(gateway.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
