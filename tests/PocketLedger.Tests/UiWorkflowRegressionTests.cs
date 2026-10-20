using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PocketLedger.Controllers;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Calendar;
using PocketLedger.Models.ViewModels.Debts;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public sealed class UiWorkflowRegressionTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public async Task DebtOverpayment_WithoutResolutionReturnsChoiceAndDoesNotCreateOperation()
    {
        var debtId = Guid.NewGuid();
        var operationCreated = false;
        var debtService = Proxy<IDebtService>((method, _) => method.Name switch
        {
            nameof(IDebtService.GetByIdAsync) => Task.FromResult<DebtDetails?>(CreateDebtDetails(debtId, 100m)),
            nameof(IDebtService.AddOperationAsync) => TrackUnexpectedOperation(),
            _ => throw new NotSupportedException(method.Name)
        });
        var controller = CreateDebtsController(debtService);
        var model = CreateDebtOperation(debtId, 150m);

        var result = await controller.AddOperation(model, CancellationToken.None);

        var returnedModel = Assert.IsType<DebtOperationFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Same(model, returnedModel);
        Assert.True(returnedModel.RequiresOverpaymentResolution);
        Assert.Equal(50m, returnedModel.OverpaymentDifference);
        Assert.Equal("HUF", returnedModel.Currency);
        Assert.False(operationCreated);
        return;

        Task<Transaction> TrackUnexpectedOperation()
        {
            operationCreated = true;
            return Task.FromResult(new Transaction());
        }
    }

    [Theory]
    [InlineData(DebtOverpaymentResolution.IncreaseOriginalAmount)]
    [InlineData(DebtOverpaymentResolution.ClampOperation)]
    public async Task DebtOverpayment_WithResolutionForwardsSelectedBehavior(DebtOverpaymentResolution resolution)
    {
        var debtId = Guid.NewGuid();
        DebtOperationInput? capturedInput = null;
        var debtService = Proxy<IDebtService>((method, arguments) => method.Name switch
        {
            nameof(IDebtService.AddOperationAsync) => CaptureOperation(arguments),
            _ => throw new NotSupportedException(method.Name)
        });
        var controller = CreateDebtsController(debtService);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new DictionaryTempDataProvider());
        var model = CreateDebtOperation(debtId, 150m);
        model.OverpaymentResolution = resolution;

        var result = await controller.AddOperation(model, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DebtsController.Details), redirect.ActionName);
        Assert.Equal(debtId, redirect.RouteValues!["id"]);
        Assert.NotNull(capturedInput);
        Assert.Equal(150m, capturedInput.Amount);
        Assert.Equal(resolution, capturedInput.OverpaymentResolution);
        return;

        Task<Transaction> CaptureOperation(object?[]? arguments)
        {
            capturedInput = Assert.IsType<DebtOperationInput>(arguments![1]);
            return Task.FromResult(new Transaction { Id = Guid.NewGuid() });
        }
    }

    [Fact]
    public async Task TransferOut_PrefillsSourceAccountOnly()
    {
        var sourceAccountId = Guid.NewGuid();
        var controller = CreateTransactionsController(sourceAccountId);

        var result = await controller.Create(sourceAccountId, null, TransactionType.Transfer, CancellationToken.None);

        var model = Assert.IsType<TransactionFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(TransactionType.Transfer, model.Type);
        Assert.Equal(sourceAccountId, model.AccountId);
        Assert.Null(model.TargetAccountId);
    }

    [Fact]
    public async Task TransferIn_PrefillsTargetAccountOnly()
    {
        var targetAccountId = Guid.NewGuid();
        var controller = CreateTransactionsController(targetAccountId);

        var result = await controller.Create(null, targetAccountId, TransactionType.Transfer, CancellationToken.None);

        var model = Assert.IsType<TransactionFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(TransactionType.Transfer, model.Type);
        Assert.Null(model.AccountId);
        Assert.Equal(targetAccountId, model.TargetAccountId);
    }

    [Fact]
    public async Task Calendar_ClipsWeeklyRangesToSelectedMonth()
    {
        var model = await CreateCalendarModelAsync(new Dictionary<DateOnly, CalendarDaySummary>());

        Assert.Equal(new DateOnly(2026, 9, 28), model.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 1), model.Days[^1].Date);
        Assert.Collection(model.WeeklyTotals,
            week => AssertWeekRange(week, 1, 4),
            week => AssertWeekRange(week, 5, 11),
            week => AssertWeekRange(week, 12, 18),
            week => AssertWeekRange(week, 19, 25),
            week => AssertWeekRange(week, 26, 31));
    }

    [Fact]
    public async Task Calendar_WeeklyTotalsAddUpToMonthlyTotal()
    {
        var summaries = new Dictionary<DateOnly, CalendarDaySummary>
        {
            [new DateOnly(2026, 10, 2)] = Summary(2026, 10, 2, 100m, 10m),
            [new DateOnly(2026, 10, 7)] = Summary(2026, 10, 7, 20m, 5m),
            [new DateOnly(2026, 10, 30)] = Summary(2026, 10, 30, 50m, 20m)
        };

        var model = await CreateCalendarModelAsync(summaries);

        Assert.Equal(model.ActiveMonthlyTotal.Income, model.WeeklyTotals.Sum(week => week.Income));
        Assert.Equal(model.ActiveMonthlyTotal.Expenses, model.WeeklyTotals.Sum(week => week.Expenses));
        Assert.Equal(model.ActiveMonthlyTotal.Balance, model.WeeklyTotals.Sum(week => week.Balance));
    }

    private static DebtsController CreateDebtsController(IDebtService debtService)
    {
        var accountService = Proxy<IAccountService>((method, _) => method.Name == nameof(IAccountService.GetChoicesAsync)
            ? Task.FromResult<IReadOnlyList<AccountChoice>>([])
            : throw new NotSupportedException(method.Name));
        return new DebtsController(debtService, accountService, new FixedUserDateProvider(), CreateUserContext());
    }

    private static TransactionsController CreateTransactionsController(Guid accountId)
    {
        var accountService = Proxy<IAccountService>((method, _) => method.Name switch
        {
            nameof(IAccountService.GetCurrentBalancesAsync) => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal> { [accountId] = 100m }),
            nameof(IAccountService.GetChoicesAsync) => Task.FromResult<IReadOnlyList<AccountChoice>>([new AccountChoice(accountId, "Test account", "HUF")]),
            _ => throw new NotSupportedException(method.Name)
        });
        var categoryService = Proxy<ICategoryService>((method, _) => method.Name == nameof(ICategoryService.GetChoicesAsync)
            ? Task.FromResult<IReadOnlyList<CategoryChoice>>([])
            : throw new NotSupportedException(method.Name));
        var transactionService = Proxy<ITransactionService>((method, _) => throw new NotSupportedException(method.Name));
        return new TransactionsController(transactionService, accountService, categoryService, new FixedUserDateProvider(), CreateUserContext());
    }

    private static async Task<CalendarViewModel> CreateCalendarModelAsync(IReadOnlyDictionary<DateOnly, CalendarDaySummary> summaries)
    {
        var calendarService = Proxy<ICalendarService>((method, _) => method.Name == nameof(ICalendarService.GetMonthAsync)
            ? Task.FromResult(summaries)
            : throw new NotSupportedException(method.Name));
        var result = await new CalendarController(calendarService, CreateUserContext()).Index(2026, 10, "HUF", CancellationToken.None);
        return Assert.IsType<CalendarViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    private static IUserContextService CreateUserContext() => Proxy<IUserContextService>((method, _) => method.Name switch
    {
        nameof(IUserContextService.GetUserAsync) => Task.FromResult(new UserPreference { DefaultCurrency = "HUF", TimeZoneId = "UTC" }),
        nameof(IUserContextService.TodayAsync) => Task.FromResult(Today),
        _ => throw new NotSupportedException(method.Name)
    });

    private static DebtDetails CreateDebtDetails(Guid debtId, decimal originalAmount) => new(new Debt
    {
        Id = debtId,
        Name = "Test debt",
        Direction = DebtDirection.Payable,
        OriginalAmount = originalAmount,
        Currency = "HUF"
    }, originalAmount, [], null, null);

    private static DebtOperationFormViewModel CreateDebtOperation(Guid debtId, decimal amount) => new()
    {
        DebtId = debtId,
        Direction = DebtDirection.Payable,
        Type = DebtOperationType.Payment,
        Amount = amount,
        AccountId = Guid.NewGuid(),
        Date = Today
    };

    private static CalendarDaySummary Summary(int year, int month, int day, decimal income, decimal expenses)
    {
        var date = new DateOnly(year, month, day);
        return new CalendarDaySummary(date, [new CurrencyPeriodTotal("HUF", income, expenses, income - expenses)], 1);
    }

    private static void AssertWeekRange(CalendarWeekTotalViewModel week, int startDay, int endDay)
    {
        Assert.Equal(new DateOnly(2026, 10, startDay), week.StartDate);
        Assert.Equal(new DateOnly(2026, 10, endDay), week.EndDate);
        Assert.True(week.StartDate <= week.EndDate);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InvocationProxy>();
        ((InvocationProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class InvocationProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private sealed class FixedUserDateProvider : IUserDateProvider
    {
        public string NormalizeTimeZoneId(string timeZoneId) => timeZoneId;
        public DateOnly Today(string timeZoneId) => UiWorkflowRegressionTests.Today;
        public DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string timeZoneId) => new(date.ToDateTime(time), TimeSpan.Zero);
        public TimeOnly LocalTime(string timeZoneId) => new(9, 30);
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => values;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => this.values = values;
    }
}
