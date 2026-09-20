using bondstone_backend.Application.Localization;
using bondstone_backend.Domain.Entities;
using bondstone_backend.Domain.Enums;

namespace bondstone_backend.Application.Analysis.Rules;

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
        var queues = graph.Nodes
            .Where(n => n.Type is NodeType.Queue or NodeType.Topic && !graph.OrphanNodes.Contains(n.Id))
            .ToList();

        if (queues.Count == 0)
            return RuleResult.NotApplicable("rule.na.QUEUE_NO_CONSUMER.noQueue");

        var findings = queues
            .Where(queue => !HasConsumer(graph, queue))
            .OrderBy(queue => queue.Id)
            .Select(queue => Report(graph, queue))
            .ToList();

        return RuleResult.Applicable(findings);
    }

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

    private RuleFinding Report(ArchitectureGraph graph, Node queue)
    {
        var backwards = graph.IncomingByTarget[queue.Id]
            .Select(edge => graph.NodesById[edge.SourceNodeId])
            .Where(source => source.Type == NodeType.Worker)
            .OrderBy(source => source.Id)
            .FirstOrDefault();

        var elements = new List<FindingElement> { FindingElement.Node(queue.Id) };

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
