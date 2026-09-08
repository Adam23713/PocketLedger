using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Planner;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class PlannerController(IPlannerService plannerService, IAccountService accountService, ICategoryService categoryService, IUserContextService userContext) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, CancellationToken cancellationToken)
    {
        var today = await userContext.TodayAsync(cancellationToken);
        try { return View(await plannerService.GetMonthAsync(year ?? today.Year, month ?? today.Month, cancellationToken)); }
        catch (BusinessRuleException exception) { TempData["ErrorMessage"] = exception.Message; return RedirectToAction(nameof(Index)); }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? year, int? month, TransactionType type = TransactionType.Expense, CancellationToken cancellationToken = default)
    {
        var today = await userContext.TodayAsync(cancellationToken);
        if ((year ?? today.Year) is < 2 or > 9998 || (month ?? today.Month) is < 1 or > 12) return BadRequest();
        var selected = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        if ((await plannerService.GetMonthAsync(selected.Year, selected.Month, cancellationToken)).IsClosed) return BadRequest("Closed months are read-only.");
        var model = new PlannerFormViewModel { Month = selected, PlannedDate = selected.Year == today.Year && selected.Month == today.Month ? today : selected, Type = type, Currency = (await userContext.GetUserAsync(cancellationToken)).DefaultCurrency };
        await PopulateAsync(model, cancellationToken);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PlannerFormViewModel model, CancellationToken cancellationToken) => await SaveAsync(model, false, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var item = await plannerService.GetByIdAsync(id, cancellationToken);
        if (item is null) return NotFound();
        if ((await plannerService.GetMonthAsync(item.Month.Year, item.Month.Month, cancellationToken)).IsClosed) return BadRequest("Closed months are read-only.");
        var model = new PlannerFormViewModel { Id = id, Month = item.Month, PlannedDate = item.PlannedDate, Type = item.Type, AccountId = item.AccountId, TargetAccountId = item.TargetAccountId, CategoryId = item.CategoryId, Amount = item.Amount, Currency = item.Currency, AccountAmount = item.AccountAmount, TargetAmount = item.TargetAmount, Note = item.Note };
        await PopulateAsync(model, cancellationToken);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, PlannerFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        return await SaveAsync(model, true, cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var item = await plannerService.GetByIdAsync(id, cancellationToken);
        if (item is null) return NotFound();
        if ((await plannerService.GetMonthAsync(item.Month.Year, item.Month.Month, cancellationToken)).IsClosed) return BadRequest("Closed months are read-only.");
        ViewData["Id"] = id;
        return View(item);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        var item = await plannerService.GetByIdAsync(id, cancellationToken);
        if (item is null) return NotFound();
        if ((await plannerService.GetMonthAsync(item.Month.Year, item.Month.Month, cancellationToken)).IsClosed) return BadRequest("Closed months are read-only.");
        try { await plannerService.DeleteAsync(id, cancellationToken); }
        catch (EntityNotFoundException) { return NotFound(); }
        return RedirectToAction(nameof(Index), new { year = item.Month.Year, month = item.Month.Month });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> OpeningBalance(int year, int month, Guid accountId, bool useCurrentBalance, decimal? amount, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid) throw new BusinessRuleException("Enter a valid opening balance.");
            await plannerService.UpdateOpeningBalanceAsync(year, month, accountId, new(useCurrentBalance, amount), cancellationToken);
        }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (BusinessRuleException exception) { TempData["ErrorMessage"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { year, month });
    }

    private async Task<IActionResult> SaveAsync(PlannerFormViewModel model, bool editing, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                if (editing) await plannerService.UpdateAsync(model.Id, model.ToInput(), cancellationToken);
                else await plannerService.CreateAsync(model.ToInput(), cancellationToken);
                TempData["SuccessMessage"] = "Planned item saved.";
                return RedirectToAction(nameof(Index), new { year = model.Month.Year, month = model.Month.Month });
            }
            catch (EntityNotFoundException) { return NotFound(); }
            catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        await PopulateAsync(model, cancellationToken);
        return View("Form", model);
    }

    private async Task PopulateAsync(PlannerFormViewModel model, CancellationToken cancellationToken)
    {
        model.Accounts = (await accountService.GetChoicesAsync(cancellationToken)).Select(item => new AccountOptionViewModel { Id = item.Id, Name = item.Name, Currency = item.Currency }).ToList();
        model.Categories = (await categoryService.GetChoicesAsync(null, null, cancellationToken)).Select(item =>
        {
            var icon = CategoryIcons.Resolve(item.EffectiveIcon, item.Type);
            return new CategoryOptionViewModel { Id = item.Id, Name = item.Name, Type = item.Type, IsSubcategory = item.IsSubcategory, IconPath = icon.WebPath, IconAlt = icon.DisplayName };
        }).ToList();
    }
}
