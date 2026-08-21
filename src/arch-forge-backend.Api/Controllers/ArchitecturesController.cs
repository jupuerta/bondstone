using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.Queries;
using arch_forge_backend.Application.Simulation;
using Microsoft.AspNetCore.Mvc;

namespace arch_forge_backend.Api.Controllers;

[ApiController]
[Route("api/architectures")]
public class ArchitecturesController : ControllerBase
{
    private readonly CreateArchitectureCommandHandler _createHandler;
    private readonly UpdateArchitectureCommandHandler _updateHandler;
    private readonly GetArchitectureByShareTokenQueryHandler _getByShareTokenHandler;
    private readonly GetArchitectureByEditTokenQueryHandler _getByEditTokenHandler;
    private readonly DeleteArchitectureCommandHandler _deleteHandler;
    private readonly AnalyzeArchitectureQueryHandler _analyzeHandler;
    private readonly SimulateArchitectureQueryHandler _simulateHandler;
    private readonly ForkArchitectureCommandHandler _forkHandler;
    private readonly GetArchitectureHistoryQueryHandler _historyHandler;

    public ArchitecturesController(
        CreateArchitectureCommandHandler createHandler,
        UpdateArchitectureCommandHandler updateHandler,
        GetArchitectureByShareTokenQueryHandler getByShareTokenHandler,
        GetArchitectureByEditTokenQueryHandler getByEditTokenHandler,
        DeleteArchitectureCommandHandler deleteHandler,
        AnalyzeArchitectureQueryHandler analyzeHandler,
        SimulateArchitectureQueryHandler simulateHandler,
        ForkArchitectureCommandHandler forkHandler,
        GetArchitectureHistoryQueryHandler historyHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _getByShareTokenHandler = getByShareTokenHandler;
        _getByEditTokenHandler = getByEditTokenHandler;
        _deleteHandler = deleteHandler;
        _analyzeHandler = analyzeHandler;
        _simulateHandler = simulateHandler;
        _forkHandler = forkHandler;
        _historyHandler = historyHandler;
    }

    /// <summary>
    /// Mints an architecture with a fresh share token and edit token. The <c>Location</c> header
    /// points at the edit route: creating something is the one moment its author has nowhere else
    /// to get that token from.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateArchitectureCommand command, CancellationToken cancellationToken)
    {
        var result = await _createHandler.Handle(command, cancellationToken);
        return CreatedAtAction(nameof(GetByEditToken), new { editToken = result.EditToken }, result);
    }

    /// <summary>The read handle. Anyone holding a share token can view, analyze, simulate and fork - never write.</summary>
    [HttpGet("{shareToken}")]
    public async Task<IActionResult> GetByShareToken(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _getByShareTokenHandler.Handle(new GetArchitectureByShareTokenQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// The owner's own load. Under <c>/edit/</c> rather than sharing the single-segment shape of
    /// <see cref="GetByShareToken"/>: the two tokens must never be interchangeable at the routing
    /// level, or a client bug that sends one where the other belongs would fail silently instead of
    /// 404ing.
    /// </summary>
    [HttpGet("edit/{editToken}")]
    public async Task<IActionResult> GetByEditToken(string editToken, CancellationToken cancellationToken)
    {
        var result = await _getByEditTokenHandler.Handle(new GetArchitectureByEditTokenQuery { EditToken = editToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("edit/{editToken}")]
    public async Task<IActionResult> Update(string editToken, [FromBody] UpdateArchitectureCommand command, CancellationToken cancellationToken)
    {
        command.EditToken = editToken;
        var result = await _updateHandler.Handle(command, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
    // 400 (validation / invalid Configuration) and 409 (version conflict) are mapped
    // by ArchForgeExceptionHandler from exceptions thrown in the handler.

    /// <summary>
    /// Deletes the architecture and its whole graph. By edit token, like every other write, and
    /// with no version: a delete has nothing to merge, so the 409 the PUT carries would only force
    /// the caller to read an architecture in order to throw it away (see the handler).
    ///
    /// 204 when it is gone, 404 when there was nothing to delete - which on an irreversible
    /// operation with no history is information, not pedantry.
    /// </summary>
    [HttpDelete("edit/{editToken}")]
    public async Task<IActionResult> Delete(string editToken, CancellationToken cancellationToken)
    {
        var deleted = await _deleteHandler.Handle(new DeleteArchitectureCommand { EditToken = editToken }, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// V4 D2.3. Copies the architecture into a new one with its own tokens, so opening a link
    /// somebody shared and playing with it stops meaning editing theirs (§11). By share token: fork
    /// is a read-side action, offered to anyone who can already see the architecture.
    /// </summary>
    [HttpPost("{shareToken}/fork")]
    public async Task<IActionResult> Fork(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _forkHandler.Handle(new ForkArchitectureCommand { ShareToken = shareToken }, cancellationToken);
        return result is null
            ? NotFound()
            : CreatedAtAction(nameof(GetByEditToken), new { editToken = result.EditToken }, result);
    }

    /// <summary>
    /// V4 D5.4. What this architecture has scored, oldest first. Written on save (never on
    /// analysis) and the only thing in V4 that persists a verdict - which is why every entry
    /// carries the scoring version that produced it.
    /// </summary>
    [HttpGet("{shareToken}/history")]
    public async Task<IActionResult> History(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _historyHandler.Handle(new GetArchitectureHistoryQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{shareToken}/analyze")]
    public async Task<IActionResult> Analyze(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _analyzeHandler.Handle(new AnalyzeArchitectureQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// V3 D2.4. Reads the saved architecture, exactly like /analyze, and persists nothing: a
    /// scenario is an experiment over an architecture, not a state of it (D3.6).
    ///
    /// An empty scenario is valid and answers 200 with everything green - it is what the screen
    /// needs when you enter Simulate mode before choosing anything (D2.1). A 400 comes from an
    /// unknown fault kind or the same fault twice over one element; an id that is no longer in the
    /// graph is dropped and reported back in `discardedFaults`, so an old link still opens.
    /// </summary>
    [HttpPost("{shareToken}/simulate")]
    public async Task<IActionResult> Simulate(string shareToken, [FromBody] SimulateRequest? request, CancellationToken cancellationToken)
    {
        var query = new SimulateArchitectureQuery
        {
            ShareToken = shareToken,
            Scenario = new SimulationScenario(request?.Faults ?? [])
        };

        var result = await _simulateHandler.Handle(query, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>The body is just the scenario; the architecture is the one in the database.</summary>
    public class SimulateRequest
    {
        public List<Fault>? Faults { get; set; }
    }
}
