using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class TransactionsController(ITransactionService transactionService, IAccountService accountService, ICategoryService categoryService, TimeProvider timeProvider) : Controller
{
    public async Task<IActionResult> Index(DateOnly? dateFrom, DateOnly? dateTo, int? year, int? month, Guid? accountId, Guid? categoryId, TransactionType? type, decimal? amountFrom, decimal? amountTo, string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        if (dateFrom is null && dateTo is null && year is null && month is null)
        {
            year = today.Year;
            month = today.Month;
        }

        try
        {
            var filter = new TransactionFilter
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                Year = year,
                Month = month,
                AccountId = accountId,
                CategoryId = categoryId,
                Type = type,
                AmountFrom = amountFrom,
                AmountTo = amountTo,
                Search = search,
                Page = page
            };
            var result = await transactionService.GetFilteredAsync(filter, cancellationToken);
            var dailyTotals = await transactionService.GetDailyTotalsAsync(filter, cancellationToken);
            var totalsByDate = dailyTotals.ToLookup(total => total.Date);
            var accountChoices = await accountService.GetChoicesAsync(cancellationToken);
            var categoryChoices = await categoryService.GetChoicesAsync(null, null, cancellationToken);
            return View(new TransactionIndexViewModel
            {
                Year = year ?? 0,
                Month = month ?? 0,
                DateFrom = dateFrom,
                DateTo = dateTo,
                AccountId = accountId,
                CategoryId = categoryId,
                Type = type,
                AmountFrom = amountFrom,
                AmountTo = amountTo,
                Search = search,
                Page = result.Page,
                TotalPages = result.TotalPages,
                TotalCount = result.TotalCount,
                Accounts = accountChoices.Select(choice => new SelectListItem($"{choice.Name} ({choice.Currency})", choice.Id.ToString(), choice.Id == accountId)).ToList(),
                Categories = categoryChoices.Select(choice => new SelectListItem(FormatCategoryName(choice), choice.Id.ToString(), choice.Id == categoryId)).ToList(),
                DayGroups = result.Items.GroupBy(transaction => transaction.TransactionDate)
                    .Select(group => new TransactionDayGroupViewModel
                    {
                        Date = group.Key,
                        Totals = totalsByDate[group.Key].Select(total => new TransactionDailyTotalViewModel { Currency = total.Currency, Income = total.Income, Expenses = total.Expenses }).ToList(),
                        Transactions = group.Select(ToListItem).ToList()
                    })
                    .ToList()
            });
        }
        catch (BusinessRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await transactionService.GetByIdAsync(id, cancellationToken);
        return transaction is null ? NotFound() : View(ToDetails(transaction));
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid? accountId, CancellationToken cancellationToken)
    {
        var model = new TransactionFormViewModel { AccountId = accountId };
        await PopulateChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            var transaction = await transactionService.CreateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Transaction created successfully.";
            return RedirectToAction(nameof(Details), new { id = transaction.Id });
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await transactionService.GetByIdAsync(id, cancellationToken);
        if (transaction is null)
        {
            return NotFound();
        }
        if (transaction.DebtId is not null) return RedirectToAction("Details", "Debts", new { id = transaction.DebtId });

        var now = timeProvider.GetLocalNow();
        var model = new TransactionFormViewModel
        {
            Id = transaction.Id,
            Type = transaction.Type,
            AccountId = transaction.AccountId,
            TargetAccountId = transaction.TargetAccountId,
            Amount = transaction.Amount,
            TargetAmount = transaction.TargetAmount,
            TransactionDate = transaction.TransactionDate,
            TransactionHour = now.Hour,
            TransactionMinute = now.Minute,
            CategoryId = transaction.CategoryId,
            AdjustmentDirection = transaction.AdjustmentDirection,
            Note = transaction.Note
        };
        await PopulateChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TransactionFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            await transactionService.UpdateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Transaction updated successfully.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await PopulateChoicesAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await transactionService.GetByIdAsync(id, cancellationToken);
        if (transaction is null) return NotFound();
        if (transaction.DebtId is not null) return RedirectToAction("Details", "Debts", new { id = transaction.DebtId });
        var categoryIcon = transaction.Category is null ? null : CategoryIcons.Resolve(transaction.Category);
        var isTransfer = transaction.Type == TransactionType.Transfer;
        return View(new TransactionDeleteViewModel
        {
            Id = transaction.Id,
            Type = transaction.Type,
            AdjustmentDirection = transaction.AdjustmentDirection,
            AccountName = transaction.Account?.Name,
            Currency = transaction.Account?.Currency ?? transaction.Debt?.Currency ?? string.Empty,
            CategoryName = isTransfer ? TransactionIcons.TransferDisplayName : transaction.Category?.Name,
            CategoryIconPath = isTransfer ? TransactionIcons.TransferWebPath : categoryIcon?.WebPath,
            CategoryIconAlt = isTransfer ? TransactionIcons.TransferDisplayName : categoryIcon?.DisplayName,
            Amount = transaction.Amount,
            TransactionDate = transaction.TransactionDate,
            TransactionTime = transaction.TransactionTime,
            Note = transaction.Note,
            DebtName = transaction.Debt?.Name,
            DebtOperationType = transaction.DebtOperationType
        });
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await transactionService.DeleteAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "Transaction deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (BusinessRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Delete), new { id });
        }
    }

    private async Task PopulateChoicesAsync(TransactionFormViewModel model, CancellationToken cancellationToken)
    {
        model.Accounts = (await accountService.GetChoicesAsync(cancellationToken)).Select(choice => new AccountOptionViewModel { Id = choice.Id, Name = choice.Name, Currency = choice.Currency }).ToList();
        model.Categories = (await categoryService.GetChoicesAsync(null, null, cancellationToken)).Select(choice =>
        {
            var icon = CategoryIcons.Resolve(choice.EffectiveIcon, choice.Type);
            return new CategoryOptionViewModel
            {
                Id = choice.Id,
                Name = FormatCategoryName(choice),
                Type = choice.Type,
                IsSubcategory = choice.IsSubcategory,
                IconPath = icon.WebPath,
                IconAlt = icon.DisplayName
            };
        }).ToList();
    }

    private static string FormatCategoryName(CategoryChoice choice) => choice.IsSubcategory ? $"|--------- {choice.Name}" : choice.Name;

    private static Transaction ToEntity(TransactionFormViewModel model) => new()
    {
        Id = model.Id,
        Type = model.Type,
        AccountId = model.AccountId ?? Guid.Empty,
        TargetAccountId = model.TargetAccountId,
        Amount = model.Amount,
        TargetAmount = model.TargetAmount,
        TransactionDate = model.TransactionDate,
        TransactionTime = new TimeOnly(model.TransactionHour, model.TransactionMinute),
        CategoryId = model.CategoryId,
        AdjustmentDirection = model.AdjustmentDirection,
        Note = model.Note
    };

    private static TransactionListItemViewModel ToListItem(Transaction transaction)
    {
        var icon = transaction.Category is null ? null : CategoryIcons.Resolve(transaction.Category);
        var debtIcon = transaction.Debt is null ? null : CategoryIcons.Resolve(transaction.Debt.Icon);
        var isTransfer = transaction.Type == TransactionType.Transfer;
        return new TransactionListItemViewModel
        {
            Id = transaction.Id,
            Type = transaction.Type,
            AdjustmentDirection = transaction.AdjustmentDirection,
            AccountName = transaction.Account?.Name,
            TargetAccountName = transaction.TargetAccount?.Name,
            Currency = transaction.Account?.Currency ?? transaction.Debt?.Currency ?? string.Empty,
            TargetCurrency = transaction.TargetAccount?.Currency,
            CategoryName = isTransfer ? TransactionIcons.TransferDisplayName : transaction.Category?.Name,
            CategoryIconPath = isTransfer ? TransactionIcons.TransferWebPath : icon?.WebPath,
            CategoryIconAlt = isTransfer ? TransactionIcons.TransferDisplayName : icon?.DisplayName,
            Amount = transaction.Amount,
            TargetAmount = transaction.TargetAmount,
            TransactionTime = transaction.TransactionTime,
            Note = transaction.Note,
            DebtName = transaction.Debt?.Name,
            DebtOperationType = transaction.DebtOperationType,
            DebtIconPath = debtIcon?.WebPath,
            DebtIconAlt = debtIcon?.DisplayName
        };
    }

    private static TransactionDetailsViewModel ToDetails(Transaction transaction)
    {
        var icon = transaction.Category is null ? null : CategoryIcons.Resolve(transaction.Category);
        var debtIcon = transaction.Debt is null ? null : CategoryIcons.Resolve(transaction.Debt.Icon);
        var isTransfer = transaction.Type == TransactionType.Transfer;
        return new TransactionDetailsViewModel
        {
            Id = transaction.Id,
            Type = transaction.Type,
            AdjustmentDirection = transaction.AdjustmentDirection,
            AccountName = transaction.Account?.Name,
            TargetAccountName = transaction.TargetAccount?.Name,
            Currency = transaction.Account?.Currency ?? transaction.Debt?.Currency ?? string.Empty,
            TargetCurrency = transaction.TargetAccount?.Currency,
            CategoryName = isTransfer ? TransactionIcons.TransferDisplayName : transaction.Category?.Name,
            CategoryIconPath = isTransfer ? TransactionIcons.TransferWebPath : icon?.WebPath,
            CategoryIconAlt = isTransfer ? TransactionIcons.TransferDisplayName : icon?.DisplayName,
            Amount = transaction.Amount,
            TargetAmount = transaction.TargetAmount,
            TransactionDate = transaction.TransactionDate,
            TransactionTime = transaction.TransactionTime,
            Note = transaction.Note,
            DebtId = transaction.DebtId,
            DebtName = transaction.Debt?.Name,
            DebtOperationType = transaction.DebtOperationType,
            DebtIconPath = debtIcon?.WebPath,
            DebtIconAlt = debtIcon?.DisplayName
        };
    }
}
