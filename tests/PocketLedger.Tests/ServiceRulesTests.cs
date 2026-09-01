using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class ServiceRulesTests
{
    [Fact]
    public void CalculateAccountBalance_AppliesTransactionDirections()
    {
        var transactions = new[]
        {
            Transaction(TransactionType.Income, 200),
            Transaction(TransactionType.Expense, 50),
            Transaction(TransactionType.Adjustment, 25, AdjustmentDirection.Increase),
            Transaction(TransactionType.Adjustment, 10, AdjustmentDirection.Decrease)
        };

        var balance = BalanceCalculator.Calculate(100, transactions);

        Assert.Equal(265, balance);
    }

    [Fact]
    public void CalculateMainBalance_ReturnsSingleCurrencyTotal()
    {
        var balances = BalanceCalculator.CalculateMainBalance([("HUF", 100m, true), ("HUF", -20m, true)]);

        Assert.Equal(new CurrencyBalance("HUF", 80m), Assert.Single(balances));
    }

    [Fact]
    public void CalculateMainBalance_KeepsEqualAmountsInDifferentCurrenciesSeparate()
    {
        var balances = BalanceCalculator.CalculateMainBalance([("HUF", 100m, true), ("EUR", 100m, true), ("USD", 100m, true)]);

        Assert.Equal([new CurrencyBalance("EUR", 100m), new CurrencyBalance("HUF", 100m), new CurrencyBalance("USD", 100m)], balances);
    }

    [Fact]
    public void CalculateMainBalance_ExcludesAccountsNotIncludedInMainBalance()
    {
        var balances = BalanceCalculator.CalculateMainBalance([("HUF", 100m, true), ("HUF", 50m, false), ("EUR", 20m, false)]);

        Assert.Equal(new CurrencyBalance("HUF", 100m), Assert.Single(balances));
    }

    [Fact]
    public void CalculateMainBalance_ReturnsEmptyCollectionForEmptyAccounts()
    {
        var balances = BalanceCalculator.CalculateMainBalance([]);

        Assert.Empty(balances);
    }

    [Theory]
    [InlineData("huf", "HUF")]
    [InlineData(" eur ", "EUR")]
    public void NormalizeCurrency_ReturnsUppercaseCode(string input, string expected)
    {
        Assert.Equal(expected, AccountRules.NormalizeAndValidateCurrency(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("12A")]
    public void NormalizeCurrency_RejectsInvalidCode(string input)
    {
        Assert.Throws<BusinessRuleException>(() => AccountRules.NormalizeAndValidateCurrency(input));
    }

    [Fact]
    public void AccountDeletion_IsRejectedWhenTransactionsExist()
    {
        Assert.Throws<BusinessRuleException>(() => AccountRules.EnsureCanDelete(true));
    }

    [Fact]
    public void AccountIcons_ContainFiveIconsForEveryAccountType()
    {
        Assert.Equal(25, AccountIcons.All.Count);
        Assert.All(Enum.GetValues<AccountType>(), accountType => Assert.Equal(5, AccountIcons.For(accountType).Count));
    }

    [Fact]
    public void AccountIconValidation_AcceptsIconFromDifferentAccountType()
    {
        Assert.Equal("cash-1", AccountRules.ValidateIcon("cash-1"));
    }

    [Fact]
    public void AccountIconValidation_RejectsUnknownIcon()
    {
        Assert.Throws<BusinessRuleException>(() => AccountRules.ValidateIcon("../../external"));
    }

    [Fact]
    public void AccountIconResolution_UsesTypeFallbackForUnknownIcon()
    {
        var icon = AccountIcons.Resolve("https://example.com/icon.png", AccountType.CreditCard);

        Assert.Equal("credit-card-1", icon.Id);
    }

    [Fact]
    public void CategoryIcons_ContainExpectedIconsForEachCategoryType()
    {
        Assert.Equal(125, CategoryIcons.All.Count);
        Assert.Equal(100, CategoryIcons.For(CategoryType.Expense).Count);
        Assert.Equal(25, CategoryIcons.For(CategoryType.Income).Count);
    }

    [Theory]
    [InlineData(AdjustmentDirection.Increase, "/images/transaction-icons/adjustment-increase.svg", "Adjustment increase")]
    [InlineData(AdjustmentDirection.Decrease, "/images/transaction-icons/adjustment-decrease.svg", "Adjustment decrease")]
    public void Adjustment_UsesDirectionSpecificSystemIcon(AdjustmentDirection direction, string expectedPath, string expectedName)
    {
        var icon = TransactionIcons.Resolve(TransactionType.Adjustment, direction);

        Assert.NotNull(icon);
        Assert.Equal(expectedPath, icon.WebPath);
        Assert.Equal(expectedName, icon.DisplayName);
    }

    [Fact]
    public void CategoryIconValidation_RejectsIconFromDifferentCategoryType()
    {
        Assert.Throws<BusinessRuleException>(() => CategoryRules.ValidateMainCategoryIcon("salary-1", CategoryType.Expense));
    }

    [Fact]
    public void CategoryIconValidation_AcceptsCompatibleIcon()
    {
        Assert.Equal("food-3", CategoryRules.ValidateMainCategoryIcon("food-3", CategoryType.Expense));
    }

    [Fact]
    public void SubcategoryIconResolution_InheritsParentIcon()
    {
        var parent = new Category { Type = CategoryType.Expense, Icon = "shopping-4" };
        var child = new Category { Type = CategoryType.Expense, ParentCategoryId = Guid.NewGuid(), ParentCategory = parent };

        Assert.Equal("shopping-4", CategoryIcons.Resolve(child).Id);
    }

    [Fact]
    public void CategoryHierarchy_RejectsSubcategoryAsParent()
    {
        var parent = new Category { ParentCategoryId = Guid.NewGuid() };

        Assert.Throws<BusinessRuleException>(() => CategoryRules.Validate("Child", CategoryType.Expense, 0, parent));
    }

    [Fact]
    public void CategoryHierarchy_RejectsDifferentParentType()
    {
        var child = new Category { Type = CategoryType.Expense };
        var parent = new Category { Id = Guid.NewGuid(), Type = CategoryType.Income };

        Assert.Throws<BusinessRuleException>(() => CategoryRules.ValidateParent(child, parent));
    }

    [Fact]
    public void Income_RejectsExpenseCategory()
    {
        var transaction = Transaction(TransactionType.Income, 100);
        transaction.CategoryId = Guid.NewGuid();

        Assert.Throws<BusinessRuleException>(() => TransactionRules.Validate(transaction, new Account(), new Category { Type = CategoryType.Expense }));
    }

    [Fact]
    public void Expense_RejectsIncomeCategory()
    {
        var transaction = Transaction(TransactionType.Expense, 100);
        transaction.CategoryId = Guid.NewGuid();

        Assert.Throws<BusinessRuleException>(() => TransactionRules.Validate(transaction, new Account(), new Category { Type = CategoryType.Income }));
    }

    [Fact]
    public void Adjustment_RejectsCategory()
    {
        var transaction = Transaction(TransactionType.Adjustment, 100, AdjustmentDirection.Increase);
        transaction.CategoryId = Guid.NewGuid();

        Assert.Throws<BusinessRuleException>(() => TransactionRules.Validate(transaction, new Account(), new Category { Type = CategoryType.Income }));
    }

    [Fact]
    public void Transaction_RejectsNonPositiveAmount()
    {
        var transaction = Transaction(TransactionType.Expense, 0);
        transaction.CategoryId = Guid.NewGuid();

        Assert.Throws<BusinessRuleException>(() => TransactionRules.Validate(transaction, new Account(), new Category { Type = CategoryType.Expense }));
    }

    private static Transaction Transaction(TransactionType type, decimal amount, AdjustmentDirection? direction = null) => new()
    {
        Type = type,
        Amount = amount,
        AdjustmentDirection = direction,
        TransactionDate = new DateOnly(2026, 7, 29)
    };
}
