namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D2.1, a clean redesign with no back-compatibility: front and back deploy together.
/// </summary>
/// <param name="ScoringVersion">Which scoring model produced this result (V2 D1.5).</param>
/// <param name="Score">Null when no area is scorable - an empty architecture is not a 100.</param>
public sealed record AnalysisResult(
    string ScoringVersion,
    int? Score,
    IReadOnlyList<AreaScore> Areas,
    AnalysisSummary Summary,
    IReadOnlyList<RuleFinding> Findings)
{
    /// <summary>
    /// The QA round of docs/QA-analisis.md, landed as one bump because none of it shipped
    /// separately. Eight corrections travel in it:
    ///
    /// <strong>B1</strong> - SPOF's pattern B stopped being a constant Critical and grades by the
    /// measured impact of the outage, the same walk its explanation already printed, so a finding
    /// can no longer be a Critical whose own card says not one request is lost. Observability
    /// nodes also left the pattern on both sides: wiring the telemetry no longer turns a clean
    /// entry point into a SPOF.
    ///
    /// <strong>B2</strong> - a system of record's redundancy is declared, never inferred from the
    /// wiring: two databases with identical consumers no longer exempt each other as "replicas",
    /// and a balancer over two of them is a router to two truths, not the redundancy mechanism.
    /// What clears the finding is what its suggestion asks for - <c>replicas</c> declared on the
    /// component.
    ///
    /// <strong>B3</strong> - Security asks "is there data back there" instead of "is there a
    /// Database": EXPOSED_DATABASE and UNAUTHENTICATED_ENTRY read <c>IsSystemOfRecord</c>, so the
    /// public bucket - an ObjectStorage on the frontier - stops being an area without a score.
    ///
    /// <strong>B4</strong> - ORPHAN_NODE's promise is now kept: a disconnected box is its finding
    /// and nobody else's. QUEUE_NO_DLQ, EXPOSED_DATABASE, NO_RATE_LIMIT and UNDERSIZED_COMPONENT
    /// stop grading leftovers, which also stops one lonely Database on the canvas from opening as
    /// a Critical.
    ///
    /// <strong>B5</strong> - NO_OBSERVABILITY stopped accepting any edge as instrumentation: an
    /// Observability node counts only when wired to something that is not itself telemetry, so
    /// two boxes watching each other no longer buy a perfect score for a blind system.
    ///
    /// <strong>B7</strong> - a self-loop no longer counts a node among its own dependents: one
    /// real consumer stopped becoming the two the SPOF threshold asks for. The loop itself stays
    /// CYCLE's finding, where it always belonged.
    ///
    /// <strong>B6</strong> - "outside" stopped meaning "root of the graph". A drawn Client
    /// declares the public surface; without one, roots that act from inside (Worker,
    /// Observability, Scheduler) are no longer doors - so a polling worker's private database and
    /// a watched database stop being "exposed", and a dead cron stops costing "100 % of
    /// requests". The QA §3 types (Client, Cdn, Scheduler, IdentityProvider) travel in the same
    /// bump: their mechanisms - traffic declared where it is born, absorption at the edge, jobs
    /// as load, delegation as authentication - are part of the same re-reading.
    ///
    /// <strong>B8</strong> - an unprotected call to a third party pays once in Resilience, not
    /// twice: EXTERNAL_NO_FALLBACK became a Producer, publishes a fact per edge it reports, and
    /// MISSING_RESILIENCE stays quiet on those as it already did for RETRY_AMPLIFICATION. The
    /// timeout half moved with it - its own sentence, and a fix that sets both - so clearing the
    /// Critical no longer uncovers the Warning it used to hide.
    ///
    /// B9 travels alongside without moving a number: saturation sheds nothing by decision (see
    /// LoadModel), and UNDERSIZED_COMPONENT's card stopped saying it did.
    ///
    /// <strong>The eight corrections leave the four reference fixtures where they were</strong>: no
    /// orphans, no telemetry wired, no passage point whose loss costs zero requests, one database
    /// each with no replicas declared, no object storage anywhere, and no unprotected call to a
    /// third party. What they move is what was mis-graded - upwards for async pipelines, diagrams
    /// that connected their telemetry and bare external calls, downwards for the drawings that
    /// were being scored on boxes their architecture does not contain or forgiven for data the
    /// rules did not read.
    ///
    /// <strong>QA §4 rides in the same bump with three rules</strong> - seventeen become twenty -
    /// and these do move two fixtures, on purpose. QUEUE_NO_CONSUMER (Coupling): a queue somebody
    /// publishes to and nobody drains, the asynchronous twin of ORPHAN_NODE; QUEUE_NO_DLQ now waits
    /// for the consumer before asking about the dead-letter queue. CACHE_AS_STORE (Resilience): a
    /// cache none of whose users can reach a system of record is the only place its data lives.
    /// SHARED_DATABASE (Coupling): two or more Services on one database or bucket - integration by
    /// database, a Warning that nothing protecting the calls can clear, because what it grades is
    /// the schema and not the call. <c>gateway-microservices</c> (three services on Db) goes 87 to
    /// 86 and <c>worst-case</c> (four) 75 to 74; <c>minimal-api-db</c> and <c>resilient-async</c>
    /// gain green ticks and keep their scores.
    ///
    /// <strong>And the product improvements of QA §4</strong>, the last passengers. NO_OBSERVABILITY
    /// grades by coverage - one finding per component nobody watches - so the area climbs with the
    /// wiring and a blind diagram sits at the bottom of that climb; every reference fixture runs
    /// blind and pays for it (gateway 84, resilient-async 94, worst-case 72; minimal stays 98). Each
    /// <see cref="AreaScore"/> now carries its <see cref="AreaScore.Penalties"/>, the arithmetic of
    /// D1.1 written out for the panel to show. And an Api can declare a rate limit, which
    /// NO_RATE_LIMIT reads on both kinds of door and the load model admits by - on everything that
    /// reaches the door, a Client's traffic included, which it did not before.
    ///
    /// V13 D1.4 bumped to v5 when SPOF's pattern A stopped counting arrows and started reading
    /// them (D1.1); v6 is that correction reaching the rest of the analyzer. Two figures produced
    /// by different scoring versions are not comparable either way, which is the whole reason this
    /// field travels (V2 D1.5) and why a challenge may never assert on one (V4 D1.4).
    /// </summary>
    public const string CurrentScoringVersion = "v6";
}
