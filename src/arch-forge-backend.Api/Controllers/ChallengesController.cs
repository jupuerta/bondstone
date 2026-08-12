using arch_forge_backend.Application.Challenges;
using Microsoft.AspNetCore.Mvc;

namespace arch_forge_backend.Api.Controllers;

/// <summary>
/// V4 D1.1. Three endpoints and none of them writes a challenge: the catalogue is content in the
/// repo (D1.5), the veredicto is recomputed and never stored (D3.4), and there is no identity to
/// attach progress to (D2.1) - so what a client keeps is a slug in <c>localStorage</c>.
/// </summary>
[ApiController]
[Route("api/challenges")]
public class ChallengesController : ControllerBase
{
    private readonly ChallengeCatalog _catalog;
    private readonly StartChallengeCommandHandler _startHandler;
    private readonly EvaluateChallengeQueryHandler _evaluateHandler;

    public ChallengesController(
        ChallengeCatalog catalog,
        StartChallengeCommandHandler startHandler,
        EvaluateChallengeQueryHandler evaluateHandler)
    {
        _catalog = catalog;
        _startHandler = startHandler;
        _evaluateHandler = evaluateHandler;
    }

    [HttpGet]
    public IActionResult List() =>
        Ok(_catalog.All.Select(c => c.ToSummaryDto()).ToList());

    [HttpGet("{slug}")]
    public IActionResult Get(string slug)
    {
        var definition = _catalog.Find(slug);
        return definition is null ? NotFound() : Ok(definition.ToDetailDto());
    }

    /// <summary>
    /// Instantiates the starter architecture and answers like <c>POST /architectures</c> does - the
    /// attempt is an ordinary architecture with an ordinary share token (D2.2), which is why the
    /// client can hand it straight to <c>/a/{token}</c>.
    /// </summary>
    [HttpPost("{slug}/start")]
    public async Task<IActionResult> Start(string slug, CancellationToken cancellationToken)
    {
        var result = await _startHandler.Handle(new StartChallengeCommand { Slug = slug }, cancellationToken);
        return result is null ? NotFound() : Created($"/api/architectures/{result.ShareToken}", result);
    }

    /// <summary>
    /// D6.2: explicit, not live. It costs one analysis and up to three simulations (D3.2), and the
    /// moment of the veredicto is a moment - a checklist that turns green on its own while you type
    /// eats it.
    ///
    /// 404 when the slug or the architecture is gone; there is nothing here that can be a 400,
    /// because the requirements come from the repo and not from the request.
    /// </summary>
    [HttpPost("{slug}/evaluate")]
    public async Task<IActionResult> Evaluate(string slug, [FromBody] EvaluateRequest request, CancellationToken cancellationToken)
    {
        var query = new EvaluateChallengeQuery { Slug = slug, ArchitectureId = request.ArchitectureId };
        var result = await _evaluateHandler.Handle(query, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    public class EvaluateRequest
    {
        public Guid ArchitectureId { get; set; }
    }
}
