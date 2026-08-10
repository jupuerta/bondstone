using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.x: Post 3 of §18 turned into a rule - retries can make an outage worse. When a service
/// starts failing, the retries of each hop multiply with each other and pile load onto the
/// component that is already in trouble.
/// </summary>
public class RetryAmplificationRule : IArchitectureRule
{
    private const string Explanation =
        "Cuando un servicio empieza a fallar, los reintentos de cada salto se multiplican entre sí y aumentan la carga justo sobre el componente que ya está en apuros.";

    private const string Suggestion =
        "Reintenta solo en el salto más cercano al fallo, añade espera creciente entre intentos y limita los reintentos en los tramos intermedios.";

    public string Code => RuleCodes.RetryAmplification;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Producer;

    public string PassedMessage => "Los reintentos configurados no se amplifican en cadena.";

    public string PassedExplanation =>
        "Se han multiplicado los reintentos a lo largo de cada cadena síncrona y ninguna llega a generar una avalancha de peticiones.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronous = graph.Connections.Where(c => c.Synchronous).ToList();

        // Nothing to amplify while nobody retries.
        if (!synchronous.Any(c => graph.ResilienceOf(c).Retries >= 1))
            return RuleResult.NotApplicable;

        var findings = new List<RuleFinding>();

        foreach (var chain in FindAmplifiedChains(graph, synchronous))
        {
            findings.Add(Report(graph, chain, Severity.Critical,
                $"Los reintentos se multiplican a lo largo de la cadena {Walk(graph, chain)}: hasta {Factor(graph, chain)} peticiones por cada una original."));
        }

        var alreadyReported = findings.SelectMany(f => f.Elements)
            .Where(e => e.Type == ElementType.Connection)
            .Select(e => e.Id)
            .ToHashSet();

        foreach (var edge in synchronous.Where(c => !alreadyReported.Contains(c.Id)))
        {
            var resilience = graph.ResilienceOf(edge);

            // Retrying without a timeout is the ingredient that turns a slow dependency into an
            // avalanche: nothing bounds how long each attempt holds on before the next one starts.
            if (resilience.Retries is not >= 1 || resilience.TimeoutSeconds is not null)
                continue;

            findings.Add(Report(graph, [edge], Severity.Warning,
                $"La llamada de '{graph.NodesById[edge.SourceNodeId].Name}' a '{graph.NodesById[edge.TargetNodeId].Name}' reintenta {resilience.Retries} veces sin timeout que las acote."));
        }

        return RuleResult.Applicable(findings);
    }

    private RuleFinding Report(ArchitectureGraph graph, IReadOnlyList<Connection> edges, Severity severity, string message)
    {
        // MISSING_RESILIENCE stays quiet about these edges: saying "you are missing a timeout"
        // twice under two different codes is exactly the noise D4.5 exists to avoid.
        foreach (var edge in edges)
            graph.Facts.Add(new RetryAmplificationFact(edge.Id));

        return new RuleFinding(Code, Area, severity, message, Explanation, Suggestion, Interleave(edges));
    }

    private static int Factor(ArchitectureGraph graph, IEnumerable<Connection> chain) =>
        chain.Aggregate(1, (product, edge) => product * ((graph.ResilienceOf(edge).Retries ?? 0) + 1));

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

    /// <summary>
    /// Walks the synchronous chains multiplying <c>retries + 1</c> and stops at the first hop that
    /// crosses the threshold: going deeper would describe the same problem with a longer chain.
    /// The path set guards against cycles - CYCLE runs in the same phase, so its facts are not
    /// available here (D5.2).
    /// </summary>
    private static List<List<Connection>> FindAmplifiedChains(ArchitectureGraph graph, List<Connection> synchronous)
    {
        var bySource = synchronous.ToLookup(c => c.SourceNodeId);
        var withIncoming = synchronous.Select(c => c.TargetNodeId).ToHashSet();
        var chains = new List<List<Connection>>();
        var path = new List<Connection>();
        var onPath = new HashSet<Guid>();

        // Chains are walked from where they start, so a chain is described whole and only once.
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

    /// <summary>
    /// Drops leading and trailing hops that retry nothing: they multiply by one, so naming them
    /// would point the user at components that are not part of the problem.
    /// </summary>
    private static List<Connection> Trim(ArchitectureGraph graph, List<Connection> path) => path
        .SkipWhile(edge => graph.ResilienceOf(edge).Retries is not >= 1)
        .Reverse()
        .SkipWhile(edge => graph.ResilienceOf(edge).Retries is not >= 1)
        .Reverse()
        .ToList();
}
