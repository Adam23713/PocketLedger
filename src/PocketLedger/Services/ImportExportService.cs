using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class ImportExportService(PocketLedgerDbContext dbContext, ITransactionService transactionService) : IImportExportService
{
    public async Task<string> ExportCsvAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        var transactions = await transactionService.GetForExportAsync(filter, cancellationToken);
        var builder = new StringBuilder("date,account,type,category,amount,currency,note\n");
        foreach (var transaction in transactions)
        {
            builder.Append(transaction.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvParser.Escape(transaction.Account.Name)).Append(',')
                .Append(transaction.Type).Append(',')
                .Append(CsvParser.Escape(transaction.Category?.Name)).Append(',')
                .Append(transaction.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvParser.Escape(transaction.Account.Currency)).Append(',')
                .Append(CsvParser.Escape(transaction.Note)).Append('\n');
        }

        return builder.ToString();
    }

    public async Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var categories = await dbContext.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var existing = await dbContext.Transactions.AsNoTracking().Select(transaction => new { transaction.TransactionDate, transaction.AccountId, transaction.Type, transaction.CategoryId, transaction.Amount, transaction.Note }).ToListAsync(cancellationToken);
        return ParseImport(csv, accounts, categories, existing.Select(item => ImportKey(item.TransactionDate, item.AccountId, item.Type, item.CategoryId, item.Amount, item.Note)).ToHashSet());
    }

    public async Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var categories = await dbContext.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var existing = await dbContext.Transactions.AsNoTracking().Select(transaction => new { transaction.TransactionDate, transaction.AccountId, transaction.Type, transaction.CategoryId, transaction.Amount, transaction.Note }).ToListAsync(cancellationToken);
        var keys = existing.Select(item => ImportKey(item.TransactionDate, item.AccountId, item.Type, item.CategoryId, item.Amount, item.Note)).ToHashSet();
        var preview = ParseImport(csv, accounts, categories, keys);
        var accountLookup = accounts.GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var categoryLookup = categories.GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in preview.Rows.Where(row => row.IsValid && !row.IsDuplicate))
        {
            var account = accountLookup[row.Account];
            var category = string.IsNullOrWhiteSpace(row.Category) ? null : categoryLookup[row.Category];
            dbContext.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                Type = row.Type!.Value,
                AccountId = account.Id,
                Amount = row.Amount!.Value,
                TransactionDate = row.Date!.Value,
                CategoryId = category?.Id,
                Note = string.IsNullOrWhiteSpace(row.Note) ? null : row.Note.Trim()
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CsvImportResult(preview.ValidCount, preview.InvalidCount, preview.DuplicateCount);
    }

    public async Task<string> ExportBackupAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().Select(account => new AccountBackup(account.Id, account.Name, account.Type, account.Currency, account.InitialBalance, account.Icon, account.DisplayOrder, account.IncludeInMainBalance, account.IncludeInNetWorth, account.IncludeInStatistics, account.Color)).ToListAsync(cancellationToken);
        var categories = await dbContext.Categories.AsNoTracking().Select(category => new CategoryBackup(category.Id, category.Name, category.Type, category.Icon, category.ParentCategoryId, category.DisplayOrder)).ToListAsync(cancellationToken);
        var transactions = await dbContext.Transactions.AsNoTracking().Select(transaction => new TransactionBackup(transaction.Id, transaction.Type, transaction.AccountId, transaction.TargetAccountId, transaction.Amount, transaction.TargetAmount, transaction.AdjustmentDirection, transaction.TransactionDate, transaction.CategoryId, transaction.Note, transaction.TransactionTime)).ToListAsync(cancellationToken);
        var recurring = await dbContext.RecurringTransactions.AsNoTracking().Select(template => new RecurringTransactionBackup(template.Id, template.Type, template.AccountId, template.CategoryId, template.Amount, template.AdjustmentDirection, template.Note, template.FirstOccurrence, template.LastOccurrence, template.Frequency, template.Enabled)).ToListAsync(cancellationToken);
        return BackupJson.Serialize(new PocketLedgerBackup(1, DateTimeOffset.UtcNow, accounts, categories, transactions, recurring));
    }

    public RestorePreview PreviewRestore(string json)
    {
        try
        {
            var backup = DeserializeBackup(json);
            var errors = BackupValidator.Validate(backup);
            return new RestorePreview(errors.Count == 0, backup.Accounts.Count, backup.Categories.Count, backup.Transactions.Count, backup.RecurringTransactions.Count, errors);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or BusinessRuleException)
        {
            return new RestorePreview(false, 0, 0, 0, 0, [exception.Message]);
        }
    }

    public async Task RestoreAsync(string json, CancellationToken cancellationToken)
    {
        var backup = DeserializeBackup(json);
        var errors = BackupValidator.Validate(backup);
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            dbContext.Transactions.RemoveRange(dbContext.Transactions);
            dbContext.RecurringTransactions.RemoveRange(dbContext.RecurringTransactions);
            dbContext.Categories.RemoveRange(dbContext.Categories);
            dbContext.Accounts.RemoveRange(dbContext.Accounts);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            dbContext.Accounts.AddRange(backup.Accounts.Select(item => new Account { Id = item.Id, Name = item.Name, Type = item.Type, Currency = item.Currency, InitialBalance = item.InitialBalance, Icon = item.Icon, Color = item.Color ?? "#ffffff", DisplayOrder = item.DisplayOrder, IncludeInMainBalance = item.IncludeInMainBalance, IncludeInNetWorth = item.IncludeInNetWorth, IncludeInStatistics = item.IncludeInStatistics }));
            dbContext.Categories.AddRange(backup.Categories.Select(item => new Category { Id = item.Id, Name = item.Name, Type = item.Type, Icon = item.Icon, ParentCategoryId = item.ParentCategoryId, DisplayOrder = item.DisplayOrder }));
            dbContext.Transactions.AddRange(backup.Transactions.Select(item => new Transaction { Id = item.Id, Type = item.Type, AccountId = item.AccountId, TargetAccountId = item.TargetAccountId, Amount = item.Amount, TargetAmount = item.TargetAmount, AdjustmentDirection = item.AdjustmentDirection, TransactionDate = item.TransactionDate, TransactionTime = item.TransactionTime, CategoryId = item.CategoryId, Note = item.Note }));
            dbContext.RecurringTransactions.AddRange(backup.RecurringTransactions.Select(item => new RecurringTransaction { Id = item.Id, Type = item.Type, AccountId = item.AccountId, CategoryId = item.CategoryId, Amount = item.Amount, AdjustmentDirection = item.AdjustmentDirection, Note = item.Note, FirstOccurrence = item.FirstOccurrence, LastOccurrence = item.LastOccurrence, Frequency = item.Frequency, Enabled = item.Enabled }));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static CsvImportPreview ParseImport(string csv, IReadOnlyList<Account> accounts, IReadOnlyList<Category> categories, HashSet<string> existingKeys)
    {
        var parsed = CsvParser.Parse(csv);
        if (parsed.Count == 0) return new CsvImportPreview([]);
        var accountsByName = accounts.GroupBy(account => account.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var categoriesByName = categories.GroupBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<CsvImportRow>();
        foreach (var item in parsed.Skip(1).Select((values, index) => (Values: values, RowNumber: index + 2)))
        {
            var values = item.Values;
            if (values.Count != 7)
            {
                rows.Add(new CsvImportRow(item.RowNumber, false, false, "Expected 7 columns.", null, string.Empty, null, null, null, string.Empty, null));
                continue;
            }

            DateOnly? date = DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ? parsedDate : null;
            TransactionType? type = Enum.TryParse<TransactionType>(values[2], true, out var parsedType) ? parsedType : null;
            decimal? amount = decimal.TryParse(values[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount) ? parsedAmount : null;
            accountsByName.TryGetValue(values[1], out var account);
            Category? category = null;
            if (!string.IsNullOrWhiteSpace(values[3])) categoriesByName.TryGetValue(values[3], out category);
            var error = ValidateCsvRow(date, type, amount, account, category, values[3], values[5]);
            var duplicate = error is null && existingKeys.Contains(ImportKey(date!.Value, account!.Id, type!.Value, category?.Id, amount!.Value, values[6]));
            if (error is null && !duplicate) existingKeys.Add(ImportKey(date!.Value, account!.Id, type!.Value, category?.Id, amount!.Value, values[6]));
            rows.Add(new CsvImportRow(item.RowNumber, error is null, duplicate, error, date, values[1], type, values[3], amount, values[5], values[6]));
        }

        return new CsvImportPreview(rows);
    }

    private static string? ValidateCsvRow(DateOnly? date, TransactionType? type, decimal? amount, Account? account, Category? category, string categoryName, string currency)
    {
        if (date is null) return "Invalid date.";
        if (account is null) return "Account not found.";
        if (!string.Equals(account.Currency, currency, StringComparison.OrdinalIgnoreCase)) return "Currency does not match the account.";
        if (type is not (TransactionType.Income or TransactionType.Expense)) return "Only income and expense CSV rows are supported.";
        if (amount is null or <= 0) return "Amount must be greater than zero.";
        if (category is null) return string.IsNullOrWhiteSpace(categoryName) ? "Category is required." : "Category not found.";
        var expected = type == TransactionType.Income ? CategoryType.Income : CategoryType.Expense;
        return category.Type == expected ? null : "Category type does not match transaction type.";
    }

    private static string ImportKey(DateOnly date, Guid accountId, TransactionType type, Guid? categoryId, decimal amount, string? note) => $"{date:yyyy-MM-dd}|{accountId}|{type}|{categoryId}|{amount.ToString("G29", CultureInfo.InvariantCulture)}|{note?.Trim()}";

    private static PocketLedgerBackup DeserializeBackup(string json)
    {
        return BackupJson.Deserialize(json);
    }

}
