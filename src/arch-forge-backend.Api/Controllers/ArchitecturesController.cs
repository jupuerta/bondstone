using arch_forge_backend.Application.Architectures.Commands;
using arch_forge_backend.Application.Architectures.Queries;
using Microsoft.AspNetCore.Mvc;

namespace arch_forge_backend.Api.Controllers;

[ApiController]
[Route("api/architectures")]
public class ArchitecturesController : ControllerBase
{
    private readonly CreateArchitectureCommandHandler _createHandler;
    private readonly UpdateArchitectureCommandHandler _updateHandler;
    private readonly GetArchitectureByShareTokenQueryHandler _getHandler;
    private readonly AnalyzeArchitectureQueryHandler _analyzeHandler;

    public ArchitecturesController(
        CreateArchitectureCommandHandler createHandler,
        UpdateArchitectureCommandHandler updateHandler,
        GetArchitectureByShareTokenQueryHandler getHandler,
        AnalyzeArchitectureQueryHandler analyzeHandler)
    {
        _createHandler = createHandler;
        _updateHandler = updateHandler;
        _getHandler = getHandler;
        _analyzeHandler = analyzeHandler;
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

    [HttpPost("{id:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid id, CancellationToken cancellationToken)
    {
        var result = await _analyzeHandler.Handle(new AnalyzeArchitectureQuery { Id = id }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
