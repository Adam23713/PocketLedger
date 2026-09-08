using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/planner")]
public sealed class PlannerController(IPlannerService service) : ControllerBase
{
    [HttpGet("{year:int}/{month:int}")] public async Task<IActionResult> Month(int year, int month, CancellationToken token) => Ok(await service.GetMonthAsync(year, month, token));
    [HttpGet("items/{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken token) => await service.GetByIdAsync(id, token) is { } item ? Ok(item) : NotFound();
    [HttpPost("items")] public async Task<IActionResult> Create(PlannerItemInput item, CancellationToken token) { var id = await service.CreateAsync(item, token); return CreatedAtAction(nameof(Get), new { id }, id); }
    [HttpPut("items/{id:guid}")] public async Task<IActionResult> Update(Guid id, PlannerItemInput item, CancellationToken token) { await service.UpdateAsync(id, item, token); return NoContent(); }
    [HttpDelete("items/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await service.DeleteAsync(id, token); return NoContent(); }
}
