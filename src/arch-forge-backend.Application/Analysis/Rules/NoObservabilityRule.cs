using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 and D5.3. The rule that makes <see cref="Area.Observability"/> - empty in the contract
/// since V2 - mean something, and the reason the palette had to grow first: <strong>without a
/// component to point at there is no rule, and without a rule there is no area</strong>. It was the
/// longest chain in V4, and it is walked whole or not at all.
///
/// <strong>QA §4: coverage, not a checkbox.</strong> Until scoring v6 this rule asked one yes/no
/// question - is anything watching - and the area could only ever read 100 or 95. One telemetry box
/// wired to one component was "instrumented", and the other six boxes ran blind with a green tick
/// over them. Now the question is asked <em>per component</em>: each one that is yours to watch
/// (<see cref="NodeRoles.IsObservable"/>) and has no edge to a telemetry box, in either direction,
/// is one finding. The arithmetic of D1.1 does the rest - one unobserved component costs 5, the next
/// 2,5 each, capped at 15 - so the area climbs as the wiring does, a diagram with no telemetry at
/// all sits at the bottom of that climb rather than two points above a fully wired one, and
/// "observed" finally means the thing the word means.
///
/// <strong>The code keeps its meaning</strong> (D2.4): it still says "this is not being watched".
/// What changed is the grain, the same way SPOF kept its code when it stopped counting arrows and
/// started reading them (V13 D1.1). The message per component is what makes the grain useful: a
/// card that names the box and lights it up on the canvas is a card you can act on, which a single
/// "nothing is watching" never was once there were nine boxes to choose from.
///
/// Watching the watcher observes nothing (QA B5): an edge between two telemetry boxes counts for
/// neither. And the telemetry's own edges cost nothing anywhere else - HIGH_COUPLING, MISSING_CACHE
/// and QUEUE_NO_CONSUMER read <see cref="NodeRoles.IsTelemetryEdge"/> - because a rule that asks
/// you to wire every component to the telemetry cannot have three other rules charging you for
/// each wire.
///
/// <strong>V15 D2. Two claims, two findings.</strong> V14 B13 was right that "you are not watching
/// this box" and "you cannot see anything" are different claims - and then made the second one by
/// recolouring every instance of the first: on a diagram with no telemetry, each unobserved box
/// became a Critical. That gave the area the biggest hammer in the analyzer (forty-five points, the
/// cap, from one rule) and made the colour of a whole area depend on <em>counting boxes</em>:
/// two observables ran blind as two Warnings, three as three Criticals, so splitting an Api into a
/// pair of replicas turned Observability red without a single wire changing.
///
/// The second claim is now its own finding, said once: <strong>the system is blind</strong> - one
/// Critical, when nothing observable is observed. The per-component findings go back to being what
/// they say, a Warning each, and the arithmetic of D1.1 composes the two the way it composes any
/// rule: the Critical at full weight, every unobserved box at half a Warning, the cap at three
/// Criticals. Total blindness costs 15 + 2,5 per box - 78 for three boxes, 60 for ten, the floor of
/// 55 from twelve up - so a bigger blind system pays more, which is what coverage means, and the
/// colour never depends on how many boxes there are.
///
/// <strong>Blind means nothing is watched, not that the box is missing.</strong> Until v7 the test
/// was "no telemetry component on the canvas", so dragging one in and wiring it to nothing turned
/// every Critical back into a Warning. An observability box nothing reaches observes nothing (QA
/// B5, one step further), and the verdict reads the wiring.
///
/// What stays is the sketch line: below <see cref="RuleThresholds.SyncChainWarning"/> observable
/// components a diagram is not yet an architecture, and the analyzer neither calls it blind here
/// nor labels its score on the client (V15 D1 reads the same threshold). Two boxes with no
/// telemetry are homework; three are a system nobody can see.
/// </summary>
public class NoObservabilityRule : IArchitectureRule
{
    public string Code => RuleCodes.NoObservability;
    public Area Area => Area.Observability;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.NO_OBSERVABILITY.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.NO_OBSERVABILITY.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // A single component is a sketch, not an architecture: asking it for telemetry would be
        // noise on the first thing anybody drags onto the canvas.
        if (graph.Nodes.Count < 2)
            return RuleResult.NotApplicable("rule.na.NO_OBSERVABILITY.singleComponent");

        // A disconnected box is ORPHAN_NODE's (QA B4): asking it to be observed would charge the
        // same leftover twice.
        var observable = graph.Nodes
            .Where(n => NodeRoles.IsObservable(n.Type) && !graph.OrphanNodes.Contains(n.Id))
            .OrderBy(n => n.Id)
            .ToList();

