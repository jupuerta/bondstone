using bondstone_backend.Application.Common;
using bondstone_backend.Domain.Entities;

using bondstone_backend.Application.Localization;

namespace bondstone_backend.Application.Analysis.Rules;

public class RetryAmplificationRule : IArchitectureRule
{
    private static readonly LocalizedText Explanation = LocalizedText.Of("finding.RETRY_AMPLIFICATION.explanation");
    private static readonly LocalizedText Suggestion = LocalizedText.Of("finding.RETRY_AMPLIFICATION.suggestion");

    public string Code => RuleCodes.RetryAmplification;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Producer;

    public LocalizedText PassedMessage => LocalizedText.Of("finding.RETRY_AMPLIFICATION.passed.message");

    public LocalizedText PassedExplanation => LocalizedText.Of("finding.RETRY_AMPLIFICATION.passed.explanation");

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronous = graph.Connections
            .Where(c => c.Synchronous && !NodeRoles.IsNotYourCall(graph, c))
            .ToList();

        if (!synchronous.Any(c => graph.ResilienceOf(c).Retries >= 1))
            return RuleResult.NotApplicable("rule.na.RETRY_AMPLIFICATION.noRetries");

        var findings = new List<RuleFinding>();

        foreach (var chain in FindAmplifiedChains(graph, synchronous))
        {
            findings.Add(Report(graph, chain, Severity.Critical,
                LocalizedText.Of("finding.RETRY_AMPLIFICATION.chain.message",
                    ("walk", Walk(graph, chain)),
                    ("factor", Factor(graph, chain)))));
        }

        var alreadyReported = findings.SelectMany(f => f.Elements)
            .Where(e => e.Type == ElementType.Connection)
            .Select(e => e.Id)
            .ToHashSet();

        foreach (var edge in synchronous.Where(c => !alreadyReported.Contains(c.Id)))
        {
            var resilience = graph.ResilienceOf(edge);

            if (resilience.Retries is not >= 1 || resilience.TimeoutSeconds is not null)
                continue;

            findings.Add(Report(graph, [edge], Severity.Warning,
                LocalizedText.Of("finding.RETRY_AMPLIFICATION.unbounded.message",
                    ("source", graph.NodesById[edge.SourceNodeId].Name),
                    ("target", graph.NodesById[edge.TargetNodeId].Name),
                    ("retries", resilience.Retries!.Value))));
        }

        return RuleResult.Applicable(findings);
    }

    private RuleFinding Report(ArchitectureGraph graph, IReadOnlyList<Connection> edges, Severity severity, LocalizedText message)
    {
        foreach (var edge in edges)
            graph.Facts.Add(new RetryAmplificationFact(edge.Id));

        return new RuleFinding(Code, Area, severity, message, Explanation, Suggestion, Interleave(edges));
    }

    // Saturating, because this is the one number in the product that a person types and nobody
    // bounds: four hops at a thousand retries each already pass `int.MaxValue`, and the plain
    // product would wrap round to something small and reassuring — the opposite of what this rule
    // is for.
    private static int Factor(ArchitectureGraph graph, IEnumerable<Connection> chain) =>
        Saturating.Product(chain.Select(edge => (graph.ResilienceOf(edge).Retries ?? 0) + 1));

    private static string Walk(ArchitectureGraph graph, IReadOnlyList<Connection> chain) =>
        string.Join(" → ", chain.Select(edge => graph.NodesById[edge.SourceNodeId].Name)
            .Append(graph.NodesById[chain[^1].TargetNodeId].Name));

    private static List<FindingElement> Interleave(IReadOnlyList<Connection> edges)
    {
        var elements = new List<FindingElement> { FindingElement.Node(edges[0].SourceNodeId) };

        foreach (var edge in edges)
        {
            elements.Add(FindingElement.Connection(edge.Id));
            elements.Add(FindingElement.Node(edge.TargetNodeId));
        }

        return elements;
    }

    private static List<List<Connection>> FindAmplifiedChains(ArchitectureGraph graph, List<Connection> synchronous)
    {
        var bySource = synchronous.ToLookup(c => c.SourceNodeId);
        var withIncoming = synchronous.Select(c => c.TargetNodeId).ToHashSet();
        var chains = new List<List<Connection>>();
        var path = new List<Connection>();
        var onPath = new HashSet<Guid>();

        foreach (var node in graph.Nodes.Where(n => !withIncoming.Contains(n.Id)).OrderBy(n => n.Id))
        {
            onPath.Add(node.Id);
            Walk(node.Id, 1);
            onPath.Remove(node.Id);
        }

        return chains;

        void Walk(Guid nodeId, int factor)
        {
            foreach (var edge in bySource[nodeId].OrderBy(c => c.TargetNodeId).ThenBy(c => c.Id))
            {
                if (!onPath.Add(edge.TargetNodeId))
                    continue;

                path.Add(edge);
                var accumulated = factor * ((graph.ResilienceOf(edge).Retries ?? 0) + 1);

                if (accumulated >= RuleThresholds.RetryAmplificationMax)
                    chains.Add(Trim(graph, path));
                else
                    Walk(edge.TargetNodeId, accumulated);

                path.RemoveAt(path.Count - 1);
                onPath.Remove(edge.TargetNodeId);
            }
        }
    }

    private static List<Connection> Trim(ArchitectureGraph graph, List<Connection> path) => path
        .SkipWhile(edge => graph.ResilienceOf(edge).Retries is not >= 1)
        .Reverse()
        .SkipWhile(edge => graph.ResilienceOf(edge).Retries is not >= 1)
        .Reverse()
        .ToList();
}
