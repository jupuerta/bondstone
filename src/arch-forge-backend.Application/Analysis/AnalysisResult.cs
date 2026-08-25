namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V2 D2.1, a clean redesign with no back-compatibility: front and back deploy together.
/// </summary>
/// <param name="ScoringVersion">Which scoring model produced this result (V2 D1.5).</param>
/// <param name="Score">Null when no area is scorable - an empty architecture is not a 100.</param>
/// <param name="Load">
/// V16 C5. How full each component is, measured while the architecture is drawn rather than
/// simulated - see <see cref="ComponentLoad"/>. <strong>Empty when nobody declared any traffic</strong>,
/// which is most drawings and every one made before V4: with no declared load there is no
/// utilisation, and a list of zeros would say "idle" where the truth is "not measured" - the same
/// distinction <see cref="Simulation.ElementState.Utilization"/> makes with a null.
/// </param>
/// <param name="Coverage">
/// V16 C7a. One line per registered rule and what it did here - fired, passed, or never applied -
/// see <see cref="RuleCoverage"/>. Ordered by area and then by code, so the catalogue that reads it
/// is grouped the way the panel groups everything else. Always the whole list: unlike
/// <see cref="Load"/> there is no architecture for which it says nothing, because "no rule of this
/// kind had anything to look at" is precisely what it is for.
/// </param>
public sealed record AnalysisResult(
    string ScoringVersion,
    int? Score,
    IReadOnlyList<AreaScore> Areas,
    AnalysisSummary Summary,
    IReadOnlyList<RuleFinding> Findings,
    IReadOnlyList<ComponentLoad>? Load = null,
    IReadOnlyList<RuleCoverage>? Coverage = null)
{
    /// <summary>V16 C5. Empty rather than null on a drawing with no declared traffic.</summary>
    public IReadOnlyList<ComponentLoad> Load { get; init; } = Load ?? [];

    /// <summary>V16 C7a. One entry per registered rule, in (area, code) order.</summary>
    public IReadOnlyList<RuleCoverage> Coverage { get; init; } = Coverage ?? [];

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
    ///
    /// <h3>v7 - the V14 sweep (docs/V14/QA-analisis-V14.md)</h3>
    ///
    /// The second QA round, and the one whose opening table said <em>"the more architecture you
    /// draw, the worse you score; the less sense it makes, the better"</em>. Sixteen corrections,
    /// three rules, one edge field and one component type, landed as one bump for the same reason
    /// v6 was: none of it shipped on its own, and every one of them moves what a drawing scores.
    /// Twenty rules become twenty-three.
    ///
    /// <strong>What was hiding findings.</strong> B1 - the scrape arrow took the Api out of the
    /// roots, so SPOF's walk entered through the metrics sink and the entry point's Critical
    /// vanished the moment NO_OBSERVABILITY's advice was followed; <c>TrafficRoots</c> is now the
    /// one definition of where traffic enters, read by the walk, by OutageImpact and (B6) by
    /// Security, which had gone quiet the same way. B4 - <c>instances &gt;= 2</c> declared on an
    /// Api, a Service or a Worker is redundancy, as <c>replicas</c> already was on a Database: the
    /// field the card asked for is the field the rule reads. B9 - CYCLE, SYNC_CHAIN,
    /// PROTOCOL_MISMATCH and both halves of SPOF stopped reading instrumentation, so wiring the
    /// telemetry by hand - synchronous by default on the canvas - no longer made the metrics box a
    /// Critical SPOF or escalated the database it scraped; MISSING_RESILIENCE keeps reading those
    /// edges on purpose.
    ///
    /// <strong>What was punishing correct architecture.</strong> B5 - a Gateway routes: each
    /// request goes to one of its routes, not to all of them, so splitting a monolith relieves the
    /// pieces and adding redundancy no longer doubles the load on the store. B3 - SYNC_CHAIN counts
    /// components that run logic (<c>IsLogicComponent</c>) and not boxes: the CDN, the router, the
    /// cache and the store a realistic drawing adds around <c>Api → Orders → Payments</c> are the
    /// layers the call crosses, not links; the threshold stays where V1 calibrated it, and the
    /// finding starts at the first thing it counts. B6 - a root is a door only if the outside
    /// could reach it (<c>ReceivesFromOutside</c>): a queue, a cache or a service at the top of a
    /// partial drawing is not the internet; and the frontier stops at anything that can ask who is
    /// calling (<c>CanAuthenticate</c>), which now includes a Service - a BFF is a door, it gets a
    /// door's question and the <c>authentication</c> field to answer it. B2 and B10 - two
    /// predicates for one idea: UNDERSIZED_COMPONENT never tells you to scale what is not yours to
    /// size (the users, the CDN, the cron, the telemetry, a third party), and the five resilience
    /// rules never opine on a third party's own calls (<c>IsNotYourCall</c>).
    ///
    /// <strong>The exemptions SPOF no longer guesses at.</strong> B11 - the "equivalent sibling"
    /// exemption is gone: two components of one type serving the same consumers are two things,
    /// not two copies, and with B4's field there is finally a way to say "copies" on purpose -
    /// redundancy is drawn (a balancer) or declared (instances, replicas), never inferred from who
    /// calls you. The balancer convention keeps its known limit (T14), pinned in a test. B12 - the
    /// identity provider is a SPOF with a single hard dependent, graded by what losing it costs:
    /// one door blocking on it is every request that door admits, so the drawing the type was
    /// justified with stops scoring Resilience 100, and the way out its suggestion teaches -
    /// validate locally, and a fallback that keeps a valid session alive - is the fix
    /// MISSING_RESILIENCE now offers on that edge.
    ///
    /// <strong>The cheap ones.</strong> B7 - MISSING_CACHE counts consumers, not arrows. B8 - a
    /// queue delivering to a Database has no consumer (<c>ProcessesMessages</c>); one delivering
    /// to another queue still does. B14 - configuration ranges are validated on save, so a
    /// negative RPS can no longer buy a hundred. B15 - a door declaring "no authentication" while
    /// wired to an IdentityProvider is a contradiction with its own Warning, not a silent choice.
    ///
    /// <strong>And §4.1, the twenty-first rule.</strong> IMPLAUSIBLE_DEPENDENCY asks whether the
    /// two ends of an arrow can talk at all - a store does not originate calls, nothing of yours
    /// calls the users, a CDN talks to an origin, a clock fires work. PROTOCOL_MISMATCH's doctrine
    /// on the ends instead of the protocol, and the reason a database that called the users
    /// stops scoring 98. (§4.4 travels beside it without moving a number: the card shows the
    /// denominator, and a score over fewer than four areas is labelled unevaluated.)
    ///
    /// <strong>B13 and the calibration.</strong> "You are not watching this box" and "you cannot
    /// see anything" stopped being the same colour: on a diagram of three or more observable
    /// components with no telemetry box at all, each unobserved component is Critical, so total
    /// blindness sits at 55 instead of the 85 a Warning-only area could never go below. The other
    /// half of the same correction is new rules where the areas were thin (Availability had one
    /// rule, Resilience eight): TRAFFIC_REJECTED (Availability) says out loud that a rate limit
    /// shedding most of the declared demand is turning users away, not keeping the system healthy
    /// - the limit had been converting "undersized" into "fine" - and MISSING_CDN (Scalability)
    /// finally gives the Cdn type the rule D3.2 demands: a public door serving a thousand
    /// requests a second with no edge in front is asked, once, whether the edge belongs there.
    ///
    /// <strong>§4.2 - the edge learned what the call does with the data.</strong> <c>access</c>:
    /// read, write, or absent - which means both, so nothing drawn before v7 changes. A declared
    /// write no longer wants a cache (MISSING_CACHE), and SHARED_DATABASE says the sentence it
    /// always wanted - two services <em>writing</em> one schema - when the drawing declares it,
    /// never guessed from the default.
    ///
    /// <strong>§3.1 - the Topic.</strong> Publish-subscribe beside point-to-point: the load model
    /// duplicates across its fan-out where a Queue divides (the cost lesson of events),
    /// QUEUE_NO_CONSUMER reads it with its own sentence, HIGH_COUPLING deliberately does not count
    /// its fan-out - broadcasting is half of why you use one - and SPOF grades the broker like any
    /// broker. It is the one type that cleared V2 D3.2's bar on day one with rules that already
    /// existed.
    ///
    /// <strong>The four reference fixtures land at 98 / 90 / 80 / 69</strong>, and the two moves
    /// compose: B3 lifted all four (a chain measured in logic, not boxes), then B13 dropped the
    /// three that run entirely blind - <c>resilient-async</c> 95 → 90, <c>gateway-microservices</c>
    /// 85 → 80, <c>worst-case</c> 74 → 69, while <c>minimal-api-db</c>'s two boxes stay a sketch
    /// and keep 98. The fixtures predate the telemetry palette, and the story they now tell is the
    /// true one: good bones, and nobody watching. What moved further is what the sweep measured -
    /// the canonical read path lost its false Critical, microservices with real redundancy stopped
    /// scoring below the ones without, the two-box nonsense stopped being "solid", and the famous
    /// SPOF finally has a card.
    ///
    /// <h3>v8 - the V15 sweep (docs/V15/QA-analisis-V15.md)</h3>
    ///
    /// The third round, run over v7 with V14 verified closed first, and the one that found the
    /// analyzer's remaining lies were mostly <em>pairs</em>: two drawings that differed in one
    /// correct thing and scored as if they differed in a mistake. Fourteen corrections and one rule,
    /// landed as one bump for the reason the last two were. Twenty-three rules become twenty-four.
    ///
    /// <strong>What the user could photograph.</strong> A1 - three of IMPLAUSIBLE_DEPENDENCY's
    /// suggestions printed a literal <c>{target}</c>, in the panel and in the exported document,
    /// because nothing ever compared a template's placeholders with the arguments the backend
    /// sends - the check <c>LocalizedText</c>'s summary always claimed existed. It exists now
    /// (<c>check-catalogs.mjs</c>, sixth check) over a generated recording of every sentence every
    /// rule can say (<c>RecordAnalyzerSentences</c>); four dead keys fell out of the catalogue on
    /// the way. A2 - TIMEOUT_TOO_SHORT stopped advising a 67 s timeout on a call into a database
    /// at 8 000 % of its capacity: over a component UNDERSIZED_COMPONENT has marked, the number is
    /// Scalability's (a Producer fact, the MISSING_RESILIENCE pattern). A3 - wiring the telemetry
    /// the way the canvas draws it no longer costs Resilience: a scrape is not a call the
    /// architect configures, and the one instrumentation edge that is - a synchronous push to the
    /// metrics sink - is a Warning whose fix makes it asynchronous, never a Critical about the
    /// outage "propagating" into the sink.
    ///
    /// <strong>Pairs that scored as if one of them were wrong.</strong> B1 - <c>instances: 2</c>
    /// behind a balancer is the same fact as two boxes behind it; the balancer stops being a
    /// Critical SPOF for the field V14 B4 added. B2 - two replicas of a service behind a
    /// balancer are one service on its database, not integration by database; SHARED_DATABASE
    /// and MISSING_CACHE group what hangs from one balancer with one type (the known limit of
    /// the convention, pinned), and E3 gives SHARED_DATABASE a <c>readers</c> sentence for a
    /// schema declared read-only. C2 - the Cdn is not graded as a single point of failure, for
    /// the reason it is not sized or watched: adding the edge MISSING_CDN asks for now raises the
    /// score instead of trading 20 points of Scalability for 15 of Availability. C1 -
    /// <c>Client → Cdn → ObjectStorage</c>, the static site, is what IMPLAUSIBLE_DEPENDENCY
    /// blesses and EXPOSED_DATABASE no longer marks Critical. C3 - a third party's service is an
    /// origin a CDN can front and work a clock can fire. E5 - outside traffic reaching something
    /// that cannot authenticate is UNAUTHENTICATED_ENTRY's <c>unguarded</c> finding, not an area
    /// with no score.
    ///
    /// <strong>The calibration.</strong> D2 - "the system is blind" is one Critical, said once,
    /// and every unobserved box is the Warning it says it is; B13 had made the second claim by
    /// recolouring every instance of the first, which handed Observability the biggest hammer in
    /// the analyzer and made its colour depend on counting boxes. Blind means nothing is watched,
    /// not that the box is missing. Total blindness now costs 15 plus 2,5 per box - 78 for three,
    /// 60 for ten, the floor of 55 from twelve up - so a bigger blind system pays more, which is
    /// what coverage means. D1 travels beside it on the client: the word next to the number needs
    /// five scored areas <em>and</em> three connected components, so a two-box sketch is
    /// "unevaluated" whichever areas apply - which V14's threshold of areas alone could no longer
    /// say once its own rules made five areas apply to everything.
    ///
    /// <strong>And E2, the twenty-fourth rule.</strong> TIMEOUT_TOO_LONG: a timeout at or above
    /// the simulator's unbounded wait (<c>LatencyModel.UnboundedWait</c>, 30 s) protects exactly
    /// as much as no timeout at all, and the drawing B14's own ceiling allowed - 600 s on
    /// <c>Api → Db</c> - stops scoring 99. Graded the way MISSING_RESILIENCE grades a missing one:
    /// Critical when held open, uncovered, on a Critical SPOF. (E1 gives SYNC_CHAIN a suggestion
    /// for a read path that does not propose a queue; E4 groups thousands from five digits.)
    ///
    /// <strong>The four reference fixtures land at 98 / 92 / 83 / 70.</strong> Only D2 moved them:
    /// <c>resilient-async</c> 90 → 92 (one Critical instead of seven, and the right one),
    /// <c>gateway-microservices</c> 80 → 83, <c>worst-case</c> 69 → 70; <c>minimal-api-db</c>
    /// stays a sketch at 98 and is now labelled one. Every other correction of the sweep left
    /// all four exactly where they were, which was the control the sweep ran on. What moved is
    /// what it measured: the best architecture the palette can draw scores 100 (it was 96, and
    /// the Critical was the CDN), and the two-box nonsense of V14's opening table no longer
    /// outscores it with a word next to the number.
    ///
    /// <h3>v8 stays v8 - the V16 contract (docs/V16/ArchForge_contenido_didactico_V16.md)</h3>
    ///
    /// <see cref="Load"/> and <see cref="Coverage"/> widen the payload without touching the
    /// analyzer: both are numbers it already had and threw away - the utilisation
    /// <c>UNDERSIZED_COMPONENT</c> grades on, and the applicability every rule already declares -
    /// so <strong>the four reference fixtures stay at 98 / 92 / 83 / 70</strong> and two results
    /// produced before and after remain comparable, which is the whole reason this field travels
    /// (V2 D1.5). A scoring version says what the analyzer <em>found</em>; it is not a shape
    /// number, and bumping it here would tell every stored history that its figures had aged when
    /// nothing about them moved.
    /// </summary>
    public const string CurrentScoringVersion = "v8";
}
