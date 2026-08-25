using arch_forge_backend.Application.Simulation;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2, and the promissory note V3 D6.6 left written: <em>"DeadLetterQueue is material for the
/// QUEUE_NO_DLQ rule and comes back with the rule that justifies it"</em>. This is that day, and
/// the field comes back in the same commit as the rule that reads it - which is the principle D3.5
/// fixed and the reason it was not brought back earlier just because it was easy.
///
/// A queue without a dead-letter queue loses in silence: a message that cannot be processed is
/// retried, fails again, and disappears. It is the failure mode asynchronous architectures are
/// actually bitten by, and it leaves no trace anywhere on the canvas.
/// </summary>
public class QueueNoDlqRule : IArchitectureRule
{
    public string Code => RuleCodes.QueueNoDlq;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.QUEUE_NO_DLQ.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.QUEUE_NO_DLQ.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // An orphan queue is ORPHAN_NODE's finding and nobody else's (QA B4): it is part of no
        // flow, so there are no messages for it to lose, and asking for its DLQ penalized the same
        // leftover box twice - once in Coupling for existing, once in Resilience for a field on a
        // queue the architecture does not contain.
        var queues = graph.Nodes
            .Where(n => n.Type == NodeType.Queue && !graph.OrphanNodes.Contains(n.Id))
            .ToList();

        if (queues.Count == 0)
            return RuleResult.NotApplicable("rule.na.QUEUE_NO_DLQ.noQueue");

        // And a queue nobody drains is QUEUE_NO_CONSUMER's (QA §4): a message that is never
        // processed can never fail processing, so there is nothing for a dead-letter queue to
        // catch yet. The DLQ question comes back the moment the consumer is drawn - one lesson at
        // a time, under one code at a time (D4.5).
        var findings = queues
            .Where(queue => QueueNoConsumerRule.HasConsumer(graph, queue))
            .Where(queue => !NodeSettings.HasDeadLetterQueue(queue))
            .OrderBy(queue => queue.Id)
            .Select(queue => new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of("finding.QUEUE_NO_DLQ.missing.message", ("node", queue.Name)),
                LocalizedText.Of("finding.QUEUE_NO_DLQ.missing.explanation"),
                LocalizedText.Of("finding.QUEUE_NO_DLQ.missing.suggestion"),
                [FindingElement.Node(queue.Id)])
            {
                // D5.6, the plainest fix there is: it is one checkbox and there is no version of
                // this problem where the answer is anything else.
                Fix = new AppliedFix(
                    FixKinds.SetNodeFlag,
                    FindingElement.Node(queue.Id),
                    LocalizedText.Of("fix.QUEUE_NO_DLQ.enable"),
                    ConfigurationKey: "deadLetterQueue",
                    ConfigurationValue: true)
            })
            .ToList();

        return RuleResult.Applicable(findings);
    }
}
