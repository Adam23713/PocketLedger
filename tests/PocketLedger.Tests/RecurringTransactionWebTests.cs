using System.Net;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Controllers;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.RecurringTransactions;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class RecurringTransactionWebTests
{
    private static readonly Account Huf = new() { Id = Guid.NewGuid(), Name = "Erste", Currency = "HUF" };
    private static readonly Account Eur = new() { Id = Guid.NewGuid(), Name = "Revolut", Currency = "EUR" };

    [Fact]
    public async Task Index_GroupsTemplatesByFrequencyInFixedOrderAndOmitsEmptyGroups()
    {
        var model = await IndexModelAsync(Template(RecurringFrequency.Yearly, 1200), Template(RecurringFrequency.Daily, 10), Template(RecurringFrequency.Monthly, 300));

        Assert.Equal([RecurringFrequency.Daily, RecurringFrequency.Monthly, RecurringFrequency.Yearly], model.FrequencyGroups.Select(group => group.Frequency));
        Assert.All(model.FrequencyGroups, group => Assert.All(group.Items, item => Assert.Equal(group.Frequency, item.Frequency)));
    }

    [Fact]
    public async Task Index_TotalsOnlyEnabledExpensesPerFrequencyAndCurrencyWithoutMixingFrequencies()
    {
        var model = await IndexModelAsync(
            Template(RecurringFrequency.Monthly, 300), Template(RecurringFrequency.Monthly, 200), Template(RecurringFrequency.Monthly, 50, account: Eur),
            Template(RecurringFrequency.Monthly, 999, enabled: false), Template(RecurringFrequency.Monthly, 999, type: TransactionType.Income),
            Template(RecurringFrequency.Monthly, 999, type: TransactionType.Adjustment, direction: AdjustmentDirection.Decrease),
            Template(RecurringFrequency.Weekly, 70));

        var monthly = model.FrequencyGroups.Single(group => group.Frequency == RecurringFrequency.Monthly);
        var weekly = model.FrequencyGroups.Single(group => group.Frequency == RecurringFrequency.Weekly);

        Assert.Equal(6, monthly.Items.Count);
        Assert.Equal([new RecurringTransactionExpenseTotalViewModel("EUR", 50), new RecurringTransactionExpenseTotalViewModel("HUF", 500)], monthly.ExpenseTotals);
        Assert.Equal([new RecurringTransactionExpenseTotalViewModel("HUF", 70)], weekly.ExpenseTotals);
    }

    [Fact]
    public async Task Index_KeepsLoanTemplatesOutOfFrequencyGroupsAndTotals()
    {
        var loan = Template(RecurringFrequency.Monthly, 5000);
        loan.DebtId = Guid.NewGuid();
        loan.Debt = new Debt { Id = loan.DebtId.Value, Name = "Car loan" };

        var model = await IndexModelAsync(loan, Template(RecurringFrequency.Monthly, 300));

        var monthly = Assert.Single(model.FrequencyGroups);
        Assert.Equal([new RecurringTransactionExpenseTotalViewModel("HUF", 300)], monthly.ExpenseTotals);
        Assert.Equal(loan.Id, Assert.Single(model.LoanItems).Id);
    }

    [Fact]
    public async Task IndexView_RendersEachFrequencyListFollowedByItsOwnTotal()
    {
        var model = await IndexModelAsync(Template(RecurringFrequency.Yearly, 1200), Template(RecurringFrequency.Monthly, 300), Template(RecurringFrequency.Daily, 10));

        var html = WebUtility.HtmlDecode(await PlannerWebTests.RenderAsync("/Views/RecurringTransactions/Index.cshtml", model));

        var positions = new[] { "<h2 class=\"h4 mt-4\">Daily</h2>", "Total daily recurring expenses", "<h2 class=\"h4 mt-4\">Monthly</h2>", "Total monthly recurring expenses", "<h2 class=\"h4 mt-4\">Yearly</h2>", "Total yearly recurring expenses", "Loan Recurring Transactions" }.Select(text => html.IndexOf(text, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
        Assert.DoesNotContain("<h2 class=\"h4 mt-4\">Weekly</h2>", html);
        Assert.DoesNotContain("Total recurring expenses", html);
        Assert.DoesNotContain("No regular recurring templates.", html);
    }

    [Fact]
    public async Task IndexView_WithoutRegularTemplates_ShowsEmptyMessage()
    {
        var html = await PlannerWebTests.RenderAsync("/Views/RecurringTransactions/Index.cshtml", await IndexModelAsync());

        Assert.Contains("No regular recurring templates.", html);
    }

    [Fact]
    public async Task Form_AmountUsesMoneyInputWithAccountCurrencyFormats()
    {
        var model = new RecurringTransactionFormViewModel
        {
            Amount = 15000.5m, AccountId = Huf.Id, FirstOccurrence = new DateOnly(2026, 9, 1),
            Accounts = [new AccountOptionViewModel { Id = Huf.Id, Name = Huf.Name, Currency = Huf.Currency }, new AccountOptionViewModel { Id = Eur.Id, Name = Eur.Name, Currency = Eur.Currency }]
        };

        var html = await PlannerWebTests.RenderAsync("/Views/RecurringTransactions/Create.cshtml", model, "RecurringTransactions");

        Assert.Matches("<input[^>]*type=\"hidden\"[^>]*id=\"recurring-amount\"[^>]*value=\"15000.5\"", html);
        Assert.Matches("<input[^>]*id=\"recurring-amount-display\"[^>]*data-money-value=\"recurring-amount\"[^>]*data-money-account=\"AccountId\"[^>]*data-decimal-places=\"0\"", html);
        Assert.Contains("id=\"AccountId\"", html);
        Assert.Matches($"<option[^>]*value=\"{Huf.Id}\"[^>]*data-decimal-places=\"0\"[^>]*data-decimal-separator=\".\"[^>]*data-thousands-separator=\",\"", html);
        Assert.Matches($"<option[^>]*value=\"{Eur.Id}\"[^>]*data-decimal-places=\"2\"[^>]*data-decimal-separator=\".\"[^>]*data-thousands-separator=\",\"", html);
        Assert.Contains("/js/money-input.js", html);
        Assert.DoesNotContain("type=\"number\"", html);
    }

    private static async Task<RecurringTransactionIndexViewModel> IndexModelAsync(params RecurringTransaction[] templates)
    {
        var controller = new RecurringTransactionsController(new StubRecurringService(templates), null!, null!, new PlannerTestUserContext());
        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        return Assert.IsType<RecurringTransactionIndexViewModel>(result.Model);
    }

    private static RecurringTransaction Template(RecurringFrequency frequency, decimal amount, Account? account = null, bool enabled = true, TransactionType type = TransactionType.Expense, AdjustmentDirection? direction = null)
    {
        account ??= Huf;
        return new RecurringTransaction { Id = Guid.NewGuid(), Account = account, AccountId = account.Id, Type = type, AdjustmentDirection = direction, Amount = amount, Frequency = frequency, Enabled = enabled, FirstOccurrence = new DateOnly(2026, 9, 1), AutomationStartsOn = new DateOnly(2026, 9, 1) };
    }

    private sealed class StubRecurringService(IReadOnlyList<RecurringTransaction> templates) : IRecurringTransactionService
    {
        public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(templates);
        public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RecurringTransaction> CreateAsync(RecurringTransaction template, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(RecurringTransaction template, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
