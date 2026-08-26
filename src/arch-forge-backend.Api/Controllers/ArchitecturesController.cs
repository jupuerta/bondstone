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
    private readonly MapArchitectureFragilityQueryHandler _fragilityHandler;
    private readonly SweepArchitectureLoadQueryHandler _loadCurveHandler;
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
        MapArchitectureFragilityQueryHandler fragilityHandler,
        SweepArchitectureLoadQueryHandler loadCurveHandler,
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
        _fragilityHandler = fragilityHandler;
        _loadCurveHandler = loadCurveHandler;
        _forkHandler = forkHandler;
        _historyHandler = historyHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateArchitectureCommand command, CancellationToken cancellationToken)
    {
        var result = await _createHandler.Handle(command, cancellationToken);
        return CreatedAtAction(nameof(GetByEditToken), new { editToken = result.EditToken }, result);
    }

    [HttpGet("{shareToken}")]
    public async Task<IActionResult> GetByShareToken(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _getByShareTokenHandler.Handle(new GetArchitectureByShareTokenQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

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

    [HttpDelete("edit/{editToken}")]
    public async Task<IActionResult> Delete(string editToken, CancellationToken cancellationToken)
    {
        var deleted = await _deleteHandler.Handle(new DeleteArchitectureCommand { EditToken = editToken }, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("{shareToken}/fork")]
    public async Task<IActionResult> Fork(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _forkHandler.Handle(new ForkArchitectureCommand { ShareToken = shareToken }, cancellationToken);
        return result is null
            ? NotFound()
            : CreatedAtAction(nameof(GetByEditToken), new { editToken = result.EditToken }, result);
    }

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

    [HttpPost("{shareToken}/fragility")]
    public async Task<IActionResult> Fragility(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _fragilityHandler.Handle(new MapArchitectureFragilityQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{shareToken}/load-curve")]
    public async Task<IActionResult> LoadCurve(string shareToken, CancellationToken cancellationToken)
    {
        var result = await _loadCurveHandler.Handle(new SweepArchitectureLoadQuery { ShareToken = shareToken }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    public class SimulateRequest
    {
        public List<Fault>? Faults { get; set; }
    }
}
