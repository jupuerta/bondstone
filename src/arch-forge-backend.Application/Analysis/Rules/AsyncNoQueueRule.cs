using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

// The «synchronous» box answers one question — does the caller wait? — and every rule that reads it
// stops looking at an edge the moment the answer is no. This is the rule for the other question:
// where does the work wait while nobody is doing it? A call that does not wait and has no broker at
// either end keeps the message nowhere: if the target is down or saturated at that moment, the work
// is lost and nobody finds out. The mechanism card says «Queue node + asynchronous call»; until this
// rule the engine only checked the second half.
public class AsyncNoQueueRule : IArchitectureRule
{
    public string Code => RuleCodes.AsyncNoQueue;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.ASYNC_NO_QUEUE.passed.message");
    public LocalizedText PassedExplanation => LocalizedText.Of("finding.ASYNC_NO_QUEUE.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        // Every hand-off of work that does not wait, brokers included: a drawing where all of them go
        // through a queue has to come out «passed», not «not applicable».
        var candidates = graph.Connections
            .Where(c => !c.Synchronous
                        && !NodeRoles.IsNotYourCall(graph, c)
                        && !NodeRoles.IsTelemetryEdge(graph, c)
                        && (Publishes(graph, c) || Delivers(graph, c)))
            .ToList();

        if (candidates.Count == 0)
            return RuleResult.NotApplicable("rule.na.ASYNC_NO_QUEUE.noAsyncCall");

        var findings = candidates
            .Where(c => !IsBroker(TypeOf(graph, c.SourceNodeId)) && !IsBroker(TypeOf(graph, c.TargetNodeId)))
            // Amqp with no broker at either end is a drawing error, and PROTOCOL_MISMATCH already says
            // «draw the queue or switch to Http» from the «fix this first» band.
            .Where(c => c.Protocol != ProtocolType.Amqp)
            .OrderBy(c => c.Id)
            .Select(c => Report(graph, c))
            .ToList();

        return RuleResult.Applicable(findings);
    }

    // A component that works, handing work to another one — or to the broker that will hold it.
    // Routers route and a scheduler fires a clock: neither hands work over. A store as the far end is
    // a deferred write, which is another lesson.
    private static bool Publishes(ArchitectureGraph graph, Connection edge) =>
        HandsOffWork(TypeOf(graph, edge.SourceNodeId))
        && TypeOf(graph, edge.TargetNodeId) is { } target
        && (IsBroker(target) || NodeRoles.ProcessesMessages(target));

    private static bool Delivers(ArchitectureGraph graph, Connection edge) =>
        IsBroker(TypeOf(graph, edge.SourceNodeId))
        && TypeOf(graph, edge.TargetNodeId) is { } target
        && NodeRoles.ProcessesMessages(target);

    private static bool HandsOffWork(NodeType? type) =>
        type is NodeType.Api or NodeType.Service or NodeType.Worker;

    private static bool IsBroker(NodeType? type) =>
        type is NodeType.Queue or NodeType.Topic;

    private static NodeType? TypeOf(ArchitectureGraph graph, Guid nodeId) =>
        graph.NodesById.TryGetValue(nodeId, out var node) ? node.Type : null;

    private RuleFinding Report(ArchitectureGraph graph, Connection edge)
    {
        var source = graph.NodesById[edge.SourceNodeId].Name;
        var target = graph.NodesById[edge.TargetNodeId].Name;

        return new RuleFinding(
            Code,
            Area,
            DefaultSeverity,
            LocalizedText.Of("finding.ASYNC_NO_QUEUE.unbuffered.message", ("source", source), ("target", target)),
            LocalizedText.Of("finding.ASYNC_NO_QUEUE.unbuffered.explanation", ("source", source), ("target", target)),
            LocalizedText.Of("finding.ASYNC_NO_QUEUE.unbuffered.suggestion", ("source", source), ("target", target)),
            [FindingElement.Connection(edge.Id)])
        {
            Fix = new AppliedFix(
                FixKinds.InsertQueue,
                FindingElement.Connection(edge.Id),
                LocalizedText.Of("fix.ASYNC_NO_QUEUE.insert", ("source", source), ("target", target)))
        };
    }
}
