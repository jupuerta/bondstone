using bondstone_backend.Application.Challenges;
using bondstone_backend.Api.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace bondstone_backend.Api.Controllers;

[ApiController]
[Route("api/challenges")]
public class ChallengesController : ControllerBase
{
    private readonly ChallengeCatalog _catalog;
    private readonly StartChallengeCommandHandler _startHandler;
    private readonly EvaluateChallengeQueryHandler _evaluateHandler;
    private readonly OpenChallengeSolutionCommandHandler _solutionHandler;
    private readonly PreviewChallengeSolutionQueryHandler _previewHandler;

    public ChallengesController(
        ChallengeCatalog catalog,
        StartChallengeCommandHandler startHandler,
        EvaluateChallengeQueryHandler evaluateHandler,
        OpenChallengeSolutionCommandHandler solutionHandler,
        PreviewChallengeSolutionQueryHandler previewHandler)
    {
        _catalog = catalog;
        _startHandler = startHandler;
        _evaluateHandler = evaluateHandler;
        _solutionHandler = solutionHandler;
        _previewHandler = previewHandler;
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

    [HttpPost("{slug}/start")]
    [EnableRateLimiting(RateLimitPolicies.Mint)]
    public async Task<IActionResult> Start(string slug, [FromBody] NameRequest? request, CancellationToken cancellationToken)
    {
        var result = await _startHandler.Handle(new StartChallengeCommand { Slug = slug, Name = request?.Name }, cancellationToken);
        return result is null ? NotFound() : Created($"/api/architectures/edit/{result.EditToken}", result);
    }

    [HttpPost("{slug}/evaluate")]
    public async Task<IActionResult> Evaluate(string slug, [FromBody] EvaluateRequest request, CancellationToken cancellationToken)
    {
        var query = new EvaluateChallengeQuery { Slug = slug, ShareToken = request.ShareToken };
        var result = await _evaluateHandler.Handle(query, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{slug}/solution/preview")]
    public IActionResult SolutionPreview(string slug)
    {
        var result = _previewHandler.Handle(new PreviewChallengeSolutionQuery { Slug = slug });
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{slug}/solution")]
    [EnableRateLimiting(RateLimitPolicies.Mint)]
    public async Task<IActionResult> Solution(string slug, [FromBody] NameRequest? request, CancellationToken cancellationToken)
    {
        var result = await _solutionHandler.Handle(new OpenChallengeSolutionCommand { Slug = slug, Name = request?.Name }, cancellationToken);
        return result is null ? NotFound() : Created($"/api/architectures/{result.ShareToken}", result);
    }

    public class EvaluateRequest
    {
        public string ShareToken { get; set; } = string.Empty;
    }

    public class NameRequest
    {
        public string? Name { get; set; }
    }
}
