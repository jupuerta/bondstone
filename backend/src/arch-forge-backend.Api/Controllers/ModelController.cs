using bondstone_backend.Application.Simulation;
using Microsoft.AspNetCore.Mvc;

namespace bondstone_backend.Api.Controllers;

[ApiController]
[Route("api/model")]
public class ModelController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(ModelReference.Snapshot);
}