        if (observable.Count == 0)
            return RuleResult.NotApplicable("rule.na.NO_OBSERVABILITY.nothingToWatch");

        var telemetry = graph.Nodes.Where(n => NodeRoles.IsTelemetry(n.Type)).OrderBy(n => n.Id).ToList();

        // D5.6: wiring a box to *the* telemetry has nothing to decide; with two telemetry boxes,
        // which one is the decision, and with none the first step is adding it.
        var theOnlyTelemetry = telemetry.Count == 1 ? telemetry[0] : null;

        var unobserved = observable.Where(node => !IsObserved(graph, node)).ToList();

        // V15 D2. The system is blind when nothing observable is observed - the wiring, not the
        // palette: a telemetry box nothing reaches watches nothing. Said once, as its own Critical,
        // on a diagram big enough to be a system; the per-box findings below stay the Warnings they
        // are, and D1.1 adds the two up (see the class summary for the numbers).
        var blind = unobserved.Count == observable.Count && observable.Count >= RuleThresholds.SyncChainWarning;

        var findings = new List<RuleFinding>();

        if (blind)
            findings.Add(Blind(unobserved, telemetry, theOnlyTelemetry));

        findings.AddRange(unobserved.Select(node => Report(node, telemetry.Count, theOnlyTelemetry)));

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// V15 D2. The one Critical: nothing in the system is observed. It points at every unobserved
    /// component, because that is what it is about - click it and the whole canvas lights up,
    /// which is the picture the sentence paints. No fix: "add a box and wire everything" is not one
    /// local edit, and the per-component cards underneath each carry theirs.
    /// </summary>
    private RuleFinding Blind(IReadOnlyList<Node> unobserved, IReadOnlyList<Node> telemetry, Node? theOnlyTelemetry)
    {
        var suggestion = theOnlyTelemetry is not null
            ? LocalizedText.Of("finding.NO_OBSERVABILITY.blind.suggestion.present", ("telemetry", theOnlyTelemetry.Name))
            : LocalizedText.Of(telemetry.Count == 0
                ? "finding.NO_OBSERVABILITY.blind.suggestion.absent"
                : "finding.NO_OBSERVABILITY.blind.suggestion.several");

        return new RuleFinding(
            Code,
            Area,
            Severity.Critical,
            LocalizedText.Of("finding.NO_OBSERVABILITY.blind.message", ("count", unobserved.Count)),
            LocalizedText.Of("finding.NO_OBSERVABILITY.blind.explanation"),
            suggestion,
            unobserved.Select(node => FindingElement.Node(node.Id)).ToList());
    }

    /// <summary>An edge, either way, between the component and something that is telemetry.</summary>
    private static bool IsObserved(ArchitectureGraph graph, Node node) =>
        graph.IncomingByTarget[node.Id].Select(c => c.SourceNodeId)
            .Concat(graph.OutgoingBySource[node.Id].Select(c => c.TargetNodeId))
            .Any(otherId => graph.NodesById.TryGetValue(otherId, out var other) && NodeRoles.IsTelemetry(other.Type));

    private RuleFinding Report(Node node, int telemetryCount, Node? theOnlyTelemetry)
    {
        // Three whole suggestions, chosen here (V7 D1.1): add telemetry and wire this to it; wire
        // this to the one there is; wire this to one of the several there are.
        var suggestion = theOnlyTelemetry is not null
            ? LocalizedText.Of("finding.NO_OBSERVABILITY.unobserved.suggestion.present",
                ("node", node.Name),
                ("telemetry", theOnlyTelemetry.Name))
            : LocalizedText.Of(telemetryCount == 0
                    ? "finding.NO_OBSERVABILITY.unobserved.suggestion.absent"
                    : "finding.NO_OBSERVABILITY.unobserved.suggestion.several",
                ("node", node.Name));

        return new RuleFinding(
            Code,
            Area,
            // Always the Warning it says it is (V15 D2): "this box is not watched" is homework,
            // and the claim that outranks it has its own finding above.
            Severity.Warning,
            LocalizedText.Of("finding.NO_OBSERVABILITY.unobserved.message", ("node", node.Name)),
            LocalizedText.Of("finding.NO_OBSERVABILITY.unobserved.explanation", ("node", node.Name)),
            suggestion,
            [FindingElement.Node(node.Id)])
        {
            Fix = theOnlyTelemetry is null
                ? null
                : new AppliedFix(
                    FixKinds.ConnectTo,
                    FindingElement.Node(node.Id),
                    LocalizedText.Of("fix.NO_OBSERVABILITY.connect", ("telemetry", theOnlyTelemetry.Name)),
                    TargetNodeId: theOnlyTelemetry.Id)
        };
    }
}
