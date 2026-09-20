using bondstone_backend.Application.Simulation;
using bondstone_backend.Domain.Enums;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

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
        var queues = graph.Nodes
            .Where(n => n.Type == NodeType.Queue && !graph.OrphanNodes.Contains(n.Id))
            .ToList();

        if (queues.Count == 0)
            return RuleResult.NotApplicable("rule.na.QUEUE_NO_DLQ.noQueue");

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
