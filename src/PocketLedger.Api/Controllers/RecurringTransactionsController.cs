using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Api;
using PocketLedger.Contracts;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/recurring-transactions")]
public sealed class RecurringTransactionsController(IRecurringTransactionService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAll(CancellationToken token) => Ok((await service.GetAllAsync(token)).Select(ApiContractMapper.ToDto));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken token) => (await service.GetByIdAsync(id, token)) is { } item ? Ok(item.ToDto()) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(RecurringTransactionDto item, CancellationToken token) { var created = await service.CreateAsync(item.ToEntity(), token); return CreatedAtAction(nameof(Get), new { id = created.Id }, created.ToDto()); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, RecurringTransactionDto item, CancellationToken token) { if (id != item.Id) return BadRequest(); await service.UpdateAsync(item.ToEntity(), token); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await service.DeleteAsync(id, token); return NoContent(); }
}
