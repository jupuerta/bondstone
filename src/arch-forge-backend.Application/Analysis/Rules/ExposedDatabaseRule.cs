using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2. A data store traffic reaches without passing through anything that could stop it.
///
/// This is the structural half of the Security area, and the reason it is worth having next to
/// <see cref="UnauthenticatedEntryRule"/>: that one asks whether the door is locked, this one asks
/// whether there is a door at all. A database hanging straight off a balancer, or drawn as an entry
/// point, is not a configuration mistake - it is a shape mistake, and no field can fix it.
///
/// QA B3. It reads <see cref="NodeRoles.IsSystemOfRecord"/> rather than <c>NodeType.Database</c>:
/// the public bucket is the most famous shape this mistake takes, and until v6 an ObjectStorage on
/// the frontier left Security without so much as a score. The code keeps its name - it is the same
/// lesson, whose original name was merely narrower than the concept - because a code is never
/// reused to mean something else (V2 D2.4), and this is not something else.
/// </summary>
public class ExposedDatabaseRule : IArchitectureRule
{
    public string Code => RuleCodes.ExposedDatabase;
    public Area Area => Area.Security;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.EXPOSED_DATABASE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.EXPOSED_DATABASE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A disconnected data store is a leftover, not a door (QA B4): no drawn traffic reaches
        // it, so "anybody can reach it" is a sentence about edges it does not have. Without this,
        // one lonely Database dragged onto the canvas opened as a Critical - the exact
        // first-screen red this product exists to avoid.
        var stores = graph.Nodes
            .Where(n => NodeRoles.IsSystemOfRecord(n.Type) && !graph.OrphanNodes.Contains(n.Id))
            .ToList();

        if (stores.Count == 0)
            return RuleResult.NotApplicable;

        // The frontier is everything traffic reaches before meeting an Api or a Gateway. A data
        // store in there is data anybody can reach.
        var frontier = EntryPoints.Frontier(graph).Select(n => n.Id).ToHashSet();

        var findings = stores
            .Where(db => frontier.Contains(db.Id))
            .OrderBy(db => db.Id)
            .Select(db => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.message", ("node", db.Name)),
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.explanation"),
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.suggestion"),
                [FindingElement.Node(db.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
