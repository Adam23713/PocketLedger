using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class MilestoneRulesTests
{
    [Fact]
    public void Transfer_UpdatesSourceAndTargetBalancesFromSingleRow()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var transfer = new Transaction { Type = TransactionType.Transfer, AccountId = sourceId, TargetAccountId = targetId, Amount = 100, TargetAmount = 120 };

        Assert.Equal(900, BalanceCalculator.Calculate(sourceId, 1000, [transfer]));
        Assert.Equal(620, BalanceCalculator.Calculate(targetId, 500, [transfer]));
    }

    [Fact]
    public void Transfer_RejectsIdenticalAccounts()
    {
        var id = Guid.NewGuid();
        var account = new Account { Id = id, Currency = "HUF" };
        var transfer = new Transaction { Type = TransactionType.Transfer, AccountId = id, TargetAccountId = id, Amount = 10, TargetAmount = 10 };

        Assert.Throws<BusinessRuleException>(() => TransactionRules.ValidateTransfer(transfer, account, account));
    }

    [Fact]
    public void Transfer_RejectsDifferentAmountForMatchingCurrencies()
    {
        var source = new Account { Id = Guid.NewGuid(), Currency = "HUF" };
        var target = new Account { Id = Guid.NewGuid(), Currency = "HUF" };
        var transfer = new Transaction { Type = TransactionType.Transfer, Amount = 100, TargetAmount = 101, ExchangeRate = 1, AccountId = source.Id, TargetAccountId = target.Id };

        Assert.Throws<BusinessRuleException>(() => TransactionRules.ValidateTransfer(transfer, source, target));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void Transfer_RejectsInvalidTargetAmountOrExchangeRate(decimal targetAmount, decimal exchangeRate)
    {
        var source = new Account { Id = Guid.NewGuid(), Currency = "HUF" };
        var target = new Account { Id = Guid.NewGuid(), Currency = "EUR" };
        var transfer = new Transaction { Type = TransactionType.Transfer, Amount = 100, TargetAmount = targetAmount, ExchangeRate = exchangeRate, AccountId = source.Id, TargetAccountId = target.Id };

        Assert.Throws<BusinessRuleException>(() => TransactionRules.ValidateTransfer(transfer, source, target));
    }

    [Fact]
    public void Transfer_RejectsCategoryAndAdjustmentDirection()
    {
        var source = new Account { Id = Guid.NewGuid(), Currency = "HUF" };
        var target = new Account { Id = Guid.NewGuid(), Currency = "EUR" };
        var categorized = new Transaction { Type = TransactionType.Transfer, Amount = 100, TargetAmount = 1, ExchangeRate = .01m, CategoryId = Guid.NewGuid() };
        var adjusted = new Transaction { Type = TransactionType.Transfer, Amount = 100, TargetAmount = 1, ExchangeRate = .01m, AdjustmentDirection = AdjustmentDirection.Increase };

        Assert.Throws<BusinessRuleException>(() => TransactionRules.ValidateTransfer(categorized, source, target));
        Assert.Throws<BusinessRuleException>(() => TransactionRules.ValidateTransfer(adjusted, source, target));
    }

    [Fact]
    public void RecurringTemplate_RejectsInvalidDateRange()
    {
        var template = new RecurringTransaction
        {
            Type = TransactionType.Expense, AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), Amount = 10,
            FirstOccurrence = new DateOnly(2026, 8, 2), LastOccurrence = new DateOnly(2026, 8, 1), Frequency = RecurringFrequency.Monthly
        };

        Assert.Throws<BusinessRuleException>(() => RecurringTransactionRules.Validate(template, new Account(), new Category { Type = CategoryType.Expense }));
    }

    [Fact]
    public void SearchFilter_EscapesSqlWildcards()
    {
        Assert.Equal(@"50\%\_off", TransactionFilterRules.EscapeLikePattern(" 50%_off "));
    }

    [Fact]
    public void SearchFilter_RejectsInvalidCombinedRange()
    {
        Assert.Throws<BusinessRuleException>(() => TransactionFilterRules.Validate(new TransactionFilter { AmountFrom = 20, AmountTo = 10 }));
    }

    [Fact]
    public void CsvParser_ParsesQuotedCommasAndQuotes()
    {
        var rows = CsvParser.Parse("date,note\n2026-01-01,\"Coffee, \"\"large\"\"\"\n");

        Assert.Equal("Coffee, \"large\"", rows[1][1]);
    }

    [Fact]
    public void PeriodCalculator_CalculatesCalendarAndStatisticsTotals()
    {
        var totals = PeriodCalculator.Calculate([
            new Transaction { Type = TransactionType.Income, Amount = 100 },
            new Transaction { Type = TransactionType.Expense, Amount = 40 },
            new Transaction { Type = TransactionType.Adjustment, Amount = 5, AdjustmentDirection = AdjustmentDirection.Decrease },
            new Transaction { Type = TransactionType.Transfer, Amount = 500 }
        ]);

        Assert.Equal(100, totals.Income);
        Assert.Equal(40, totals.Expenses);
        Assert.Equal(60, totals.Savings);
        Assert.Equal(55, totals.Balance);
    }

    [Fact]
    public void BackupJson_RoundTripsAndPreservesEnums()
    {
        var account = new AccountBackup(Guid.NewGuid(), "Cash", AccountType.Cash, "HUF", 10, null, 0, true, true, true);
        var backup = new PocketLedgerBackup(1, DateTimeOffset.UtcNow, [account], [], [], []);

        var restored = BackupJson.Deserialize(BackupJson.Serialize(backup));

        Assert.Equal(AccountType.Cash, restored.Accounts[0].Type);
        Assert.Empty(BackupValidator.Validate(restored));
    }

    [Fact]
    public void RestoreValidation_RejectsMissingAccountReference()
    {
        var transaction = new TransactionBackup(Guid.NewGuid(), TransactionType.Income, Guid.NewGuid(), null, 10, null, null, new DateOnly(2026, 1, 1), null, null);
        var backup = new PocketLedgerBackup(1, DateTimeOffset.UtcNow, [], [], [transaction], []);

        Assert.Contains(BackupValidator.Validate(backup), error => error.Contains(transaction.Id.ToString()) && error.Contains("source-account-reference"));
    }
}
