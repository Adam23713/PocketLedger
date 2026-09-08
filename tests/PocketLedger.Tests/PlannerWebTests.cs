using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PocketLedger.Controllers;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Planner;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class PlannerWebTests
{
    [Fact]
    public async Task Index_RendersCurrenciesChartManualActionsAndUpdatedNavigation()
    {
        var month = new DateOnly(2026, 9, 1);
        var account = new Account { Id = Guid.NewGuid(), Name = "Erste", Currency = "HUF", InitialBalance = 520000, IncludeInMainBalance = true };
        var euro = new Account { Id = Guid.NewGuid(), Name = "Revolut EUR", Currency = "EUR", InitialBalance = 300, IncludeInMainBalance = true };
        var category = new Category { Name = "Lakhatás" };
        var plan = new PlannerItem { Id = Guid.NewGuid(), Month = month, PlannedDate = month.AddDays(14), Type = TransactionType.Expense, AccountId = account.Id, Amount = 150000, AccountAmount = 150000, Currency = "HUF", Category = category, Note = "<script>alert('x')</script>" };
        var budget = new PlannerItem { Id = Guid.NewGuid(), Month = month, Type = TransactionType.Expense, AccountId = account.Id, Amount = 120000, AccountAmount = 120000, Currency = "HUF", Note = "Élelmiszer (havi keret)" };
        var recurring = new RecurringTransaction { Id = Guid.NewGuid(), AccountId = account.Id, FirstOccurrence = month.AddDays(9), AutomationStartsOn = month, Frequency = RecurringFrequency.Monthly, Enabled = true, Amount = 450000, Type = TransactionType.Income, Note = "Fizetés (munkahely)" };
        var actual = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, Type = TransactionType.Income, Amount = 1000, SourceCurrency = "HUF", TransactionDate = month.AddDays(-1), Note = "Previous actual transaction must not appear" };
        var currentActual = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, Type = TransactionType.Expense, Amount = 1000, SourceCurrency = "HUF", TransactionDate = month.AddDays(1), Note = "Current actual transaction must not appear" };
        var model = PlannerProjection.Calculate(month, month.AddDays(7), [account, euro], new Dictionary<Guid, decimal> { [account.Id] = 520000, [euro.Id] = 300 }, [], [plan, budget], [recurring]);
        var html = await RenderAsync("/Views/Planner/Index.cshtml", model);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Monthly planner", decoded);
        Assert.Contains("Expected balance over time", decoded);
        Assert.Contains("820,000 HUF", decoded);
        Assert.Contains("300.00 EUR", decoded);
        Assert.Contains("Undated", decoded);
        Assert.DoesNotContain("actual transaction must not appear", decoded);
        Assert.Contains("polyline", html);
        Assert.DoesNotContain("<script>alert('x')</script>", html);
        Assert.Contains("navigation-icon", html);
        var profile = html.IndexOf("id=\"profile-menu\"", StringComparison.Ordinal);
        var accounts = html.IndexOf("href=\"/Accounts/Index\"", StringComparison.Ordinal);
        var categories = html.IndexOf("href=\"/Categories/Index\"", StringComparison.Ordinal);
        Assert.True(profile >= 0 && accounts > profile && categories > accounts);
        Assert.Contains("Fixed expenses", decoded);
        Assert.Contains("Use current balance", decoded);
        Assert.DoesNotContain("Manuális", decoded);
        var closed = await RenderAsync("/Views/Planner/Index.cshtml", model with { IsClosed = true });
        Assert.DoesNotContain("/Planner/Edit", closed);
        Assert.DoesNotContain("/Planner/Create", closed);
        Assert.DoesNotContain("planner-opening\"", closed);
        Assert.Contains("read-only", closed);
        await SavePreviewAsync("planner.html", html);
        await SavePreviewAsync("planner-closed.html", closed);
    }

    [Fact]
    public async Task Form_RendersExistingCategoryTreeManualConversionAndAntiforgery()
    {
        var model = new PlannerFormViewModel
        {
            Month = new DateOnly(2026, 9, 1), PlannedDate = new DateOnly(2026, 9, 10), Currency = "EUR",
            Accounts = [new AccountOptionViewModel { Id = Guid.NewGuid(), Name = "Erste", Currency = "HUF" }, new AccountOptionViewModel { Id = Guid.NewGuid(), Name = "Revolut", Currency = "EUR" }],
            Categories = [new CategoryOptionViewModel { Id = Guid.NewGuid(), Name = "Food", Type = CategoryType.Expense }, new CategoryOptionViewModel { Id = Guid.NewGuid(), Name = "Groceries", Type = CategoryType.Expense, IsSubcategory = true }, new CategoryOptionViewModel { Id = Guid.NewGuid(), Name = "Salary", Type = CategoryType.Income }]
        };
        model.AccountId = model.Accounts[0].Id;
        var html = await RenderAsync("/Views/Planner/Form.cshtml", model);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("planner-account-amount", html);
        Assert.Contains("planner-target-amount", html);
        Assert.Contains("subcategory", html);
        Assert.Contains("planner-form.js", html);
        await SavePreviewAsync("planner-form.html", html);
    }

    private static async Task<string> RenderAsync<T>(string path, T model)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment, TestEnvironment>();
        var listener = new DiagnosticListener(nameof(PlannerWebTests));
        services.AddSingleton(listener);
        services.AddSingleton<DiagnosticSource>(listener);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddControllersWithViews().AddApplicationPart(typeof(PlannerController).Assembly);
        services.AddSingleton<IUserContextService, PlannerTestUserContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Planner test")], "Test")) };
        var routes = new RouteData(new RouteValueDictionary { ["controller"] = "Planner", ["action"] = path.EndsWith("Form.cshtml") ? "Create" : "Index" });
        routes.Routers.Add(new TestRouter());
        var action = new ActionContext(http, routes, new ActionDescriptor());
        var engine = scope.ServiceProvider.GetRequiredService<ICompositeViewEngine>();
        var view = engine.GetView(null, path, true);
        Assert.True(view.Success, string.Join(", ", view.SearchedLocations ?? []));
        await using var writer = new StringWriter();
        var data = new ViewDataDictionary<T>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var temp = new TempDataDictionary(http, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
        await view.View.RenderAsync(new ViewContext(action, view.View, data, temp, writer, new HtmlHelperOptions()));
        return writer.ToString();
    }

    private static async Task SavePreviewAsync(string name, string html)
    {
        if (Environment.GetEnvironmentVariable("PLANNER_PREVIEW_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, name), html);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(PlannerController).Assembly.GetName().Name!;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestRouter : IRouter
    {
        public VirtualPathData GetVirtualPath(VirtualPathContext context) => new(this, "/" + context.Values["controller"] + "/" + context.Values["action"]);
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;
    }
}
