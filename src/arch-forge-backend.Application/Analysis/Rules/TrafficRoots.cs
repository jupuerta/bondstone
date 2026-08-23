using arch_forge_backend.Domain.Entities;

namespace arch_forge_backend.Application.Analysis.Rules;

/// <summary>
/// V14 B1. Where a request enters the architecture — <see cref="ArchitectureGraph.RootNodes"/>
/// asked of the traffic rather than of the drawing: a component nothing <em>calls</em>, rather than
/// one nothing points at.
///
/// <strong>The two are the same list until somebody wires the telemetry</strong>, and then they stop
/// being it. A box being scraped for metrics is not being called by anybody, but the scrape is an
/// arrow, so <c>Obs → Api</c> took the Api out of <c>RootNodes</c> and put the metrics sink in.
/// Availability read that literally, twice over: <see cref="SpofRule"/>'s walk entered through the
/// telemetry and reached everything behind the Api down its own instrumentation cables, so the Api
/// stopped being a point of passage and its Critical vanished; and
/// <see cref="OutageImpact.OfLosing"/> stopped recognising it as a front door, so what survived came
/// out a Warning saying every caller was covered. Both of them the moment the student did what
/// <c>NO_OBSERVABILITY</c> asked.
///
/// Held in one place for the reason <c>ArchitectureGraph</c> gives for holding <c>RootNodes</c>
/// there: two rules answering "where does traffic come in" slightly differently is how an area stops
/// being trustworthy. It is deliberately <em>not</em> <see cref="EntryPoints.TrafficOrigins"/>, which
/// answers Security's question — what is exposed to the internet — and is allowed to say "nowhere"
/// about a graph whose requests still have to enter somewhere.
/// </summary>
public static class TrafficRoots
{
    /// <summary>
    /// The components no non-telemetry edge arrives at. The telemetry box itself is never one of
    /// them, for the reason <see cref="Simulation.CapacityModel"/> already wrote down and this half
    /// of the analyzer has agreed with since QA B1: it is nobody's lesson, and leaving it out is
    /// what keeps an empty answer meaning "traffic has nowhere to enter" instead of "the metrics
    /// sink is the only door".
    /// </summary>
    public static IReadOnlyList<Node> Of(ArchitectureGraph graph) =>
        graph.Nodes
            .Where(node => !NodeRoles.IsTelemetry(node.Type)
                           && !graph.IncomingByTarget[node.Id].Any(edge => !NodeRoles.IsTelemetryEdge(graph, edge)))
            .ToList();
}
