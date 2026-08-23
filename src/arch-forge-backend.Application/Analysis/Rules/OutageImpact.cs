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
    /// (<see cref="RequestYield.Impact"/>) - with one difference, written down in the body: the
    /// doors it takes the worst of are <see cref="TrafficRoots"/>, so an instrumented graph does
    /// not get measured from its metrics sink (V14 B1).
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
        // V14 B1. The doors, and not simply the nodes nothing points at. An arrow that scrapes a
        // component for metrics is not a caller, so `Obs → Api` used to take the Api out of
        // `RootNodes` - and with it out of both halves of this answer: it stopped being recognised
        // as a front door below, and the walk further down started at the metrics sink instead.
        // Losing the entry point of an instrumented architecture came back as costing nobody
        // anything, which is how a Critical became a Warning saying every caller was covered.
        var roots = TrafficRoots.Of(graph);

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
        if (roots.Any(root => root.Id == nodeId))
        {
            // QA B6 refines the one line: a dead *door* costs everything that enters - but a root
            // is not always a door. A Worker, a Scheduler or an Observability node standing at the
            // top of its own flow starts work, it does not receive requests, and answering "100 %
            // of requests" for a dead cron was the same conflation of root-with-outside that
            // exposed the worker's database.
            return NodeRoles.ActsFromInside(graph.NodesById[nodeId].Type) ? 0m : 1m;
        }

        var served = RequestYield.Of(graph, load, new YieldInputs(new HashSet<Guid> { nodeId }, new HashSet<Guid>()));

        // The worst door, taken over the doors traffic actually uses. `RequestYield.Impact()` asks
        // exactly this of `graph.RootNodes`, and the two answers part company only on an
        // instrumented graph - where the telemetry box can be the one node nothing points at, so
        // that walk begins at the metrics sink, finds it perfectly healthy, and reports that losing
        // the database costs nothing (V14 B1). Asked here from the public per-node figure rather
        // than fixed there, for the same reason as the front-door line above: `RequestYield` is the
        // simulator's model, shared with the engine, and changing what it calls an entry point
        // moves `simulationVersion` for every scenario - which is a decision of its own and not
        // this fix's to take. With no door at all nothing is coming in, so nothing is affected,
        // which is `Impact()`'s answer to the same case.
        var worst = roots.Select(root => served.Of(root.Id)).DefaultIfEmpty(1m).Min();

        return Math.Round(1m - worst, 4);
    }
}
