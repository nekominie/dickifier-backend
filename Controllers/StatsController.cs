using backend_dickifier.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend_dickifier.Controllers;

[ApiController, Route("api/stats"), Route("api/dickifier/stats")]
public sealed class StatsController(StatisticsService statistics) : ControllerBase
{
    [HttpGet("global")]
    public async Task<IActionResult> Global(CancellationToken ct) => Ok(await statistics.Global(ct));

    [HttpGet("demographics")]
    public async Task<IActionResult> Demographics(CancellationToken ct) => Ok(await statistics.Demographics(ct));
}
