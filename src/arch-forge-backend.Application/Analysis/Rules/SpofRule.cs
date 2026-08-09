namespace arch_forge_backend.Application.Analysis.Rules;

public class SpofRule : IArchitectureRule
{
    public string Code => "SPOF";
    public Severity Severity => Severity.Critical;

    public IEnumerable<RuleFinding> Evaluate(ArchitectureGraph graph)
    {
        var dependentsByTarget = graph.Connections
            .GroupBy(c => c.TargetNodeId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.SourceNodeId).Distinct().ToList());

        foreach (var node in graph.Nodes)
        {
            if (!dependentsByTarget.TryGetValue(node.Id, out var dependents) || dependents.Count < 2)
                continue;

            var hasRedundantSibling = graph.Nodes.Any(other =>
                other.Id != node.Id &&
                other.Type == node.Type &&
                dependentsByTarget.TryGetValue(other.Id, out var otherDependents) &&
                otherDependents.Intersect(dependents).Any());

            if (hasRedundantSibling)
                continue;

            yield return new RuleFinding
            {
                RuleCode = Code,
                Severity = Severity,
                NodeId = node.Id,
                Message = $"'{node.Name}' es un punto único de fallo: {dependents.Count} componentes dependen de él sin redundancia."
            };
        }
    }
}
