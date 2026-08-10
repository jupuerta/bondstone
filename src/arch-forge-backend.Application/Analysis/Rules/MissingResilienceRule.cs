namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V2 D4.4. Only synchronous edges - on an asynchronous one these mechanisms do not apply.
///
/// This is the rule that put <c>Facts</c> into service: it needs to know whether another rule
/// marked the destination as a single point of failure, and whether RETRY_AMPLIFICATION already
/// spoke about the edge (D4.5).
/// </summary>
public class MissingResilienceRule : IArchitectureRule
{
    public string Code => RuleCodes.MissingResilience;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Warning;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Las llamadas síncronas tienen protección frente a fallos.";

    public string PassedExplanation =>
        "Cada llamada síncrona tiene un timeout que la acota y, cuando el destino es crítico, una alternativa por la que salir si no responde.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var synchronous = graph.Connections.Where(c => c.Synchronous).ToList();
        if (synchronous.Count == 0)
            return RuleResult.NotApplicable;

        // Escalation asks for a Critical SPOF, not just any SPOF: with severity now graded by
        // blast radius, requiring only "is a SPOF" flagged every call to any component shared by
        // two consumers, and a correct architecture with one shared cache came out full of
        // criticals (D4.4, correction v1.1).
        var criticalSpofs = graph.Facts
            .OfType<SinglePointOfFailureFact>()
            .Where(fact => fact.Severity == Severity.Critical)
            .Select(fact => fact.NodeId)
            .ToHashSet();

        // Saying "you are missing a timeout" twice, under two different codes, is what D4.5 calls
        // noise. The more specific rule already said it.
        var alreadyReported = graph.Facts
            .OfType<RetryAmplificationFact>()
            .Select(fact => fact.ConnectionId)
            .ToHashSet();

        var findings = new List<RuleFinding>();

        foreach (var edge in synchronous.Where(c => !alreadyReported.Contains(c.Id)))
        {
            var resilience = graph.ResilienceOf(edge);
            var hasTimeout = resilience.TimeoutSeconds is not null;

            // Asking for a circuit breaker on every synchronous edge would flag practically the
            // whole graph. Asking for it only where the destination already takes the system down
            // puts the message where the risk is (D4.4).
            var exposedToSpof = !resilience.CircuitBreaker
                                && !resilience.Fallback
                                && criticalSpofs.Contains(edge.TargetNodeId);

            if (hasTimeout && !exposedToSpof)
                continue;

            var severity = exposedToSpof ? Severity.Critical : Severity.Warning;
            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            findings.Add(new RuleFinding(
                Code,
                Area,
                severity,
                $"La llamada de '{source}' a '{target}' no tiene protección frente a fallos.",
                Explain(hasTimeout, exposedToSpof, target),
                Suggest(hasTimeout, exposedToSpof),
                [FindingElement.Connection(edge.Id)]));
        }

        return RuleResult.Applicable(findings);
    }

    // The three texts of §6.1 say what happens, why it matters and what to do - so the "why" has
    // to name the reason that actually applies to this edge, not the union of both.
    private static string Explain(bool hasTimeout, bool exposedToSpof, string target)
    {
        var reasons = new List<string>();

        if (!hasTimeout)
            reasons.Add("Sin timeout, una respuesta lenta retiene recursos indefinidamente.");

        if (exposedToSpof)
            reasons.Add($"Y como '{target}' es un punto único de fallo, su caída se propaga directamente hacia atrás.");

        return string.Join(" ", reasons);
    }

    private static string Suggest(bool hasTimeout, bool exposedToSpof)
    {
        if (!hasTimeout && exposedToSpof)
            return "Configura un timeout acorde a la operación y añade un circuit breaker o una respuesta alternativa para cuando el destino no conteste.";

        return hasTimeout
            ? "Añade un circuit breaker o una respuesta alternativa para cuando el destino no conteste."
            : "Configura un timeout acorde a la operación.";
    }
}
