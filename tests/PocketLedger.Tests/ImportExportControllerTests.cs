using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Controllers;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.ImportExport;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class ImportExportControllerTests
{
    [Fact]
    public void Index_PreservesTransactionFiltersForExcelExport()
    {
        var accountId = Guid.NewGuid();
        var controller = new ImportExportController(new StubImportExportService(), new FixedUserContext(default));

        var result = Assert.IsType<ViewResult>(controller.Index(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 2026, 9, accountId, null, TransactionType.Expense, 10, 200, "food"));
        var model = Assert.IsType<ImportExportIndexViewModel>(result.Model);

        Assert.Equal(new DateOnly(2026, 9, 1), model.ExcelExport.Filter.DateFrom);
        Assert.Equal(accountId, model.ExcelExport.Filter.AccountId);
        Assert.Equal(TransactionType.Expense, model.ExcelExport.Filter.Type);
        Assert.Equal("food", model.ExcelExport.Filter.Search);
    }

    [Fact]
    public async Task ExportExcel_UsesUsersLocalDateInFileNameWhenUtcDateDiffers()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero));
        var userDates = new UserDateProvider(clock);
        var controller = new ImportExportController(new StubImportExportService(), new FixedUserContext(userDates.Today("Pacific/Kiritimati")));
        var model = new ExcelExportViewModel { Password = "0123456789", ConfirmPassword = "0123456789" };

        var result = Assert.IsType<FileContentResult>(await controller.ExportExcel(model, CancellationToken.None));

        Assert.Equal("transactions-20260815.xlsx", result.FileDownloadName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.ContentType);
    }

    [Fact]
    public async Task EncryptedBackup_ReturnsDedicatedBinaryFormatAndFileName()
    {
        var encrypted = new StubEncryptedBackupService();
        var controller = new ImportExportController(new StubImportExportService(), new FixedUserContext(default), encrypted);
        var model = new EncryptedBackupExportViewModel { Password = "0123456789", ConfirmPassword = "0123456789" };

        var result = Assert.IsType<FileContentResult>(await controller.EncryptedBackup(model, CancellationToken.None));

        Assert.Equal("0123456789", encrypted.Password);
        Assert.Equal("application/vnd.pocketledger.backup", result.ContentType);
        Assert.Equal("PLBACKUP"u8.ToArray(), result.FileContents);
        Assert.Matches("^pocketledger-[0-9]{8}T[0-9]{9}Z\\.plbackup$", result.FileDownloadName);
    }

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("0123456789", "different-password")]
    public void EncryptedBackupModel_RejectsShortOrMismatchedPasswords(string password, string confirmation)
    {
        var model = new EncryptedBackupExportViewModel { Password = password, ConfirmPassword = confirmation };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
    }

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("0123456789", "different-password")]
    public void ExcelExportModel_RejectsShortOrMismatchedPasswords(string password, string confirmation)
    {
        var model = new ExcelExportViewModel { Password = password, ConfirmPassword = confirmation };
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
    }

    private sealed class StubImportExportService : IImportExportService
    {
        public Task<byte[]> ExportExcelAsync(TransactionFilter filter, string password, CancellationToken cancellationToken) => Task.FromResult(new byte[] { 1, 2, 3 });
        public Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubEncryptedBackupService : IEncryptedBackupService
    {
        public string? Password { get; private set; }
        public Task<byte[]> ExportEncryptedBackupAsync(string password, CancellationToken cancellationToken) { Password = password; return Task.FromResult("PLBACKUP"u8.ToArray()); }
        public RestorePreview PreviewEncryptedRestore(byte[] encryptedBackup, string password) => throw new NotSupportedException();
        public Task RestoreEncryptedAsync(byte[] encryptedBackup, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
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
