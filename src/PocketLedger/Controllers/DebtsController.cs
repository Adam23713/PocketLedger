using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Entities;
using PocketLedger.Models;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Debts;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class DebtsController(IDebtService debtService, IAccountService accountService, TimeProvider timeProvider) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = (await debtService.GetAllAsync(cancellationToken)).Select(item => ToListItem(item.Debt, item.RemainingAmount, item.NextPayment, item.AutomaticPayment?.Account.Name ?? item.Debt.Account?.Name)).ToList();
        return View(new DebtIndexViewModel { Active = items.Where(item => item.Status == DebtStatus.Active).ToList(), Closed = items.Where(item => item.Status == DebtStatus.Closed).ToList() });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var item = await debtService.GetByIdAsync(id, cancellationToken);
        if (item is null) return NotFound();
        return View(new DebtDetailsViewModel { Summary = ToListItem(item.Debt, item.RemainingAmount, item.NextPayment, item.AutomaticPayment?.Account.Name ?? item.Debt.Account?.Name), CounterpartyName = item.Debt.CounterpartyName, StartDate = item.Debt.StartDate, DueDate = item.Debt.DueDate, Note = item.Debt.Note, AutomaticPaymentAmount = item.AutomaticPayment?.Amount, Frequency = item.AutomaticPayment?.Frequency, Operations = item.Transactions.Select(transaction => new DebtOperationListItemViewModel { Id = transaction.Id, Type = transaction.DebtOperationType!.Value, Amount = transaction.Amount, Date = transaction.TransactionDate, Time = transaction.TransactionTime, AccountName = transaction.Account?.Name, Note = transaction.Note }).ToList() });
    }

    [HttpGet] public async Task<IActionResult> Create(CancellationToken cancellationToken) { var model = new DebtFormViewModel { Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id }; await PopulateAccountsAsync(model, cancellationToken); return View(model); }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Create(DebtFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { await PopulateAccountsAsync(model, cancellationToken); return View(model); }
        try { var debt = await debtService.CreateAsync(ToEntity(model), ToRecurring(model), cancellationToken); TempData["SuccessMessage"] = "Debt created."; return RedirectToAction(nameof(Details), new { id = debt.Id }); }
        catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); await PopulateAccountsAsync(model, cancellationToken); return View(model); }
    }

    [HttpGet] public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var item = await debtService.GetByIdAsync(id, cancellationToken); if (item is null) return NotFound();
        var model = new DebtFormViewModel { Id = item.Debt.Id, Name = item.Debt.Name, Icon = item.Debt.Icon, Direction = item.Debt.Direction, Type = item.Debt.Type, CounterpartyName = item.Debt.CounterpartyName, OriginalAmount = item.Debt.OriginalAmount, Currency = item.Debt.Currency, StartDate = item.Debt.StartDate, DueDate = item.Debt.DueDate, Note = item.Debt.Note, AccountId = item.AutomaticPayment?.AccountId ?? item.Debt.AccountId, AutomaticPaymentEnabled = item.AutomaticPayment?.Enabled == true, AutomaticPaymentAmount = item.AutomaticPayment?.Amount, NextPaymentDate = item.NextPayment ?? item.AutomaticPayment?.FirstOccurrence, LastPaymentDate = item.AutomaticPayment?.LastOccurrence, Frequency = item.AutomaticPayment?.Frequency ?? RecurringFrequency.Monthly };
        await PopulateAccountsAsync(model, cancellationToken); return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Edit(Guid id, DebtFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest(); if (!ModelState.IsValid) { await PopulateAccountsAsync(model, cancellationToken); return View(model); }
        try { await debtService.UpdateAsync(ToEntity(model), ToRecurring(model), cancellationToken); TempData["SuccessMessage"] = "Debt updated."; return RedirectToAction(nameof(Details), new { id }); }
        catch (EntityNotFoundException) { return NotFound(); } catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); await PopulateAccountsAsync(model, cancellationToken); return View(model); }
    }

    [HttpGet] public async Task<IActionResult> AddOperation(Guid id, CancellationToken cancellationToken)
    {
        var debt = await debtService.GetByIdAsync(id, cancellationToken); if (debt is null) return NotFound();
        var model = new DebtOperationFormViewModel { DebtId = id, Direction = debt.Debt.Direction, Type = debt.Debt.Direction == DebtDirection.Payable ? DebtOperationType.Payment : DebtOperationType.ReceivedRepayment, Date = BudapestDate.Today(timeProvider) }; await PopulateAccountsAsync(model, cancellationToken); return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> AddOperation(DebtOperationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { await PopulateAccountsAsync(model, cancellationToken); return View(model); }
        try { await debtService.AddOperationAsync(model.DebtId, new DebtOperationInput(model.Type, model.Amount, model.AccountId, model.Date, TimeOnly.FromDateTime(timeProvider.GetLocalNow().DateTime), model.Note), cancellationToken); TempData["SuccessMessage"] = "Debt operation recorded."; return RedirectToAction(nameof(Details), new { id = model.DebtId }); }
        catch (EntityNotFoundException) { return NotFound(); } catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); await PopulateAccountsAsync(model, cancellationToken); return View(model); }
    }
    [HttpGet] public async Task<IActionResult> EditOperation(Guid id, CancellationToken cancellationToken)
    {
        var item = await debtService.GetOperationAsync(id, cancellationToken); if (item?.Debt is null) return NotFound();
        var model = new DebtOperationFormViewModel { TransactionId = item.Id, DebtId = item.Debt.Id, Direction = item.Debt.Direction, Type = item.DebtOperationType!.Value, Amount = item.Amount, AccountId = item.AccountId, Date = item.TransactionDate, Note = item.Note }; await PopulateAccountsAsync(model, cancellationToken); return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> EditOperation(DebtOperationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { await PopulateAccountsAsync(model, cancellationToken); return View(model); }
        try { await debtService.UpdateOperationAsync(model.TransactionId, new DebtOperationInput(model.Type, model.Amount, model.AccountId, model.Date, TimeOnly.FromDateTime(timeProvider.GetLocalNow().DateTime), model.Note), cancellationToken); TempData["SuccessMessage"] = "Debt operation updated."; return RedirectToAction(nameof(Details), new { id = model.DebtId }); }
        catch (EntityNotFoundException) { return NotFound(); } catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); await PopulateAccountsAsync(model, cancellationToken); return View(model); }
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> DeleteOperation(Guid id, Guid debtId, CancellationToken cancellationToken) { try { await debtService.DeleteOperationAsync(id, cancellationToken); TempData["SuccessMessage"] = "Debt operation deleted."; return RedirectToAction(nameof(Details), new { id = debtId }); } catch (EntityNotFoundException) { return NotFound(); } catch (BusinessRuleException exception) { TempData["ErrorMessage"] = exception.Message; return RedirectToAction(nameof(Details), new { id = debtId }); } }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Close(Guid id, CancellationToken cancellationToken) { try { await debtService.CloseAsync(id, cancellationToken); TempData["SuccessMessage"] = "Debt closed."; } catch (BusinessRuleException exception) { TempData["ErrorMessage"] = exception.Message; } return RedirectToAction(nameof(Details), new { id }); }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Reopen(Guid id, CancellationToken cancellationToken) { await debtService.ReopenAsync(id, cancellationToken); TempData["SuccessMessage"] = "Debt reopened."; return RedirectToAction(nameof(Details), new { id }); }

    private async Task PopulateAccountsAsync(DebtFormViewModel model, CancellationToken cancellationToken) => model.Accounts = (await accountService.GetChoicesAsync(cancellationToken)).Select(item => new AccountOptionViewModel { Id = item.Id, Name = item.Name, Currency = item.Currency }).ToList();
    private async Task PopulateAccountsAsync(DebtOperationFormViewModel model, CancellationToken cancellationToken) => model.Accounts = (await accountService.GetChoicesAsync(cancellationToken)).Select(item => new AccountOptionViewModel { Id = item.Id, Name = item.Name, Currency = item.Currency }).ToList();
    private static Debt ToEntity(DebtFormViewModel model) => new() { Id = model.Id, Name = model.Name, Icon = model.Icon, Direction = model.Direction, Type = model.Type, CounterpartyName = model.CounterpartyName, OriginalAmount = model.OriginalAmount, Currency = model.Currency, StartDate = model.StartDate, DueDate = model.DueDate, Note = model.Note, AccountId = model.AccountId };
    private static RecurringPaymentInput? ToRecurring(DebtFormViewModel model) => model.AutomaticPaymentEnabled ? new RecurringPaymentInput(model.AccountId!.Value, model.AutomaticPaymentAmount!.Value, model.NextPaymentDate!.Value, model.LastPaymentDate, model.Frequency, true) : null;
    private static DebtListItemViewModel ToListItem(Debt debt, decimal remainingAmount, DateOnly? nextPayment, string? accountName)
    {
        var icon = CategoryIcons.Resolve(debt.Icon);
        return new DebtListItemViewModel { Id = debt.Id, Name = debt.Name, IconPath = icon.WebPath, IconAlt = icon.DisplayName, Direction = debt.Direction, Type = debt.Type, OriginalAmount = debt.OriginalAmount, RemainingAmount = remainingAmount, Currency = debt.Currency, NextPayment = nextPayment, AccountName = accountName, Status = debt.Status };
    }
}
