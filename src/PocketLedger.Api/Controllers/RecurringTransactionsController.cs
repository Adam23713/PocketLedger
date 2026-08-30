using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/recurring-transactions")]
public sealed class RecurringTransactionsController(IRecurringTransactionService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAll(CancellationToken token) => Ok(await service.GetAllAsync(token));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken token) => (await service.GetByIdAsync(id, token)) is { } item ? Ok(item) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(RecurringTransaction item, CancellationToken token) { var created = await service.CreateAsync(item, token); return CreatedAtAction(nameof(Get), new { id = created.Id }, created); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, RecurringTransaction item, CancellationToken token) { if (id != item.Id) return BadRequest(); await service.UpdateAsync(item, token); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await service.DeleteAsync(id, token); return NoContent(); }
}
