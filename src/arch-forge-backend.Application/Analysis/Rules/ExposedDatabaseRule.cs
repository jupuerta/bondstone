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
///
/// <strong>V15 C1. Except the bucket an edge cache serves.</strong> <c>Users → Cdn → Assets</c> is
/// the static site - the cleanest drawing in the palette, and the one <c>IMPLAUSIBLE_DEPENDENCY</c>
/// blesses on purpose ("the bucket it serves") - and this rule was calling it Critical with advice
/// to put an API in front, which is the one thing nobody does to a static site. B3's lesson was
/// never "a bucket is a database": a public bucket is the <em>function</em> of half the buckets
/// anybody draws. What tells the served bucket from the open one is the CDN in front of it, not the
/// type, so that is what the rule reads.
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
            return RuleResult.NotApplicable("rule.na.EXPOSED_DATABASE.noStore");

        // The frontier is everything traffic reaches before meeting a door. A data store in there
        // is data anybody can reach.
        var frontier = EntryPoints.Frontier(graph).Select(n => n.Id).ToHashSet();

        // V15 C1. Unless it is a bucket the outside only ever reaches through an edge cache: the
        // same walk with the CDN as a second stop says whether there is some other way in, and a
        // bucket with none is the bucket the CDN serves, not the public bucket of V13 B3. Every
        // path, not one of them - a direct arrow beside the CDN is still the open bucket - and
        // buckets only: a Database behind a CDN keeps its Critical, because an edge cache serves
        // files and not rows, and that arrow is already the one Critical IMPLAUSIBLE_DEPENDENCY
        // asserts has no innocent reading.
        var bare = EntryPoints.Frontier(graph, type => NodeRoles.CanAuthenticate(type) || type == NodeType.Cdn)
            .Select(n => n.Id)
            .ToHashSet();

        var findings = stores
            .Where(store => store.Type == NodeType.ObjectStorage ? bare.Contains(store.Id) : frontier.Contains(store.Id))
            .OrderBy(store => store.Id)
            .Select(store => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.message", ("node", store.Name)),
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.explanation"),
                LocalizedText.Of("finding.EXPOSED_DATABASE.reachable.suggestion"),
                [FindingElement.Node(store.Id)]))
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
