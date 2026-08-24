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
            return RuleResult.NotApplicable;

        // A disconnected box is ORPHAN_NODE's (QA B4): asking it to be observed would charge the
        // same leftover twice.
        var observable = graph.Nodes
            .Where(n => NodeRoles.IsObservable(n.Type) && !graph.OrphanNodes.Contains(n.Id))
            .OrderBy(n => n.Id)
            .ToList();

        if (observable.Count == 0)
            return RuleResult.NotApplicable;

        var telemetry = graph.Nodes.Where(n => NodeRoles.IsTelemetry(n.Type)).OrderBy(n => n.Id).ToList();

        // D5.6: wiring a box to *the* telemetry has nothing to decide; with two telemetry boxes,
        // which one is the decision, and with none the first step is adding it.
        var theOnlyTelemetry = telemetry.Count == 1 ? telemetry[0] : null;

        // V14 B13. "You are not watching this box" and "you cannot see anything" are different
        // claims, and until v7 they carried the same colour - which, with one Warning-only rule in
        // the area, put a floor of 85 under it: a twenty-box system with not one line of telemetry
        // read "aceptable", two and a half global points below a fully instrumented one. On a
        // diagram big enough to be an architecture (the SYNC_CHAIN warning threshold, which is
        // also where "sketch" stops being the right word) with no telemetry box at all, each
        // unobserved component is Critical: the cap does the rest, and total blindness now sits at
        // 55, forty points under the fully wired 100 instead of fifteen.
        var blind = telemetry.Count == 0 && observable.Count >= RuleThresholds.SyncChainWarning;

        var findings = observable
            .Where(node => !IsObserved(graph, node))
            .Select(node => Report(node, telemetry.Count, theOnlyTelemetry, blind ? Severity.Critical : Severity.Warning))
            .ToList();

        return RuleResult.Applicable(findings);
    }

    /// <summary>An edge, either way, between the component and something that is telemetry.</summary>
    private static bool IsObserved(ArchitectureGraph graph, Node node) =>
        graph.IncomingByTarget[node.Id].Select(c => c.SourceNodeId)
            .Concat(graph.OutgoingBySource[node.Id].Select(c => c.TargetNodeId))
            .Any(otherId => graph.NodesById.TryGetValue(otherId, out var other) && NodeRoles.IsTelemetry(other.Type));

    private RuleFinding Report(Node node, int telemetryCount, Node? theOnlyTelemetry, Severity severity)
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
            severity,
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
