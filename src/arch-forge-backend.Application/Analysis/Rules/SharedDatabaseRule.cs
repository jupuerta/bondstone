using arch_forge_backend.Application.Localization;
using arch_forge_backend.Domain.Entities;
using arch_forge_backend.Domain.Enums;

namespace arch_forge_backend.Application.Analysis.Rules;

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
        var stores = graph.Nodes
            .Where(n => NodeRoles.IsSystemOfRecord(n.Type) && graph.IncomingByTarget[n.Id].Any())
            .ToList();

        if (stores.Count == 0)
            return RuleResult.NotApplicable("rule.na.SHARED_DATABASE.noStore");

        var findings = new List<RuleFinding>();

        foreach (var store in stores.OrderBy(s => s.Id))
        {
            var sharing = Callers(graph, store.Id, [])
                .Where(pair => pair.Service.Type == NodeType.Service)
                .GroupBy(pair => pair.Service.Id)
                .Select(group => (
                    Service: graph.NodesById[group.Key],
                    Edge: group.Select(pair => pair.Edge)
                        .OrderByDescending(edge => graph.ResilienceOf(edge).DeclaresWrite)
                        .ThenBy(edge => edge.Id)
                        .First()))
                .OrderBy(pair => pair.Service.Id)
                .ToList();

            var services = sharing
                .GroupBy(pair => NodeRoles.ReplicaSetOf(graph, pair.Service))
                .Select(set => set.OrderBy(pair => pair.Service.Id).ToList())
                .OrderBy(set => set[0].Service.Id)
                .ToList();

            if (services.Count < RuleThresholds.SharedDatabaseMinServices)
                continue;

            List<FindingElement> elements = [FindingElement.Node(store.Id)];
            elements.AddRange(services.SelectMany(set => set.Select(pair => FindingElement.Connection(pair.Edge.Id))));

            var writers = services
                .Where(set => set.Any(pair => graph.ResilienceOf(pair.Edge).DeclaresWrite))
                .ToList();

            var readers = services
                .Where(set => set.All(pair => graph.ResilienceOf(pair.Edge).DeclaresRead))
                .ToList();
            var possibleWriters = services.Count - readers.Count;

            var (variant, named) =
                writers.Count >= RuleThresholds.SharedDatabaseMinServices ? ("writers", writers)
                : possibleWriters <= 1 ? ("readers", readers)
                : ("shared", services);

            findings.Add(new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                LocalizedText.Of($"finding.SHARED_DATABASE.{variant}.message",
                    ("node", store.Name),
                    ("count", named.Count),
                    ("names", Names(named.Select(set => set[0].Service)))),
                LocalizedText.Of($"finding.SHARED_DATABASE.{variant}.explanation", ("node", store.Name)),
                LocalizedText.Of($"finding.SHARED_DATABASE.{variant}.suggestion"),
                elements));
        }

        return RuleResult.Applicable(findings);
    }

    // The callers of a store, read through any cache in front of it: a service that reaches the
    // schema through a cache still shares it — putting a box in between hid the coupling from this
    // rule without removing it (QA 30-08, §10.3 point 5) — the same way ReplicaSetOf reads through a
    // balancer. The edge kept is the service's own, where its access is declared.
    private static IEnumerable<(Node Service, Connection Edge)> Callers(ArchitectureGraph graph, Guid targetId, HashSet<Guid> visited)
    {
        if (!visited.Add(targetId))
            yield break;

        foreach (var edge in graph.IncomingByTarget[targetId])
        {
            var source = graph.NodesById.GetValueOrDefault(edge.SourceNodeId);
            if (source is null)
                continue;

            if (source.Type == NodeType.Cache)
            {
                foreach (var behind in Callers(graph, source.Id, visited))
                    yield return behind;

                continue;
            }

            yield return (source, edge);
        }
    }

    private static string Names(IEnumerable<Node> services) =>
        string.Join(", ", services.Select(service => $"'{service.Name}'"));
}
