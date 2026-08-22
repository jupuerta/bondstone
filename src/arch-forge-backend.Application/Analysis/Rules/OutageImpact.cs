using arch_forge_backend.Application.Simulation;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V13 D4.1. What a rule asks when it wants to say <em>how much</em> instead of <em>how many</em>.
///
/// <strong>The precedent is <see cref="TimeoutTooShortRule"/>, not a new idea.</strong> That rule has
/// used <see cref="CallCost"/> - a simulator model - since V4 to put a real millisecond figure in its
/// text, and it was the only one of the seventeen that did. Everything else counted things it could
/// see in the graph: three dependents, four components behind it, a fan-out of seven. Counting is
/// what an analyzer can do without help; measuring needs a model, and the model was already written
/// and tested two versions ago.
///
/// <strong>This does not blur the line V3 D2.3 drew.</strong> That decision keeps the analysis and
/// the simulation as two <em>contracts</em>, so a panel never mixes "this is always wrong" with "this
/// is what would happen if…" - and it still does not: no <c>SimulationResult</c> enters an
/// <c>AnalysisResult</c> and no finding gains a state. What crosses is a number, computed by a shared
/// model, the way <c>CallCost</c> already crossed.
/// </summary>
public static class OutageImpact
{
    /// <summary>
    /// The fraction of incoming requests that stops being served if <paramref name="nodeId"/> dies -
    /// between 0 and 1, and <strong>the same figure the impact panel prints</strong> as
    /// <c>requestsAffected</c>, because it is the same walk
    /// (<see cref="RequestYield.Impact"/>).
    ///
    /// <strong>The yield walk rather than the whole engine</strong>, and the two agree on this
    /// number. The engine hands <see cref="YieldInputs.Down"/> every component that ended up dead
    /// after propagation; handing it only the one that died is enough, because the walk takes the
    /// minimum along the call tree and reaches the same fraction on its own. It differs in what it
    /// names as the culprit - the component that actually died rather than the nearest one it took
    /// with it - which is the better answer here, since the finding is about that component.
    ///
    /// <strong>What it does not model</strong>, and the finding must not claim it does: calls that
    /// blow their own timeout because of the outage. The engine passes those in too. So this is a
    /// bound on the bound - if it says 40 %, it is at least 40 %.
    ///
    /// Every reason a request survives here is one <see cref="SpofRule"/> now also knows about
    /// (D1.1): <c>RequestYield.Through</c> has returned the whole yield for an asynchronous edge and
    /// for one carrying a timeout and a fallback since V3. The simulator knew what a hard dependency
    /// was three versions before the rule did.
    /// </summary>
    public static decimal OfLosing(ArchitectureGraph graph, LoadModel load, Guid nodeId)
    {
        // **The front door is its own case, and the yield walk does not cover it.**
        // `RequestYield` starts at the roots and only ever asks whether the *target* of an edge is
        // down - never whether the root it is standing on is. That is not an oversight there: the
        // simulator answers "the entry point itself died" with `entryPointsAffected`, and
        // `SimulationEngine` drops dead roots out of the latency figures for the same reason
        // (`live`). Asked here without this, a gateway - the flagship SPOF of the whole product -
        // came back as costing 0 % of requests, which is the opposite of true.
        //
        // One line and not a change to `RequestYield`, because the two are answering different
        // questions and only this one is about a component that is not there any more. Touching the
        // shared model would move `simulationVersion` for a case the simulator already reports
        // another way.
        if (graph.RootNodes.Any(root => root.Id == nodeId))
            return 1m;

        return RequestYield
            .Of(graph, load, new YieldInputs(new HashSet<Guid> { nodeId }, new HashSet<Guid>()))
            .Impact()
            .Affected;
    }
}
