using arch_forge_backend.Application.Simulation;

using arch_forge_backend.Application.Localization;

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

    public LocalizedText PassedMessage => LocalizedText.Of("finding.UNDERSIZED_COMPONENT.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.UNDERSIZED_COMPONENT.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var load = LoadModel.Of(graph);
        if (load.IsIdle)
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            // A disconnected box serves no flow, whatever RPS its configuration declares: sizing
            // it is ORPHAN_NODE's problem to raise, not this rule's (QA B4).
            if (graph.OrphanNodes.Contains(node.Id))
                continue;

            // V8 §6.2. Somebody else's service is left out on purpose. Every sentence this rule
            // knows how to say - add instances, add replicas, share the work - is an instruction
            // to do something you cannot do here, and a finding whose suggestion is impossible is
            // worse than no finding. What there *is* to say about an external call under load is
            // said by EXTERNAL_NO_FALLBACK, on the call rather than on the box.
            //
            // V14 B2. And that argument was never about one type. "Give your users more
            // instances", "scale the CDN", "add a replica of the cron" are the same impossible
            // sentence, and the enormous capacity CapacityModel hands those boxes so they never
            // saturate was no guard against a declared load of two million. The exclusion is a
            // predicate now, and it names all five.
            if (!NodeRoles.IsYoursToSize(node.Type))
                continue;

            var utilization = load.UtilizationOf(node.Id);
            if (utilization < CapacityModel.SaturationWarning)
                continue;

            // Over capacity it is not "close to the limit", it is already failing requests, and
            // the two deserve different colours: one is a risk, the other is an outage in progress.
            var overloaded = utilization > 1m;

            findings.Add(new RuleFinding(
                Code,
                Area,
                overloaded ? Severity.Critical : Severity.Warning,
                LocalizedText.Of(overloaded
                        ? "finding.UNDERSIZED_COMPONENT.overloaded.message"
                        : "finding.UNDERSIZED_COMPONENT.atCapacity.message",
                    ("node", node.Name)),

                // Whole sentences, not a shared prefix plus a tail. Concatenating fragments is the
                // localization bug that survives review: the join reads fine in the language it
                // was written in and falls apart in the next one, where the clause order moves
                // (V7 D1.1). The numbers travel raw and the catalogue formats them.
                LocalizedText.Of(Explanation(graph, node, overloaded),
                    ("loadRps", load.LoadOf(node.Id)),
                    ("capacityRps", load.CapacityOf(node.Id)),
                    ("utilization", utilization)),

                Suggest(graph, node),
                [FindingElement.Node(node.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// QA B9. The overloaded sentence used to say "what does not fit is rejected", and the model
    /// does not reject: a saturated component passes its whole load on to what it calls (see
    /// <see cref="LoadModel"/>, "saturation sheds nothing"). So a component that has dependencies
    /// gets the sentence that says so - the database behind an Api at 200 % is about to show up
    /// in this same list, and the card should be the one to explain why - and a component with
    /// nothing behind it gets the one without the clause it would have no use for. The telemetry
    /// edge does not count: what reaches an Observability node is not the traffic the lesson is
    /// about.
    /// </summary>
    private static string Explanation(ArchitectureGraph graph, Domain.Entities.Node node, bool overloaded)
    {
        if (!overloaded)
            return "finding.UNDERSIZED_COMPONENT.atCapacity.explanation";

        var hasDependencies = graph.OutgoingBySource[node.Id]
            .Any(edge => graph.NodesById[edge.TargetNodeId].Type != Domain.Enums.NodeType.Observability);

        return hasDependencies
            ? "finding.UNDERSIZED_COMPONENT.overloaded.explanation.withDependencies"
            : "finding.UNDERSIZED_COMPONENT.overloaded.explanation";
    }

    private static LocalizedText Suggest(ArchitectureGraph graph, Domain.Entities.Node node) => LocalizedText.Of(node.Type switch
    {
        Domain.Enums.NodeType.Database => "finding.UNDERSIZED_COMPONENT.suggestion.database",
        Domain.Enums.NodeType.Queue => "finding.UNDERSIZED_COMPONENT.suggestion.queue",
        _ when graph.IncomingByTarget[node.Id].Any(c => c.Synchronous) => "finding.UNDERSIZED_COMPONENT.suggestion.shareWork",
        _ => "finding.UNDERSIZED_COMPONENT.suggestion.addInstances"
    });
}
