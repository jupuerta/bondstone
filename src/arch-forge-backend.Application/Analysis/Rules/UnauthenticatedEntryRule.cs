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
///
/// <strong>V15 E5. And a fourth, which is not a state of the door but the absence of one.</strong>
/// <c>Client → Cdn → Worker</c> left Security <em>without a score</em>: the three rules of the area
/// asked only about doors, and a drawing where nothing could authenticate had none to ask about -
/// so the area dropped out of the average and the panel said nothing, on the one drawing where the
/// question mattered most. The observation is the obvious one, and it is this rule's: the outside
/// reaches something of yours and there is nowhere to check who is calling. A Warning, like the
/// undeclared door: a Worker or a queue facing the users is a shape to fix, not a decision somebody
/// declared. What holds the data is left to <see cref="ExposedDatabaseRule"/>, which has said it
/// under its own name since V4.
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

        // V15 E5. The boxes the outside reaches that are not a door at all - and are not the way
        // to one, not somebody else's, and not the data, which is EXPOSED_DATABASE's sentence.
        // No "data behind" condition here, unlike the doors above: a door with only a cache behind
        // it is a sketch the rule waits on, but a Worker facing the users cannot ask whoever comes,
        // has no field to declare anything with, and never will - the finding is the shape.
        var unguarded = EntryPoints.Unguarded(graph);

        if (exposed.Count == 0 && unguarded.Count == 0)
            return RuleResult.NotApplicable("rule.na.UNAUTHENTICATED_ENTRY.nothingExposed");

        var findings = new List<RuleFinding>();

        foreach (var node in exposed)
        {
            // QA §3.4. A door wired to an IdentityProvider has answered the question this rule
            // asks: delegation is a declaration, made in the drawing instead of in a checkbox,
            // and the drawing is the stronger claim - it is the one every other rule reads.
            var authentication = NodeSettings.AuthenticationOf(node);
            var delegation = DelegationOf(graph, node);

            // V14 B15. Unless the checkbox says the opposite. "The drawing is the stronger claim"
            // settles silence against a wire; it does not settle two explicit declarations that
            // contradict each other, and choosing one of them quietly is the analyzer making the
            // user's decision for them. A Warning, not the Critical of a declared "no": the wire
            // is there, so the data is not open in the way an undelegated "no" leaves it - what is
            // wrong is that the diagram says two things, and the card says which two.
            if (authentication == false && delegation is { } contradicted)
            {
                var provider = graph.NodesById[contradicted.TargetNodeId];

                findings.Add(new RuleFinding(
                    Code,
                    Area,
                    Severity.Warning,
                    LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.contradicted.message",
                        ("node", node.Name),
                        ("provider", provider.Name)),
                    LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.contradicted.explanation",
                        ("node", node.Name),
                        ("provider", provider.Name)),
                    LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.contradicted.suggestion",
                        ("node", node.Name),
                        ("provider", provider.Name)),
                    [FindingElement.Node(node.Id), FindingElement.Connection(contradicted.Id)]));

                continue;
            }

            if (authentication == true || delegation is not null)
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

        // V15 E5. The name travels in all three texts: the explanation says what the box is not
        // (an API, a gateway, a service) and the suggestion says what to put in front of it.
        foreach (var node in unguarded)
        {
            findings.Add(new RuleFinding(
                Code,
                Area,
                Severity.Warning,
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.unguarded.message", ("node", node.Name)),
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.unguarded.explanation", ("node", node.Name)),
                LocalizedText.Of("finding.UNAUTHENTICATED_ENTRY.unguarded.suggestion", ("node", node.Name)),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// QA §3.4: the door hands the "who are you" question to an IdentityProvider it calls. The
    /// edge itself, so a contradiction (V14 B15) can point at the wire it is about; the first one
    /// in the graph's order when a door is wired to more than one provider.
    /// </summary>
    private static Domain.Entities.Connection? DelegationOf(ArchitectureGraph graph, Domain.Entities.Node door) =>
        graph.OutgoingBySource[door.Id].FirstOrDefault(edge =>
            graph.NodesById.TryGetValue(edge.TargetNodeId, out var target)
            && target.Type == NodeType.IdentityProvider);
}
