using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Contracts;
using PocketLedger.Api;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/debts")]
public sealed class DebtsController(IDebtService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAll(CancellationToken token) => Ok((await service.GetAllAsync(token)).Select(ApiContractMapper.ToResponse));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken token) => (await service.GetByIdAsync(id, token)) is { } item ? Ok(item.ToResponse()) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(DebtWriteRequest request, CancellationToken token) { var created = await service.CreateAsync(request.Debt.ToEntity(), request.RecurringPayment, token); return CreatedAtAction(nameof(Get), new { id = created.Id }, created.ToDto()); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, DebtWriteRequest request, CancellationToken token) { if (id != request.Debt.Id) return BadRequest(); await service.UpdateAsync(request.Debt.ToEntity(), request.RecurringPayment, token); return NoContent(); }
    [HttpGet("{id:guid}/deletion-summary")] public async Task<IActionResult> GetDeletionSummary(Guid id, CancellationToken token) => Ok(await service.GetDeletionSummaryAsync(id, token));
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await service.DeleteAsync(id, token); return NoContent(); }
    [HttpGet("operations/{transactionId:guid}")] public async Task<IActionResult> GetOperation(Guid transactionId, CancellationToken token) => (await service.GetOperationAsync(transactionId, token)) is { } item ? Ok(item.ToDto()) : NotFound();
    [HttpPost("{id:guid}/operations")] public async Task<IActionResult> AddOperation(Guid id, DebtOperationWriteRequest request, CancellationToken token) => Ok((await service.AddOperationAsync(id, request.Operation, token)).ToDto());
    [HttpPut("operations/{transactionId:guid}")] public async Task<IActionResult> UpdateOperation(Guid transactionId, DebtOperationWriteRequest request, CancellationToken token) => Ok((await service.UpdateOperationAsync(transactionId, request.Operation, token)).ToDto());
    [HttpDelete("operations/{transactionId:guid}")] public async Task<IActionResult> DeleteOperation(Guid transactionId, CancellationToken token) { await service.DeleteOperationAsync(transactionId, token); return NoContent(); }
    [HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, CancellationToken token) { await service.CloseAsync(id, token); return NoContent(); }
    [HttpPost("{id:guid}/reopen")] public async Task<IActionResult> Reopen(Guid id, CancellationToken token) { await service.ReopenAsync(id, token); return NoContent(); }
    [HttpGet("funding-warnings")] public async Task<IActionResult> FundingWarnings([FromQuery] DateOnly today, CancellationToken token) => Ok(await service.GetFundingWarningsAsync(today, token));
}
