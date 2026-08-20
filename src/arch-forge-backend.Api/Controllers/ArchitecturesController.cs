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
    private readonly GetArchitectureByShareTokenQueryHandler _getHandler;
    private readonly DeleteArchitectureCommandHandler _deleteHandler;
    private readonly AnalyzeArchitectureQueryHandler _analyzeHandler;
    private readonly SimulateArchitectureQueryHandler _simulateHandler;
    private readonly ForkArchitectureCommandHandler _forkHandler;
    private readonly GetArchitectureHistoryQueryHandler _historyHandler;

    public ArchitecturesController(
        CreateArchitectureCommandHandler createHandler,
        UpdateArchitectureCommandHandler updateHandler,
        GetArchitectureByShareTokenQueryHandler getHandler,
        DeleteArchitectureCommandHandler deleteHandler,
        AnalyzeArchitectureQueryHandler analyzeHandler,
        SimulateArchitectureQueryHandler simulateHandler,
        ForkArchitectureCommandHandler forkHandler,
        GetArchitectureHistoryQueryHandler historyHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _getHandler = getHandler;
        _deleteHandler = deleteHandler;
        _analyzeHandler = analyzeHandler;
        _simulateHandler = simulateHandler;
        _forkHandler = forkHandler;
        _historyHandler = historyHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateArchitectureCommand command, CancellationToken cancellationToken)
    {
        var result = await _createHandler.Handle(command, cancellationToken);
        return CreatedAtAction(nameof(GetByShareToken), new { shareToken = result.ShareToken }, result);
    }

    [HttpGet("{shareToken}")]
    public async Task<IActionResult> GetByShareToken(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _getHandler.Handle(new GetArchitectureByShareTokenQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateArchitectureCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _updateHandler.Handle(command, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
    // 400 (validation / invalid Configuration) and 409 (version conflict) are mapped
    // by ArchForgeExceptionHandler from exceptions thrown in the handler.

    /// <summary>
    /// Deletes the architecture and its whole graph. By id like the other writes, and with no
    /// version: a delete has nothing to merge, so the 409 the PUT carries would only force the
    /// caller to read an architecture in order to throw it away (see the handler).
    ///
    /// 204 when it is gone, 404 when there was nothing to delete - which on an irreversible
    /// operation with no history is information, not pedantry.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _deleteHandler.Handle(new DeleteArchitectureCommand { Id = id }, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// V4 D2.3. Copies the architecture into a new one with its own share token, so opening a link
    /// somebody shared and playing with it stops meaning editing theirs (§11).
    /// </summary>
    [HttpPost("{id:guid}/fork")]
    public async Task<IActionResult> Fork(Guid id, CancellationToken cancellationToken)
    {
        var result = await _forkHandler.Handle(new ForkArchitectureCommand { Id = id }, cancellationToken);
        return result is null
            ? NotFound()
            : CreatedAtAction(nameof(GetByShareToken), new { shareToken = result.ShareToken }, result);
    }

    /// <summary>
    /// V4 D5.4. What this architecture has scored, oldest first. Written on save (never on
    /// analysis) and the only thing in V4 that persists a verdict - which is why every entry
    /// carries the scoring version that produced it.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
    {
        var result = await _historyHandler.Handle(new GetArchitectureHistoryQuery { Id = id }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }


    [HttpPost("{id:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid id, CancellationToken cancellationToken)
    {
        var result = await _analyzeHandler.Handle(new AnalyzeArchitectureQuery { Id = id }, cancellationToken);
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
    [HttpPost("{id:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid id, [FromBody] SimulateRequest? request, CancellationToken cancellationToken)
    {
        var query = new SimulateArchitectureQuery
        {
            Id = id,
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
