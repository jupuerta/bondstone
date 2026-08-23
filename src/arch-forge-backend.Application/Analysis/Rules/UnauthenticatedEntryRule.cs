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
///
/// V13 D2.1. That condition was not enough, and the measurement said so: <strong>the rule fired on
/// all four reference architectures, in Critical, <c>minimal-api-db</c> included</strong> - two
/// boxes and an arrow. The reason was one letter of the schema. With <c>Authentication</c> as a
/// <see cref="bool"/> there was no way to tell an undeclared field from a declared "no", so the rule
/// was reading silence as a decision and shouting about it.
///
/// The rule to compare it with is <see cref="NoRateLimitRule"/>, which has been honest since V4: no
/// declared RPS, no opinion. This one now answers the three states separately.
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
        // "Data behind the door" means any system of record (QA B3): the files in an object
        // storage are as much somebody's data as the rows in a database, and a door that reaches
        // either one is the same question.
        var exposed = EntryPoints.PublicFacing(graph)
            .Where(node => EntryPoints.Reaches(graph, node.Id, NodeRoles.IsSystemOfRecord))
            .ToList();

        if (exposed.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var node in exposed)
        {
            // QA §3.4. A door wired to an IdentityProvider has answered the question this rule
            // asks: delegation is a declaration, made in the drawing instead of in a checkbox,
            // and the drawing is the stronger claim - it is the one every other rule reads.
            var authentication = NodeSettings.AuthenticationOf(node);
            if (authentication == true || DelegatesAuthentication(graph, node))
                continue;

            // V13 D2.1. Declared "no" is a decision about a door with a database behind it, and it
            // keeps the Critical it has had since V4. Undeclared is a gap in the drawing: worth
            // saying, worth a different colour, and not worth fifteen points of an area on a
            // diagram somebody started two minutes ago.
            var declared = authentication is not null;
            var variant = declared ? "open" : "undeclared";

            findings.Add(new RuleFinding(
                Code,
                Area,
                declared ? Severity.Critical : Severity.Warning,
                LocalizedText.Of($"finding.UNAUTHENTICATED_ENTRY.{variant}.message", ("node", node.Name)),

                // Two whole sentences and not one with a clause bolted on. The two cases say
                // different things - "anyone who knows the address reaches the data" versus "nobody
                // has said whether this door asks" - and which of them applies is the rule's
                // decision rather than the translator's (V7 D1.1).
                LocalizedText.Of($"finding.UNAUTHENTICATED_ENTRY.{variant}.explanation"),
                LocalizedText.Of($"finding.UNAUTHENTICATED_ENTRY.{variant}.suggestion"),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>QA §3.4: the door hands the "who are you" question to an IdentityProvider it calls.</summary>
    private static bool DelegatesAuthentication(ArchitectureGraph graph, Domain.Entities.Node door) =>
        graph.OutgoingBySource[door.Id].Any(edge =>
            graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
            && target.Type == NodeType.IdentityProvider);
}
