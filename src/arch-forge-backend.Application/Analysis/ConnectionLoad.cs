namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V17 D3. How much traffic one call carries, published <strong>while the architecture is being
/// drawn</strong> - the twin of <see cref="ComponentLoad"/> one level down, on the arrow instead of
/// on the box.
///
/// <strong>Nothing here is new either.</strong> <see cref="Simulation.LoadModel"/> has written
/// <c>_edgeLoad</c> on both of its passes since V4 DT.3, and until now nobody read it: it existed
/// only so the next node could add its incoming edges up. That is the same shape of loss C5 found -
/// a number computed and spent - except this one was never even spent on a verdict. The
/// amplification the panel reports as a <c>×24</c> happens <em>on these arrows</em>, and the figure
/// that says so was thrown away one line after it was computed.
///
/// <strong>The four rules of the split travel with it, without being repeated here.</strong> A
/// cache lets through <c>load × (1 − hitRatio)</c> and a CDN does the same at the edge; a router -
/// gateway or balancer - divides rather than duplicates (V14 B5); a queue delivers what its
/// consumers can chew and the rest is backlog; everything else sums, because a fan-out of calls
/// happens whole. Those decisions are <see cref="Simulation.LoadModel.Basis"/>'s and stay there:
/// this record is their result, which is exactly what the drawing needs and what prose cannot say.
/// It is also the visible half of the asymmetry of Anexo B - <em>latency takes the maximum of a
/// fan-out, load sums it</em> - which until now had no pixel anywhere.
///
/// <strong>One entry per call, zeros included.</strong> An edge carrying nothing carries nothing for
/// a reason worth seeing: it instruments (a scrape is not traffic, QA §4), or it closes a cycle and
/// is cut at 0 the way V3 D5.4 cuts it. Dropping those would make the list disagree with the
/// drawing, and a client that cannot find an arrow in the list has to guess whether the answer is
/// "zero" or "not measured". The whole list is absent instead - see
/// <see cref="AnalysisResult.CallLoad"/> - which is the same distinction
/// <see cref="AnalysisResult.Load"/> already makes.
/// </summary>
/// <param name="ConnectionId">The call, by the id the canvas already draws the arrow with.</param>
/// <param name="Rps">
/// Requests per second the call carries, after the source's split and under the second pass's retry
/// amplification (<see cref="Simulation.LoadModel.CallLoadOf"/>). Rounded to two decimals, the same
/// as <see cref="ComponentLoad.Rps"/>, so an arrow and the box it arrives at never print two
/// figures that do not add up.
/// </param>
public sealed record ConnectionLoad(Guid ConnectionId, decimal Rps);
