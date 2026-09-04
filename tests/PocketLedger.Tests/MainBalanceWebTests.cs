using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PocketLedger.Controllers;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Home;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;
using PocketLedger.Web.Api;

namespace PocketLedger.Tests;

public sealed class MainBalanceWebTests
{
    [Fact]
    public async Task TransactionsApiClient_MapsCurrencyCodesAndAmounts()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler("[{\"currency\":\"EUR\",\"amount\":100},{\"currency\":\"HUF\",\"amount\":100}]")) { BaseAddress = new Uri("https://api.test/") };
        var client = new TransactionsApiClient(httpClient);

        var balances = await client.CalculateMainBalanceAsync(CancellationToken.None);

        Assert.Equal("EUR", balances[0].Currency);
        Assert.Equal(100m, balances[0].Amount);
        Assert.Equal("HUF", balances[1].Currency);
        Assert.Equal(100m, balances[1].Amount);
    }

    [Fact]
    public async Task HomeIndex_RendersEqualAmountsAsSeparateCurrencyCodeAndAmountPairs()
    {
        var model = await CreateHomeModelAsync([new CurrencyBalance("EUR", 100m), new CurrencyBalance("HUF", 100m)]);

        var html = await RenderHomeViewAsync(model);

        Assert.Contains("EUR <span data-private-amount>100.00</span>", html);
        Assert.Contains("HUF <span data-private-amount>100</span>", html);
        Assert.DoesNotContain("200", html);
    }

    [Fact]
    public async Task HomeIndex_RendersEmptyMainBalanceWithoutInventingATotal()
    {
        var model = await CreateHomeModelAsync([]);

        var html = await RenderHomeViewAsync(model);

        Assert.Contains("Total main balance: </p>", html);
        Assert.DoesNotContain("Total main balance: 0", html);
    }

    private static async Task<HomeViewModel> CreateHomeModelAsync(IReadOnlyList<CurrencyBalance> mainBalances)
    {
        var accountService = Proxy<IAccountService>(method => method.Name switch
        {
            nameof(IAccountService.GetAllAsync) => Task.FromResult<IReadOnlyList<Account>>([]),
            nameof(IAccountService.GetCurrentBalancesAsync) => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>()),
            _ => throw new NotSupportedException(method.Name)
        });
        var transactionService = Proxy<ITransactionService>(method => method.Name switch
        {
            nameof(ITransactionService.GetRecentAsync) or nameof(ITransactionService.GetForMonthAsync) => Task.FromResult<IReadOnlyList<Transaction>>([]),
            nameof(ITransactionService.CalculateMainBalanceAsync) => Task.FromResult(mainBalances),
            _ => throw new NotSupportedException(method.Name)
        });
        var debtService = Proxy<IDebtService>(method => method.Name == nameof(IDebtService.GetFundingWarningsAsync)
            ? Task.FromResult<IReadOnlyList<DebtFundingWarning>>([])
            : throw new NotSupportedException(method.Name));
        var userContext = Proxy<IUserContextService>(method => method.Name == nameof(IUserContextService.TodayAsync)
            ? Task.FromResult(new DateOnly(2026, 9, 1))
            : throw new NotSupportedException(method.Name));

        var result = await new HomeController(accountService, transactionService, debtService, userContext).Index(CancellationToken.None);

        return Assert.IsType<HomeViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    private static async Task<string> RenderHomeViewAsync(HomeViewModel model)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var diagnosticListener = new DiagnosticListener(nameof(MainBalanceWebTests));
        services.AddSingleton(diagnosticListener);
        services.AddSingleton<DiagnosticSource>(diagnosticListener);
        services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly);
        services.AddSingleton<IUserContextService, TestMoneyFormatter>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var routeData = new RouteData(new RouteValueDictionary { ["controller"] = "Home", ["action"] = "Index" });
        routeData.Routers.Add(new TestRouter());
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var viewEngine = scope.ServiceProvider.GetRequiredService<ICompositeViewEngine>();
        var viewResult = viewEngine.GetView(null, "/Views/Home/Index.cshtml", false);
        Assert.True(viewResult.Success, string.Join(Environment.NewLine, viewResult.SearchedLocations ?? []));
        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<HomeViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var tempData = new TempDataDictionary(httpContext, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        var viewContext = new ViewContext(actionContext, viewResult.View, viewData, tempData, writer, new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);

        return writer.ToString();
    }

    private static T Proxy<T>(Func<MethodInfo, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InvocationProxy>();
        ((InvocationProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api/v1/transactions/main-balance", request.RequestUri?.PathAndQuery.TrimStart('/'));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private class InvocationProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!);
    }

    private sealed class TestMoneyFormatter : IUserContextService
    {
        public string FormatNumber(decimal amount, string? currency) => amount.ToString(currency == "HUF" ? "F0" : "F2", System.Globalization.CultureInfo.InvariantCulture);
        public string Format(decimal amount, string? currency) => FormatNumber(amount, currency);
        public Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) => Task.FromResult(Format(amount, currency));
        public Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public MoneyInputFormat GetMoneyInputFormat(string currency) => throw new NotSupportedException();
        public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestRouter : IRouter
    {
        public VirtualPathData GetVirtualPath(VirtualPathContext context) => new(this, "/test");
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;
    }
}
