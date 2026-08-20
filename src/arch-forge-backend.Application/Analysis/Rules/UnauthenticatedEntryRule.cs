using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

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

    public LocalizedText PassedMessage => LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.passed.explanation");

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
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.open.message", ("node", node.Name)),
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.open.explanation"),
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.open.suggestion"),
                [FindingElement.Node(node.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
