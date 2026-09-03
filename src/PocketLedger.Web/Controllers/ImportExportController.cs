using System.Text;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.ImportExport;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class ImportExportController(IImportExportService importExportService, IUserContextService userContext) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> ExportCsv(DateOnly? dateFrom, DateOnly? dateTo, int? year, int? month, Guid? accountId, Guid? categoryId, TransactionType? type, decimal? amountFrom, decimal? amountTo, string? search, CancellationToken cancellationToken)
    {
        var csv = await importExportService.ExportCsvAsync(new TransactionFilter { DateFrom = dateFrom, DateTo = dateTo, Year = year, Month = month, AccountId = accountId, CategoryId = categoryId, Type = type, AmountFrom = amountFrom, AmountTo = amountTo, Search = search }, cancellationToken);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"transactions-{await userContext.TodayAsync(cancellationToken):yyyyMMdd}.csv");
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

    [HttpGet]
    public async Task<IActionResult> Backup(CancellationToken cancellationToken)
    {
        var json = await importExportService.ExportBackupAsync(cancellationToken);
        var fileName = $"pocketledger-backup-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.json";
        return File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", fileName);
    }

    [HttpGet]
    public IActionResult Restore() => View(new RestoreViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewRestore(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Select a JSON backup file.");
            return View("Restore", new RestoreViewModel());
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var json = await reader.ReadToEndAsync(cancellationToken);
        return View("Restore", new RestoreViewModel { Json = json, Preview = importExportService.PreviewRestore(json) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmRestore(RestoreViewModel model, CancellationToken cancellationToken)
    {
        if (!model.Confirm)
        {
            ModelState.AddModelError(nameof(model.Confirm), "Explicit confirmation is required.");
            model.Preview = importExportService.PreviewRestore(model.Json);
            return View("Restore", model);
        }

        try
        {
            await importExportService.RestoreAsync(model.Json, cancellationToken);
            TempData["SuccessMessage"] = "Backup restored successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            model.Preview = importExportService.PreviewRestore(model.Json);
            return View("Restore", model);
        }
    }
}
