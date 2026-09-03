using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Controllers;
using PocketLedger.Models.Entities;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class ImportExportControllerTests
{
    [Fact]
    public async Task Backup_UsesUniqueSortableUtcPocketLedgerFileNames()
    {
        var controller = new ImportExportController(new StubImportExportService(), new FixedUserContext(new DateOnly(2026, 1, 1)));

        var first = Assert.IsType<FileContentResult>(await controller.Backup(CancellationToken.None));
        var second = Assert.IsType<FileContentResult>(await controller.Backup(CancellationToken.None));

        Assert.NotEqual(first.FileDownloadName, second.FileDownloadName);
        Assert.Equal("application/json; charset=utf-8", first.ContentType);
        Assert.Matches("^pocketledger-backup-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{32}\\.json$", first.FileDownloadName);

        var timestamp = first.FileDownloadName!["pocketledger-backup-".Length..("pocketledger-backup-".Length + 19)];
        Assert.True(DateTimeOffset.TryParseExact(timestamp, "yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var generatedAt));
        Assert.InRange(generatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task ExportCsv_UsesUsersLocalDateInFileNameWhenUtcDateDiffers()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero));
        var userDates = new UserDateProvider(clock);
        var controller = new ImportExportController(new StubImportExportService(), new FixedUserContext(userDates.Today("Pacific/Kiritimati")));

        var result = Assert.IsType<FileContentResult>(await controller.ExportCsv(null, null, null, null, null, null, null, null, null, null, CancellationToken.None));

        Assert.Equal("transactions-20260815.csv", result.FileDownloadName);
    }

    private sealed class StubImportExportService : IImportExportService
    {
        public Task<string> ExportCsvAsync(TransactionFilter filter, CancellationToken cancellationToken) => Task.FromResult("date,account,type,category,amount,currency,note\n");
        public Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> ExportBackupAsync(CancellationToken cancellationToken) => Task.FromResult("{}");
        public RestorePreview PreviewRestore(string json) => throw new NotSupportedException();
        public Task RestoreAsync(string json, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedUserContext(DateOnly today) : IUserContextService
    {
        public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(today);
        public Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string Format(decimal amount, string? currency) => throw new NotSupportedException();
        public string FormatNumber(decimal amount, string? currency) => throw new NotSupportedException();
        public MoneyInputFormat GetMoneyInputFormat(string currency) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
