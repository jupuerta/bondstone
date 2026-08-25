using arch_forge_backend.Application.Simulation;
using Microsoft.AspNetCore.Mvc;

namespace arch_forge_backend.Api.Controllers;

[ApiController]
[Route("api/model")]
public class ModelController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(ModelReference.Snapshot);
}
