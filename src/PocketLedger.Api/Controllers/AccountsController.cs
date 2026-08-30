using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/accounts")]
public sealed class AccountsController(IAccountService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAll(CancellationToken token) => Ok(await service.GetAllAsync(token));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken token) => (await service.GetByIdAsync(id, token)) is { } item ? Ok(item) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(Account item, CancellationToken token) { var created = await service.CreateAsync(item, token); return CreatedAtAction(nameof(Get), new { id = created.Id }, created); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, AccountUpdateRequest request, CancellationToken token) { if (id != request.Account.Id) return BadRequest(); await service.UpdateAsync(request.Account, request.CreateInitialBalanceAdjustment, token); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await service.DeleteAsync(id, token); return NoContent(); }
    [HttpGet("{id:guid}/deletion-summary")] public async Task<IActionResult> DeletionSummary(Guid id, CancellationToken token) => Ok(await service.GetDeletionSummaryAsync(id, token));
    [HttpGet("{id:guid}/balance")] public async Task<IActionResult> Balance(Guid id, CancellationToken token) => Ok(await service.GetCurrentBalanceAsync(id, token));
    [HttpGet("balances")] public async Task<IActionResult> Balances(CancellationToken token) => Ok(await service.GetCurrentBalancesAsync(token));
    [HttpGet("choices")] public async Task<IActionResult> Choices(CancellationToken token) => Ok(await service.GetChoicesAsync(token));
    [HttpGet("{id:guid}/recent-transactions")] public async Task<IActionResult> RecentTransactions(Guid id, [FromQuery] int count = 10, CancellationToken token = default) => Ok(await service.GetRecentTransactionsAsync(id, count, token));
}
