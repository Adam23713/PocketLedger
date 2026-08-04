using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.RecurringTransactions;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class RecurringTransactionsController(IRecurringTransactionService recurringService, IAccountService accountService, ICategoryService categoryService, TimeProvider timeProvider) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var templates = await recurringService.GetAllAsync(cancellationToken);
        var today = BudapestDate.Today(timeProvider);
        var items = templates.Select(template =>
        {
            var categoryIcon = template.Category is null ? null : CategoryIcons.Resolve(template.Category);
            return new RecurringTransactionListItemViewModel
            {
                Id = template.Id,
                Type = template.Type,
                AdjustmentDirection = template.AdjustmentDirection,
                AccountName = template.Account.Name,
                CategoryName = template.Category?.Name,
                CategoryIconPath = categoryIcon?.WebPath,
                CategoryIconAlt = categoryIcon?.DisplayName,
                Note = template.Note,
                Amount = template.Amount,
                Currency = template.Account.Currency,
                FirstOccurrence = template.FirstOccurrence,
                LastOccurrence = template.LastOccurrence,
                NextOccurrence = template.Enabled ? RecurringSchedule.GetNextOccurrence(template, today) : null,
                Frequency = template.Frequency,
                Enabled = template.Enabled
            };
        }).OrderBy(item => !item.Enabled).ThenBy(item => item.NextOccurrence is null).ThenBy(item => item.NextOccurrence).ThenBy(item => item.FirstOccurrence).ToList();
        var expenseTotals = items.Where(item => item.Enabled && item.Type == Models.Enums.TransactionType.Expense)
            .GroupBy(item => item.Currency)
            .Select(group => new RecurringTransactionExpenseTotalViewModel(group.Key, group.Sum(item => item.Amount)))
            .OrderBy(total => total.Currency)
            .ToList();
        return View(new RecurringTransactionIndexViewModel { Items = items, ExpenseTotals = expenseTotals });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new RecurringTransactionFormViewModel();
        await PopulateChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RecurringTransactionFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await InvalidFormAsync("Create", model, cancellationToken);
        try
        {
            await recurringService.CreateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Recurring transaction created.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await InvalidFormAsync("Create", model, cancellationToken);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var template = await recurringService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();
        var model = new RecurringTransactionFormViewModel
        {
            Id = template.Id, Type = template.Type, AccountId = template.AccountId, CategoryId = template.CategoryId, Amount = template.Amount,
            AdjustmentDirection = template.AdjustmentDirection, Note = template.Note, FirstOccurrence = template.FirstOccurrence,
            LastOccurrence = template.LastOccurrence, NoEndDate = template.LastOccurrence is null, Frequency = template.Frequency, Enabled = template.Enabled
        };
        await PopulateChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, RecurringTransactionFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return await InvalidFormAsync("Edit", model, cancellationToken);
        try
        {
            await recurringService.UpdateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Recurring transaction updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await InvalidFormAsync("Edit", model, cancellationToken);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var template = await recurringService.GetByIdAsync(id, cancellationToken);
        return template is null ? NotFound() : View(template);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await recurringService.DeleteAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "Recurring transaction deleted.";
            return RedirectToAction(nameof(Index));
        }
        catch (EntityNotFoundException) { return NotFound(); }
    }

    private async Task<IActionResult> InvalidFormAsync(string view, RecurringTransactionFormViewModel model, CancellationToken cancellationToken)
    {
        await PopulateChoicesAsync(model, cancellationToken);
        return View(view, model);
    }

    private async Task PopulateChoicesAsync(RecurringTransactionFormViewModel model, CancellationToken cancellationToken)
    {
        model.Accounts = (await accountService.GetChoicesAsync(cancellationToken)).Select(choice => new AccountOptionViewModel { Id = choice.Id, Name = choice.Name, Currency = choice.Currency }).ToList();
        model.Categories = (await categoryService.GetChoicesAsync(null, null, cancellationToken)).Select(choice =>
        {
            var icon = CategoryIcons.Resolve(choice.EffectiveIcon, choice.Type);
            return new CategoryOptionViewModel { Id = choice.Id, Name = choice.IsSubcategory ? $"|--------- {choice.Name}" : choice.Name, Type = choice.Type, IsSubcategory = choice.IsSubcategory, IconPath = icon.WebPath, IconAlt = icon.DisplayName };
        }).ToList();
    }

    private static RecurringTransaction ToEntity(RecurringTransactionFormViewModel model) => new()
    {
        Id = model.Id, Type = model.Type, AccountId = model.AccountId ?? Guid.Empty, CategoryId = model.CategoryId, Amount = model.Amount,
        AdjustmentDirection = model.AdjustmentDirection, Note = model.Note, FirstOccurrence = model.FirstOccurrence,
        LastOccurrence = model.NoEndDate ? null : model.LastOccurrence, Frequency = model.Frequency, Enabled = model.Enabled
    };
}
