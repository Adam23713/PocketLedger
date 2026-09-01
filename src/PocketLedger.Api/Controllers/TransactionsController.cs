using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Api;
using PocketLedger.Contracts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/transactions")]
public sealed class TransactionsController(ITransactionService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Filtered([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, [FromQuery] int? year, [FromQuery] int? month, [FromQuery] Guid? accountId, [FromQuery] Guid? categoryId, [FromQuery] PocketLedger.Models.Enums.TransactionType? type, [FromQuery] decimal? amountFrom, [FromQuery] decimal? amountTo, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken token = default) => Ok((await service.GetFilteredAsync(new TransactionFilter { DateFrom = dateFrom, DateTo = dateTo, Year = year, Month = month, AccountId = accountId, CategoryId = categoryId, Type = type, AmountFrom = amountFrom, AmountTo = amountTo, Search = search, Page = page, PageSize = pageSize }, token)).ToResponse());
    [HttpGet("month")] public async Task<IActionResult> Month([FromQuery] int year, [FromQuery] int month, CancellationToken token) => Ok((await service.GetForMonthAsync(year, month, token)).Select(ApiContractMapper.ToDto));
    [HttpGet("recent")] public async Task<IActionResult> Recent([FromQuery] int count = 10, CancellationToken token = default) => Ok((await service.GetRecentAsync(count, token)).Select(ApiContractMapper.ToDto));
    [HttpPost("daily-totals")] public async Task<IActionResult> DailyTotals(TransactionFilter filter, CancellationToken token) => Ok(await service.GetDailyTotalsAsync(filter, token));
    [HttpPost("export-query")] public async Task<IActionResult> ExportQuery(TransactionFilter filter, CancellationToken token) => Ok((await service.GetForExportAsync(filter, token)).Select(ApiContractMapper.ToDto));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken token) => (await service.GetByIdAsync(id, token)) is { } item ? Ok(item.ToDto()) : NotFound();
    [HttpPost] public async Task<IActionResult> Create(TransactionDto item, CancellationToken token) { var created = await service.CreateAsync(item.ToEntity(), token); return CreatedAtAction(nameof(Get), new { id = created.Id }, created.ToDto()); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, TransactionDto item, CancellationToken token) { if (id != item.Id) return BadRequest(); await service.UpdateAsync(item.ToEntity(), token); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken token) { await service.DeleteAsync(id, token); return NoContent(); }
    [HttpGet("balances")] public async Task<IActionResult> Balances(CancellationToken token) => Ok(await service.CalculateAccountBalancesAsync(token));
    [HttpGet("main-balance")] public async Task<ActionResult<IReadOnlyList<CurrencyBalanceDto>>> MainBalance(CancellationToken token) => Ok((await service.CalculateMainBalanceAsync(token)).Select(ApiContractMapper.ToDto).ToArray());
}
