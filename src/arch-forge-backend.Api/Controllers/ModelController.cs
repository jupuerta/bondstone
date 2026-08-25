using arch_forge_backend.Application.Simulation;
using Microsoft.AspNetCore.Mvc;

namespace arch_forge_backend.Api.Controllers;

/// <summary>
/// V16 C2. One endpoint, one verb, and the whole model behind it.
///
/// The figures it serves are the ones every score of Escalabilidad is computed from — a Database
/// takes 200 rps and a Gateway 2 000, an Sql hop costs 2 ms and an Http one 5, saturation starts at
/// 80 % — and before this they lived only in two C# files. That is the hole the veredicto falls
/// into: <c>UNDERSIZED_COMPONENT</c> is a judgement about a threshold the reader was never shown,
/// so they learn that something was wrong without learning <em>which number</em> was wrong, and
/// cannot see the next one coming.
///
/// It takes no dependencies and touches no architecture: it is not analysis, it is the criterion
/// analysis uses, which is why it is a controller of its own beside the two that already exist
/// rather than a field on somebody's result. Nothing here is copied — see
/// <see cref="ModelReference"/>, which asks <see cref="CapacityModel"/> and
/// <see cref="LatencyModel"/> for every value it publishes.
/// </summary>
[ApiController]
[Route("api/model")]
public class ModelController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(ModelReference.Snapshot);
}
