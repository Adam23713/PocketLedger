using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.ImportExport;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class ImportExportController(IImportExportService importExportService, IUserContextService userContext, IEncryptedBackupService? encryptedBackupService = null) : Controller
{
    public IActionResult Index() => View(new ImportExportIndexViewModel());

    [HttpGet]
    public IActionResult ExportExcel(DateOnly? dateFrom, DateOnly? dateTo, int? year, int? month, Guid? accountId, Guid? categoryId, TransactionType? type, decimal? amountFrom, decimal? amountTo, string? search)
    {
        return View(new ExcelExportViewModel { Filter = new TransactionFilter { DateFrom = dateFrom, DateTo = dateTo, Year = year, Month = month, AccountId = accountId, CategoryId = categoryId, Type = type, AmountFrom = amountFrom, AmountTo = amountTo, Search = search } });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(BackupProtectionFormat.MaximumPasswordRequestBytes)]
    [RequestFormLimits(ValueLengthLimit = BackupProtectionFormat.MaximumPasswordFormValueBytes)]
    public async Task<IActionResult> ExportExcel(ExcelExportViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var content = await importExportService.ExportExcelAsync(model.Filter, model.Password, cancellationToken);
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"transactions-{await userContext.TodayAsync(cancellationToken):yyyyMMdd}.xlsx");
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult Import() => View(new CsvImportViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewImport(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Select a CSV file.");
            return View("Import", new CsvImportViewModel());
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var csv = await reader.ReadToEndAsync(cancellationToken);
        return View("Import", new CsvImportViewModel { Csv = csv, Preview = await importExportService.PreviewCsvAsync(csv, cancellationToken) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmImport(CsvImportViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Csv)) return BadRequest();
        var result = await importExportService.ImportCsvAsync(model.Csv, cancellationToken);
        TempData["SuccessMessage"] = $"Imported {result.ImportedCount} rows; skipped {result.DuplicateCount} duplicates and {result.InvalidCount} invalid rows.";
        return RedirectToAction(nameof(Import));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(BackupProtectionFormat.MaximumPasswordRequestBytes)]
    [RequestFormLimits(ValueLengthLimit = BackupProtectionFormat.MaximumPasswordFormValueBytes)]
    public async Task<IActionResult> EncryptedBackup(ImportExportIndexViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Index", model);
        try
        {
            var content = await EncryptedBackups().ExportEncryptedBackupAsync(model.EncryptedBackup.Password, cancellationToken);
            var fileName = $"pocketledger-{DateTimeOffset.UtcNow:yyyy-MM-dd}.plbackup";
            return File(content, "application/vnd.pocketledger.backup", fileName);
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("Index", model);
        }
    }

    [HttpGet]
    public IActionResult Restore() => View(new RestoreViewModel());

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(BackupProtectionFormat.MaximumUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupProtectionFormat.MaximumUploadBytes)]
    public async Task<IActionResult> PreviewRestore(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Select a .plbackup file.");
            return View("Restore", new RestoreViewModel());
        }

        if (file.Length > BackupProtectionFormat.MaximumFileBytes)
        {
            ModelState.AddModelError(string.Empty, "The backup exceeds the 64 MiB limit.");
            return View("Restore", new RestoreViewModel());
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();
        if (!BackupProtectionFormat.HasMagic(content))
        {
            ModelState.AddModelError(string.Empty, "The selected file is not a PocketLedger encrypted backup.");
            return View("Restore", new RestoreViewModel());
        }

        var password = Request.Form["Password"].ToString();
        if (string.IsNullOrEmpty(password))
        {
            ModelState.AddModelError(nameof(RestoreViewModel.Password), "Backup password is required.");
            return View("Restore", new RestoreViewModel());
        }

        var preview = EncryptedBackups().PreviewEncryptedRestore(content, password);
        ModelState.Remove(nameof(RestoreViewModel.Password));
        return View("Restore", new RestoreViewModel { EncryptedContent = preview.IsValid ? Convert.ToBase64String(content) : string.Empty, Preview = preview });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(BackupProtectionFormat.MaximumFormBytes)]
    [RequestFormLimits(ValueLengthLimit = BackupProtectionFormat.MaximumFormBytes)]
    public async Task<IActionResult> ConfirmRestore(RestoreViewModel model, CancellationToken cancellationToken)
    {
        if (!model.Confirm)
        {
            ModelState.AddModelError(nameof(model.Confirm), "Explicit confirmation is required.");
            model.Preview = Preview(model);
            return View("Restore", model);
        }

        try
        {
            if (string.IsNullOrEmpty(model.Password)) throw new BusinessRuleException("Backup password is required.");
            await EncryptedBackups().RestoreEncryptedAsync(Convert.FromBase64String(model.EncryptedContent), model.Password, cancellationToken);
            TempData["SuccessMessage"] = "Backup restored successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception) when (exception is BusinessRuleException or FormatException)
        {
            ModelState.AddModelError(string.Empty, exception is FormatException ? "The encrypted backup data is invalid." : exception.Message);
            model.Preview = Preview(model);
            ModelState.Remove(nameof(RestoreViewModel.Password));
            model.Password = string.Empty;
            return View("Restore", model);
        }
    }

    private RestorePreview Preview(RestoreViewModel model)
    {
        if (string.IsNullOrEmpty(model.Password)) return model.Preview ?? new RestorePreview(false, 0, 0, 0, 0, ["Backup password is required."]);
        try { return EncryptedBackups().PreviewEncryptedRestore(Convert.FromBase64String(model.EncryptedContent), model.Password); }
        catch (FormatException) { return new RestorePreview(false, 0, 0, 0, 0, ["The encrypted backup data is invalid."]); }
    }

    private IEncryptedBackupService EncryptedBackups() => encryptedBackupService ?? throw new InvalidOperationException("Encrypted backup service is not configured.");
}
