using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/statistics")]
public sealed class StatisticsController(IStatisticsService service) : ControllerBase
{
    [HttpGet("currencies")] public async Task<IActionResult> Currencies([FromQuery] int year, [FromQuery] int month, CancellationToken token) => Ok(await service.GetAvailableCurrenciesAsync(year, month, token));
    [HttpGet("summary")] public async Task<IActionResult> Summary([FromQuery] int year, [FromQuery] int month, [FromQuery] string currency, CancellationToken token) => Ok(await service.GetSummaryAsync(year, month, currency, token));
}
