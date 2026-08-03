using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Accounts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class AccountsController(IAccountService accountService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAllAsync(cancellationToken);
        var balances = await accountService.GetCurrentBalancesAsync(cancellationToken);
        return View(accounts.Select(account => ToListItem(account, balances[account.Id])).ToList());
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var account = await accountService.GetByIdAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var balance = await accountService.GetCurrentBalanceAsync(id, cancellationToken);
        var transactions = await accountService.GetRecentTransactionsAsync(id, 10, cancellationToken);
        return View(new AccountDetailsViewModel
        {
            Account = ToListItem(account, balance),
            InitialBalance = account.InitialBalance,
            RecentTransactions = transactions.Select(transaction =>
            {
                var categoryIcon = transaction.Category is null ? null : CategoryIcons.Resolve(transaction.Category);
                var isTransfer = transaction.Type == TransactionType.Transfer;
                return new AccountTransactionViewModel
                {
                    Id = transaction.Id,
                    Type = transaction.Type,
                    AdjustmentDirection = transaction.AdjustmentDirection,
                    Date = transaction.TransactionDate,
                    Amount = transaction.Amount,
                    CategoryName = isTransfer ? TransactionIcons.TransferDisplayName : transaction.Category?.Name,
                    CategoryIconPath = isTransfer ? TransactionIcons.TransferWebPath : categoryIcon?.WebPath,
                    CategoryIconAlt = isTransfer ? TransactionIcons.TransferDisplayName : categoryIcon?.DisplayName
                };
            }).ToList()
        });
    }

    [HttpGet]
    public IActionResult Create() => View(new AccountFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccountFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var account = await accountService.CreateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Account created successfully.";
            return RedirectToAction(nameof(Details), new { id = account.Id });
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var account = await accountService.GetByIdAsync(id, cancellationToken);
        return account is null ? NotFound() : View(ToForm(account));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AccountFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await accountService.UpdateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Account updated successfully.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var account = await accountService.GetByIdAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        return View(new AccountDeleteViewModel { Id = id, Name = account.Name, Currency = account.Currency, CurrentBalance = await accountService.GetCurrentBalanceAsync(id, cancellationToken) });
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await accountService.DeleteAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "Account deleted successfully.";
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

    private static AccountListItemViewModel ToListItem(Account account, decimal currentBalance)
    {
        var icon = AccountIcons.Resolve(account.Icon, account.Type);
        return new AccountListItemViewModel
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            CurrentBalance = currentBalance,
            Currency = account.Currency,
            IconPath = icon.WebPath,
            IconAlt = icon.DisplayName,
            Color = account.Color,
            DisplayOrder = account.DisplayOrder,
            IncludeInMainBalance = account.IncludeInMainBalance,
            IncludeInNetWorth = account.IncludeInNetWorth,
            IncludeInStatistics = account.IncludeInStatistics
        };
    }

    private static Account ToEntity(AccountFormViewModel model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Type = model.Type,
        Currency = model.Currency,
        InitialBalance = model.InitialBalance,
        Icon = model.Icon,
        Color = model.Color,
        DisplayOrder = model.DisplayOrder,
        IncludeInMainBalance = model.IncludeInMainBalance,
        IncludeInNetWorth = model.IncludeInNetWorth,
        IncludeInStatistics = model.IncludeInStatistics
    };

    private static AccountFormViewModel ToForm(Account account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        Type = account.Type,
        Currency = account.Currency,
        InitialBalance = account.InitialBalance,
        Icon = AccountIcons.Resolve(account.Icon, account.Type).Id,
        Color = account.Color,
        DisplayOrder = account.DisplayOrder,
        IncludeInMainBalance = account.IncludeInMainBalance,
        IncludeInNetWorth = account.IncludeInNetWorth,
        IncludeInStatistics = account.IncludeInStatistics
    };
}
