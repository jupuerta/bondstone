using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V4 D5.2 - "timeout inconsistencies", the third of the debts V2 wrote down.
///
/// <strong>It was impossible in V2 and it is trivial now</strong>, and that is worth saying: it
/// needs to know what a call costs, and that is exactly <see cref="CallCost"/>, which V3 wrote and
/// tested for the simulator. The debt was not deferred out of laziness - it could not be done.
///
/// A timeout below what the call takes when everything is <em>working</em>, under the traffic that
/// was declared, does not protect anything: it expires every single time, so the dependency behaves as if it were permanently
/// down, retries pile onto a component that is answering fine, and the fallback - if there is one -
/// becomes the normal path. It is the one misconfiguration that turns a healthy architecture into
/// a broken one with no component failing.
/// </summary>
public class TimeoutTooShortRule : IArchitectureRule
{
    public string Code => RuleCodes.TimeoutTooShort;
    public Area Area => Area.Resilience;
    public Severity DefaultSeverity => Severity.Critical;
    public RulePhase Phase => RulePhase.Consumer;

    public string PassedMessage => "Los timeouts dan tiempo a que la llamada termine.";
    public string PassedExplanation =>
        "Cada timeout es mayor que lo que cuesta esa llamada con todo funcionando, así que solo salta cuando algo va mal de verdad.";

    public RuleResult Evaluate(ArchitectureGraph graph)
    {
        var timed = graph.Connections
            .Where(c => c.Synchronous && graph.ResilienceOf(c).TimeoutSeconds is > 0)
            .ToList();

        if (timed.Count == 0)
            return RuleResult.NotApplicable;

        // Nothing broken and nothing injected - but **under the declared load** (DT.6), and that is
        // what makes this rule worth having rather than dead code. Timeouts are configured in whole
        // seconds and an idle architecture answers in tens of milliseconds, so a timeout shorter
        // than an idle call is nearly unreachable. Traffic is what makes calls slow: a database at
        // six times its capacity costs 2,4 s, and a 1 s timeout in front of it expires every time
        // while every component is still "up".
        //
        // It is the same walk the simulator does, so the figure in the message is the figure the
        // impact panel shows.
        var cost = CallCost.Of(graph, CostInputs.Nominal with { QueueingFactors = LoadModel.Of(graph).QueueingFactors });
        var findings = new List<RuleFinding>();

        foreach (var edge in timed.OrderBy(c => c.Id))
        {
            var timeoutMs = LatencyModel.TimeoutMs(graph.ResilienceOf(edge).TimeoutSeconds!.Value);
            var callMs = cost.Of(edge).RawLatencyMs;

            if (callMs <= timeoutMs)
                continue;

            var source = graph.NodesById[edge.SourceNodeId].Name;
            var target = graph.NodesById[edge.TargetNodeId].Name;

            // Twice the measured cost, rounded up to the next whole second: enough headroom that
            // the fix does not have to be applied twice, and still a timeout rather than a wait.
            var suggested = Math.Max(1, (int)Math.Ceiling(callMs * 2 / 1000m));

            findings.Add(new RuleFinding(
                Code,
                Area,
                DefaultSeverity,
                $"El timeout de la llamada de '{source}' a '{target}' es más corto que la propia llamada.",
                $"Cuesta {Duration(callMs)} con todo funcionando y el timeout es de {Duration(timeoutMs)}: expira siempre, así que '{target}' se comporta como si estuviera caído de forma permanente.",
                $"Sube el timeout por encima de {Duration(callMs)}, o acorta la llamada quitando saltos de en medio.",
                [FindingElement.Connection(edge.Id)])
            {
                // D5.6: raising it is unambiguous. *Shortening the call* is the other fix and it is
                // a decision - which one of the hops goes - so it stays as text.
                Fix = new AppliedFix(
                    FixKinds.SetEdgeResilience,
                    FindingElement.Connection(edge.Id),
                    $"Subir el timeout a {suggested} s",
                    TimeoutSeconds: suggested)
            });
        }

        return RuleResult.Applicable(findings);
    }

    private static string Duration(int ms) => ms >= 1_000 ? $"{ms / 1_000m:0.##} s" : $"{ms} ms";
}
