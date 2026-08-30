using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Account;
using PocketLedger.Models.ViewModels.Debts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class DebtAndSettingsRegressionTests
{
    [Theory]
    [InlineData(nameof(DebtFormViewModel.AutomaticPaymentAmount), "Automatic payment amount")]
    [InlineData(nameof(DebtFormViewModel.NextPaymentDate), "Next payment date")]
    [InlineData(nameof(DebtFormViewModel.LastPaymentDate), "Last payment date")]
    public void DebtForm_AutomaticPaymentFieldsHaveReadableLabels(string propertyName, string expectedLabel)
    {
        var property = typeof(DebtFormViewModel).GetProperty(propertyName)!;

        Assert.Equal(expectedLabel, property.GetCustomAttributes(typeof(DisplayAttribute), true).Cast<DisplayAttribute>().Single().Name);
    }

    [Fact]
    public void Settings_AcceptsLiteralSpaceAsThousandsSeparator()
    {
        var model = new SettingsViewModel
        {
            DisplayName = "Ada",
            DefaultCurrency = "HUF",
            TimeZoneId = "Europe/Budapest",
            CurrencyFormats =
            [
                new CurrencyFormatViewModel { CurrencyCode = "HUF", DecimalPlaces = 0, DecimalSeparator = ",", ThousandsSeparator = " " }
            ]
        };
        var results = new List<ValidationResult>();
        var currencyResults = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), results, true);
        var currencyValid = Validator.TryValidateObject(model.CurrencyFormats[0], new ValidationContext(model.CurrencyFormats[0]), currencyResults, true);

        Assert.True(valid, string.Join(Environment.NewLine, results.Select(result => result.ErrorMessage)));
        Assert.True(currencyValid, string.Join(Environment.NewLine, currencyResults.Select(result => result.ErrorMessage)));
    }

    [Fact]
    public async Task SettingsProfile_RoundTripsDisplayNameAndLiteralSpaceSeparator()
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var userId = Guid.NewGuid();
        await using (var db = new PocketLedgerDbContext(options))
        {
            db.UserPreferences.Add(new UserPreference
            {
                UserId = userId,
                DisplayName = "Ada Lovelace",
                CurrencyFormats = [new UserCurrencyFormat { UserId = userId, CurrencyCode = "HUF", DecimalPlaces = 0, DecimalSeparator = ",", ThousandsSeparator = " " }]
            });
            await db.SaveChangesAsync();
        }

        await using var verify = new PocketLedgerDbContext(options);
        var saved = await verify.UserPreferences.Include(user => user.CurrencyFormats).SingleAsync(user => user.UserId == userId);
        Assert.Equal("Ada Lovelace", saved.DisplayName);
        Assert.Equal(" ", Assert.Single(saved.CurrencyFormats).ThousandsSeparator);
    }

    [Fact]
    public async Task DebtUpdate_PersistsEditableFieldsAndAutomaticPaymentSchedule()
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ownerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var debtId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        db.Accounts.Add(new Account { Id = accountId, Name = "Bank", Type = AccountType.BankAccount, Currency = "HUF" });
        db.Debts.Add(new Debt
        {
            Id = debtId, Name = "Old name", Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id, Direction = DebtDirection.Payable,
            Type = DebtType.PrivatePerson, CounterpartyName = "Old counterparty", OriginalAmount = 1000, Currency = "HUF", StartDate = new DateOnly(2026, 1, 1), AccountId = accountId
        });
        db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = Guid.NewGuid(), DebtId = debtId, AccountId = accountId, Type = TransactionType.Expense, Amount = 100,
            FirstOccurrence = new DateOnly(2026, 9, 1), Frequency = RecurringFrequency.Monthly, Enabled = true, DebtOperationType = DebtOperationType.Payment
        });
        await db.SaveChangesAsync();
        var service = new DebtService(db, TimeProvider.System);

        await service.UpdateAsync(new Debt
        {
            Id = debtId, Name = "Updated name", Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id, Direction = DebtDirection.Payable,
            Type = DebtType.PrivatePerson, CounterpartyName = "Updated counterparty", OriginalAmount = 1200, Currency = "HUF",
            StartDate = new DateOnly(2026, 2, 1), DueDate = new DateOnly(2027, 2, 1), Note = "Updated note", AccountId = accountId
        }, new RecurringPaymentInput(accountId, 150, new DateOnly(2026, 10, 1), new DateOnly(2027, 5, 1), RecurringFrequency.Monthly, true), CancellationToken.None);

        db.ChangeTracker.Clear();
        var saved = await db.Debts.Include(item => item.RecurringTransactions).SingleAsync(item => item.Id == debtId);
        Assert.Equal("Updated name", saved.Name);
        Assert.Equal("Updated counterparty", saved.CounterpartyName);
        Assert.Equal(1200, saved.OriginalAmount);
        Assert.Equal(new DateOnly(2026, 2, 1), saved.StartDate);
        Assert.Equal(new DateOnly(2027, 2, 1), saved.DueDate);
        Assert.Equal("Updated note", saved.Note);
        var schedule = Assert.Single(saved.RecurringTransactions);
        Assert.Equal(150, schedule.Amount);
        Assert.Equal(new DateOnly(2026, 10, 1), schedule.FirstOccurrence);
        Assert.Equal(new DateOnly(2027, 5, 1), schedule.LastOccurrence);
        Assert.True(schedule.Enabled);
    }

    [Fact]
    public async Task DebtUpdate_PreservesExistingCurrency()
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ownerId = Guid.NewGuid();
        var debtId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        db.Debts.Add(new Debt
        {
            Id = debtId, Name = "Loan", Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id, Direction = DebtDirection.Payable,
            Type = DebtType.Bank, CounterpartyName = "Bank", OriginalAmount = 1000, Currency = "HUF", StartDate = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
        var service = new DebtService(db, TimeProvider.System);

        await service.UpdateAsync(new Debt
        {
            Id = debtId, Name = "Loan", Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id, Direction = DebtDirection.Payable,
            Type = DebtType.Bank, CounterpartyName = "Bank", OriginalAmount = 1000, Currency = string.Empty, StartDate = new DateOnly(2026, 1, 1), Note = "Updated note"
        }, null, CancellationToken.None);

        db.ChangeTracker.Clear();
        var saved = await db.Debts.SingleAsync(item => item.Id == debtId);
        Assert.Equal("HUF", saved.Currency);
        Assert.Equal("Updated note", saved.Note);
    }

    [Fact]
    public async Task RecentAccountTransactions_IncludeDebtMetadata()
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var ownerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var debtId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        db.Accounts.Add(new Account { Id = accountId, Name = "Bank", Type = AccountType.BankAccount, Currency = "HUF" });
        db.Debts.Add(new Debt
        {
            Id = debtId, Name = "Mortgage", Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id, Direction = DebtDirection.Payable,
            Type = DebtType.Bank, CounterpartyName = "Bank", OriginalAmount = 1000, Currency = "HUF", StartDate = new DateOnly(2026, 1, 1), AccountId = accountId
        });
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), AccountId = accountId, DebtId = debtId, DebtOperationType = DebtOperationType.Payment,
            Type = TransactionType.Expense, Amount = 100, SourceCurrency = "HUF", TransactionDate = new DateOnly(2026, 8, 28)
        });
        await db.SaveChangesAsync();
        var service = new AccountService(db, TimeProvider.System, null!);

        var transaction = Assert.Single(await service.GetRecentTransactionsAsync(accountId, 10, CancellationToken.None));

        Assert.NotNull(transaction.Debt);
        Assert.Equal("Mortgage", transaction.Debt.Name);
        Assert.Equal(DebtOperationType.Payment, transaction.DebtOperationType);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
