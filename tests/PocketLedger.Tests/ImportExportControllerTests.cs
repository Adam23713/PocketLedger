using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Controllers;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class ImportExportControllerTests
{
    [Fact]
    public async Task Backup_UsesUniqueSortableUtcPocketLedgerFileNames()
    {
        var controller = new ImportExportController(new StubImportExportService());

        var first = Assert.IsType<FileContentResult>(await controller.Backup(CancellationToken.None));
        var second = Assert.IsType<FileContentResult>(await controller.Backup(CancellationToken.None));

        Assert.NotEqual(first.FileDownloadName, second.FileDownloadName);
        Assert.Equal("application/json; charset=utf-8", first.ContentType);
        Assert.Matches("^pocketledger-backup-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{32}\\.json$", first.FileDownloadName);

        var timestamp = first.FileDownloadName!["pocketledger-backup-".Length..("pocketledger-backup-".Length + 19)];
        Assert.True(DateTimeOffset.TryParseExact(timestamp, "yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var generatedAt));
        Assert.InRange(generatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private sealed class StubImportExportService : IImportExportService
    {
        public Task<string> ExportCsvAsync(TransactionFilter filter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> ExportBackupAsync(CancellationToken cancellationToken) => Task.FromResult("{}");
        public RestorePreview PreviewRestore(string json) => throw new NotSupportedException();
        public Task RestoreAsync(string json, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
