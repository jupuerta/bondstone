using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// QA §4. <strong>The asynchronous twin of <see cref="OrphanNodeRule"/>.</strong> A queue somebody
/// publishes to and nobody drains is a flow with no end: the messages go in, the request is
/// answered, and the work they stood for is never done. Until this rule the analyzer said nothing -
/// <c>Api → Queue</c> scored the same as <c>Api → Queue → Worker</c>, which is the product
/// recommending the first half of a pattern and not noticing when the second half is missing.
///
/// <strong>What a consumer is, here, is the model's answer and not a guess.</strong> An edge that
/// leaves a queue is the broker delivering to whoever processes the message - it is what
/// <see cref="Simulation.LoadModel"/> splits among competing consumers, what
/// <see cref="ProtocolMismatchRule"/> calls "the shape this analyzer spends its time recommending",
/// and the only direction in which the simulator can carry the work forward. An edge <em>into</em> a
/// queue is publishing. So a worker drawn pointing at the queue is, to every model in this product,
/// a second publisher and not a consumer - which is why the finding gets a variant that says exactly
/// that, instead of leaving the user to wonder why the simulation never delivers anything.
///
/// A Warning and not a Critical, for the reason ORPHAN_NODE is one: this is the shape of a draft -
/// the box you have not wired yet - more often than the shape of a decision, and the analyzer must
/// not go red between the stroke that draws the queue and the one that draws its worker.
/// </summary>
public class QueueNoConsumerRule : IArchitectureRule
{
    public string Code => RuleCodes.QueueNoConsumer;
    public Area Area => Area.Coupling;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.QUEUE_NO_CONSUMER.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.QUEUE_NO_CONSUMER.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // An orphan queue is ORPHAN_NODE's finding (QA B4): with no producer there is nothing to
        // drain, and a queue nobody publishes to is not a flow with no end, it is no flow.
        var queues = graph.Nodes
            // V14 §3.1. A Topic is the same lesson with the same silence: published facts nobody
            // subscribes to. Same code - "somebody publishes and nobody receives" is one claim,
            // not two - with the topic's own sentence.
            .Where(n => n.Type is NodeType.Queue or NodeType.Topic && !graph.OrphanNodes.Contains(n.Id))
            .ToList();

        if (queues.Count == 0)
            return RuleResult.NotApplicable;

        var findings = queues
            .Where(queue => !HasConsumer(graph, queue))
            .OrderBy(queue => queue.Id)
            .Select(queue => Report(graph, queue))
            .ToList();

        return RuleResult.Applicable(findings);
    }

    /// <summary>
    /// The edge that leaves the queue is the delivery. Nothing else is - and the telemetry watching
    /// the queue is not draining it (QA §4, coverage).
    ///
    /// <strong>V14 B8. And the delivery has to land on something that runs code.</strong> Until V14
    /// any non-telemetry arrow out of the queue counted, so a queue whose only outgoing edge
    /// pointed at a database passed - the one drawing this rule was written to catch, and the one
    /// where a worker drawn backwards went unreported because the false consumer hid the
    /// <c>backwards</c> variant. What consumes is <see cref="NodeRoles.ProcessesMessages"/>.
    ///
    /// A queue delivering to another queue is the broker routing, not dropping: the work is
    /// forwarded, and it is done if the chain ends in somebody who does it. So
    /// <c>Api → Cola1 → Cola2 → Worker</c> - the staged pipeline the V13 QA calls "the one this
    /// product itself recommends" - has a consumer at every stage, and <c>Api → Cola1 → Cola2</c>
    /// has one at none.
    /// </summary>
    public static bool HasConsumer(ArchitectureGraph graph, Node queue) =>
        HasConsumer(graph, queue.Id, []);

    private static bool HasConsumer(ArchitectureGraph graph, Guid queueId, HashSet<Guid> visited)
    {
        if (!visited.Add(queueId))
            return false;

        foreach (var edge in graph.OutgoingBySource[queueId])
        {
            if (edge.TargetNodeId == queueId || NodeRoles.IsTelemetryEdge(graph, edge))
                continue;

            if (!graph.NodesById.TryGetValue(edge.TargetNodeId, out var target))
                continue;

            if (NodeRoles.ProcessesMessages(target.Type))
                return true;

            if (target.Type is NodeType.Queue or NodeType.Topic && HasConsumer(graph, target.Id, visited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// D5.6: no fix on either variant. Wiring the consumer is the decision - which component, and
    /// whether the worker drawn backwards was meant as one at all - and reversing an edge is not a
    /// transformation the fix kinds know how to describe.
    /// </summary>
    private RuleFinding Report(ArchitectureGraph graph, Node queue)
    {
        // A Worker pointing at the queue is the one drawing the convention turns upside down: the
        // user meant "consumes from" and drew "depends on", and the sentence that helps is the one
        // that names the arrow. Two whole texts rather than a shared one plus a hint (V7 D1.1).
        var backwards = graph.IncomingByTarget[queue.Id]
            .Select(edge => graph.NodesById[edge.SourceNodeId])
            .Where(source => source.Type == NodeType.Worker)
            .OrderBy(source => source.Id)
            .FirstOrDefault();

        var elements = new List<FindingElement> { FindingElement.Node(queue.Id) };

        // V14 §3.1: the advice names what the box is - "wire the queue to its consumer" and
        // "subscribe somebody to the topic" are different instructions (V13 D1.2).
        var kind = queue.Type == NodeType.Topic ? ".topic" : "";

        if (backwards is null)
        {
            return new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.QUEUE_NO_CONSUMER.unconsumed.message", ("node", queue.Name)),
                LocalizedText.Of("finding.QUEUE_NO_CONSUMER.unconsumed.explanation"),
                LocalizedText.Of($"finding.QUEUE_NO_CONSUMER.unconsumed.suggestion{kind}"),
                elements);
        }

        elements.Add(FindingElement.Connection(graph.IncomingByTarget[queue.Id]
            .First(edge => edge.SourceNodeId == backwards.Id).Id));

        return new RuleFinding(
            Code,
            Area,
            DefaultSeverity,
            LocalizedText.Of("finding.QUEUE_NO_CONSUMER.unconsumed.message", ("node", queue.Name)),
            LocalizedText.Of("finding.QUEUE_NO_CONSUMER.backwards.explanation",
                ("node", queue.Name),
                ("worker", backwards.Name)),
            LocalizedText.Of($"finding.QUEUE_NO_CONSUMER.backwards.suggestion{kind}",
                ("node", queue.Name),
                ("worker", backwards.Name)),
            elements);
    }
}
