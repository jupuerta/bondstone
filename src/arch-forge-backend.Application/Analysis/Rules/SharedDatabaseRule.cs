using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// QA §4. <strong>Integration by database</strong>, the coupling anti-pattern this analyzer could
/// see and never named: two services meeting on one schema. Until this rule the shape surfaced only
/// sideways - as MISSING_CACHE, which talks about reads, or as CYCLE's <c>sharedData</c> advice,
/// which only appears when the services also happen to call each other in a circle.
///
/// <strong>This is not fan-in counted twice.</strong> HIGH_COUPLING's D4.1 says that "many depend
/// on it" is SPOF's measurement and Coupling must not charge for it again, and that still holds: SPOF
/// counts hard dependents and grades by what falls. This rule counts nothing that falls. Two
/// services on one database are coupled with every edge protected by a fallback and with the
/// database replicated three times, because what couples them is not the call - it is the schema.
/// Each can be deployed alone; neither can change its tables alone. The measurement is <em>which
/// kind of component meets on which kind of node</em>, and the only number in it is "more than one".
///
/// <strong>Only <see cref="NodeType.Service"/> counts, on purpose.</strong> The model has no notion
/// of reads versus writes, so the rule cannot say "shared by write" as the QA note did; what it can
/// say is "shared by two owners of business logic". An Api and a Worker on one database are the
/// ordinary shape of a single application with a background job, and an Api beside a Service is a
/// front door that also reaches into a store - plausible shapes this rule has no way to tell from
/// the mistake. Two Services are two bounded contexts, or should be. The conservative reading is
/// the one that never fires on a diagram that is right, which is the bar D4.3 set.
///
/// A Warning with no escalation, for MISSING_CACHE's reason: it is a design smell, never an outage,
/// and a Critical here would devalue the real ones.
/// </summary>
public class SharedDatabaseRule : IArchitectureRule
{
    public string Code => RuleCodes.SharedDatabase;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.SHARED_DATABASE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.SHARED_DATABASE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A store nobody calls is ORPHAN_NODE's box, not a schema anybody shares. Object storage
        // counts alongside the database (QA B3): a bucket two services both read and write is
        // integration by files, which is the same lesson with a different interface.
        var stores = graph.Nodes
            .Where(n => NodeRoles.IsSystemOfRecord(n.Type) && graph.IncomingByTarget[n.Id].Any())
            .ToList();

        if (stores.Count == 0)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var store in stores.OrderBy(s => s.Id))
        {
            // Direct edges only: a service reaching the database through a cache is still on the
            // schema, but the cache is where the model already says it is; keeping the walk to one
            // hop keeps the card's list equal to the arrows the user can see.
            var sharing = graph.IncomingByTarget[store.Id]
                .Where(edge => graph.NodesById.GetValueOrDefault(edge.SourceNodeId)?.Type == NodeType.Service)
                .GroupBy(edge => edge.SourceNodeId)
                .Select(group => (Service: graph.NodesById[group.Key], Edge: group.OrderBy(e => e.Id).First()))
                .OrderBy(pair => pair.Service.Id)
                .ToList();

            if (sharing.Count < RuleThresholds.SharedDatabaseMinServices)
                continue;

            // The store, then one edge per service: what the canvas lights up is the meeting point
            // and every arrow into it, which is the shape the card describes.
            List<FindingElement> elements = [FindingElement.Node(store.Id)];
            elements.AddRange(sharing.Select(pair => FindingElement.Connection(pair.Edge.Id)));

            // V14 §4.2. The sentence this rule always wanted to say is "two services *writing*
            // one schema" - integration by database in its full form - and until the edge could
            // carry the claim it said the conservative one. It still does by default: the strong
            // variant needs the writes *declared* (access "write" or an explicit "both"), because
            // two undeclared edges defaulting to both is the model's guess, not the user's claim.
            var writers = sharing.Count(pair => graph.ResilienceOf(pair.Edge).DeclaresWrite);
            var variant = writers >= RuleThresholds.SharedDatabaseMinServices ? "writers" : "shared";

            // D5.6: no fix. Which service owns which table is the whole of the decision.
            findings.Add(new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of($"finding.SHARED_DATABASE.{variant}.message",
                    ("node", store.Name),
                    ("count", variant == "writers" ? writers : sharing.Count),
                    ("names", Names(sharing.Where(pair => variant == "shared" || graph.ResilienceOf(pair.Edge).DeclaresWrite).Select(pair => pair.Service)))),
                LocalizedText.Of($"finding.SHARED_DATABASE.{variant}.explanation", ("node", store.Name)),
                LocalizedText.Of($"finding.SHARED_DATABASE.{variant}.suggestion"),
                elements));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// Quoted and comma-separated, the way CYCLE joins its walk: a list of names is the one piece of
    /// text that reads the same in every language, so it is composed here rather than asking the
    /// catalogue for a plural form per length.
    /// </summary>
    private static string Names(IEnumerable<Node> services) =>
        string.Join(", ", services.Select(service => $"'{service.Name}'"));
}
