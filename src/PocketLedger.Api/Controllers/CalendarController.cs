using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/calendar")]
public sealed class CalendarController(ICalendarService service) : ControllerBase
{
    [HttpGet("{year:int}/{month:int}")] public async Task<IActionResult> Month(int year, int month, CancellationToken token) => Ok(await service.GetMonthAsync(year, month, token));
}
