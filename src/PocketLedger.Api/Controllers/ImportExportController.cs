using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Contracts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/import-export")]
public sealed class ImportExportController(IImportExportService service) : ControllerBase
{
    [HttpPost("csv/export")] public async Task<IActionResult> ExportCsv(TransactionFilter filter, CancellationToken token) => Ok(new TextPayload(await service.ExportCsvAsync(filter, token)));
    [HttpPost("csv/preview")] public async Task<IActionResult> PreviewCsv(TextPayload request, CancellationToken token) => Ok(await service.PreviewCsvAsync(request.Content, token));
    [HttpPost("csv/import")] public async Task<IActionResult> ImportCsv(TextPayload request, CancellationToken token) => Ok(await service.ImportCsvAsync(request.Content, token));
    [HttpGet("backup")] public async Task<IActionResult> Backup(CancellationToken token) => Ok(new TextPayload(await service.ExportBackupAsync(token)));
    [HttpPost("restore/preview")] public IActionResult PreviewRestore(TextPayload request) => Ok(service.PreviewRestore(request.Content));
    [HttpPost("restore")] public async Task<IActionResult> Restore(TextPayload request, CancellationToken token) { await service.RestoreAsync(request.Content, token); return NoContent(); }
}
