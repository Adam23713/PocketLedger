using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Contracts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/import-export")]
public sealed class ImportExportController(IImportExportService service, IEncryptedBackupService encryptedBackupService) : ControllerBase
{
    [HttpPost("excel/export"), RequestSizeLimit(BackupProtectionFormat.MaximumPasswordRequestBytes)]
    public async Task<IActionResult> ExportExcel(ExcelExportRequest request, CancellationToken token)
    {
        var content = await service.ExportExcelAsync(request.Filter, request.Password, token);
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
    [HttpPost("csv/preview")] public async Task<IActionResult> PreviewCsv(TextPayload request, CancellationToken token) => Ok(await service.PreviewCsvAsync(request.Content, token));
    [HttpPost("csv/import")] public async Task<IActionResult> ImportCsv(TextPayload request, CancellationToken token) => Ok(await service.ImportCsvAsync(request.Content, token));
    [HttpPost("backup/encrypted"), RequestSizeLimit(BackupProtectionFormat.MaximumPasswordRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupProtectionFormat.MaximumPasswordRequestBytes, ValueLengthLimit = BackupProtectionFormat.MaximumPasswordFormValueBytes)]
    public async Task<IActionResult> EncryptedBackup([FromForm] string password, CancellationToken token)
    {
        var content = await encryptedBackupService.ExportEncryptedBackupAsync(password, token);
        return File(content, "application/vnd.pocketledger.backup");
    }
    [HttpPost("restore/encrypted/preview"), RequestSizeLimit(BackupProtectionFormat.MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupProtectionFormat.MaximumUploadBytes)]
    public async Task<IActionResult> PreviewEncryptedRestore(IFormFile? file, [FromForm] string password, CancellationToken token)
    {
        var content = await ReadEncryptedBackupAsync(file, token);
        return Ok(encryptedBackupService.PreviewEncryptedRestore(content, password));
    }
    [HttpPost("restore/encrypted"), RequestSizeLimit(BackupProtectionFormat.MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupProtectionFormat.MaximumUploadBytes)]
    public async Task<IActionResult> RestoreEncrypted(IFormFile? file, [FromForm] string password, CancellationToken token)
    {
        var content = await ReadEncryptedBackupAsync(file, token);
        await encryptedBackupService.RestoreEncryptedAsync(content, password, token);
        return NoContent();
    }

    private static async Task<byte[]> ReadEncryptedBackupAsync(IFormFile? file, CancellationToken token)
    {
        if (file is null || file.Length == 0) throw new BusinessRuleException("Select an encrypted backup file.");
        if (file.Length > BackupProtectionFormat.MaximumFileBytes) throw new BusinessRuleException("The encrypted backup exceeds the 64 MiB limit.");
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, token);
        return buffer.ToArray();
    }
}
