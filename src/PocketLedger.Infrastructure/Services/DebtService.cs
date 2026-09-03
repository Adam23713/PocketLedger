using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class DebtService(PocketLedgerDbContext dbContext, IUserContextService? userContext = null) : IDebtService
{
    public async Task<IReadOnlyList<DebtSummary>> GetAllAsync(CancellationToken cancellationToken)
    {
        var debts = await BaseQuery().ToListAsync(cancellationToken);
        var today = await RequireUserContext().TodayAsync(cancellationToken);
        return debts.Select(debt => ToSummary(debt, today)).OrderBy(item => item.Debt.Status).ThenBy(item => item.NextPayment is null).ThenBy(item => item.NextPayment).ThenBy(item => item.Debt.Name).ToList();
    }

    public async Task<DebtDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var debt = await BaseQuery().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (debt is null) return null;
        var summary = ToSummary(debt, await RequireUserContext().TodayAsync(cancellationToken));
        return new DebtDetails(debt, summary.RemainingAmount, debt.Transactions.OrderByDescending(item => item.TransactionDate).ThenByDescending(item => item.TransactionTime).ToList(), summary.AutomaticPayment, summary.NextPayment);
    }

    public Task<Transaction?> GetOperationAsync(Guid transactionId, CancellationToken cancellationToken) => dbContext.Transactions.AsNoTracking().Include(item => item.Debt).Include(item => item.Account).SingleOrDefaultAsync(item => item.Id == transactionId && item.DebtId != null, cancellationToken);

    public async Task<Debt> CreateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken cancellationToken)
    {
        debt.Id = debt.Id == Guid.Empty ? Guid.NewGuid() : debt.Id;
        debt.Status = DebtStatus.Active;
        await PrepareAndValidateAsync(debt, cancellationToken);
        dbContext.Debts.Add(debt);
        if (recurringPayment is not null) dbContext.RecurringTransactions.Add(await CreateTemplateAsync(debt, recurringPayment, cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
        return debt;
    }

    public async Task UpdateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Debts.Include(item => item.RecurringTransactions).SingleOrDefaultAsync(item => item.Id == debt.Id, cancellationToken) ?? throw new EntityNotFoundException("Debt not found.");
        debt.Currency = existing.Currency;
        await PrepareAndValidateAsync(debt, cancellationToken);
        if (existing.Direction != debt.Direction && await dbContext.Transactions.AnyAsync(item => item.DebtId == debt.Id, cancellationToken)) throw new BusinessRuleException("Debt direction cannot be changed after operations have been recorded.");
        var operations = await dbContext.Transactions.AsNoTracking().Where(item => item.DebtId == debt.Id && item.DebtOperationType != null).ToListAsync(cancellationToken);
        var remaining = DebtBalanceCalculator.Calculate(debt.OriginalAmount, operations);
        if (remaining < 0) throw new BusinessRuleException("Original amount cannot be lower than the already repaid amount.");
        existing.Name = debt.Name; existing.Icon = debt.Icon; existing.Direction = debt.Direction; existing.Type = debt.Type; existing.CounterpartyName = debt.CounterpartyName;
        existing.OriginalAmount = debt.OriginalAmount; existing.StartDate = debt.StartDate; existing.DueDate = debt.DueDate; existing.Note = debt.Note; existing.AccountId = debt.AccountId;
        var template = existing.RecurringTransactions.SingleOrDefault();
        if (recurringPayment is null)
        {
            if (template is not null) template.Enabled = false;
        }
        else if (template is null)
        {
            dbContext.RecurringTransactions.Add(await CreateTemplateAsync(existing, recurringPayment, cancellationToken));
        }
        else
        {
            await ValidateRecurringAsync(existing, recurringPayment, cancellationToken);
            var wasEnabled = template.Enabled;
            var scheduleChanged = template.FirstOccurrence != recurringPayment.NextOccurrence || template.LastOccurrence != recurringPayment.LastOccurrence || template.Frequency != recurringPayment.Frequency;
            template.AccountId = recurringPayment.AccountId; template.Amount = recurringPayment.Amount; template.FirstOccurrence = recurringPayment.NextOccurrence;
            template.LastOccurrence = recurringPayment.LastOccurrence; template.Frequency = recurringPayment.Frequency; template.Enabled = recurringPayment.Enabled && existing.Status == DebtStatus.Active;
            if (scheduleChanged || !wasEnabled && template.Enabled) template.AutomationStartsOn = await RequireUserContext().TodayAsync(cancellationToken);
        }
        ApplyAutomaticStatus(existing, remaining);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DebtDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await dbContext.Debts.AnyAsync(item => item.Id == id, cancellationToken)) throw new EntityNotFoundException("Debt not found.");
        var transactions = dbContext.Transactions.Where(item => item.DebtId == id);
        return new DebtDeletionSummary(
            await transactions.CountAsync(cancellationToken),
            await dbContext.RecurringTransactions.CountAsync(item => item.DebtId == id, cancellationToken),
            await transactions.Where(item => item.AccountId != null).Select(item => item.AccountId).Distinct().CountAsync(cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var databaseTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (dbContext.Database.IsRelational()) await LockDebtAsync(id, cancellationToken);
        var debt = await dbContext.Debts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new EntityNotFoundException("Debt not found.");
        var recurringTransactions = await dbContext.RecurringTransactions.Where(item => item.DebtId == id).ToListAsync(cancellationToken);
        var recurringIds = recurringTransactions.Select(item => item.Id).ToList();
        var transactions = await dbContext.Transactions.Where(item => item.DebtId == id).ToListAsync(cancellationToken);
        var transactionIds = transactions.Select(item => item.Id).ToList();
        var occurrences = await dbContext.RecurringTransactionOccurrences.Where(item => recurringIds.Contains(item.RecurringTransactionId) || item.TransactionId != null && transactionIds.Contains(item.TransactionId.Value)).ToListAsync(cancellationToken);
        var accountIds = transactions.Where(item => item.AccountId != null).Select(item => item.AccountId!.Value).Distinct().ToList();
        var accounts = await dbContext.Accounts.Where(item => accountIds.Contains(item.Id)).ToListAsync(cancellationToken);

        foreach (var account in accounts) account.InitialBalance += BalanceCalculator.Calculate(account.Id, 0, transactions);

        dbContext.RecurringTransactionOccurrences.RemoveRange(occurrences);
        dbContext.Transactions.RemoveRange(transactions);
        dbContext.RecurringTransactions.RemoveRange(recurringTransactions);
        dbContext.Debts.Remove(debt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await databaseTransaction.CommitAsync(cancellationToken);
    }

    public async Task<Transaction> AddOperationAsync(Guid debtId, DebtOperationInput input, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockDebtAsync(debtId, cancellationToken);
        var debt = await dbContext.Debts.Include(item => item.Transactions).Include(item => item.RecurringTransactions).SingleOrDefaultAsync(item => item.Id == debtId, cancellationToken) ?? throw new EntityNotFoundException("Debt not found.");
        var transaction = await AddOperationCoreAsync(debt, input, cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return transaction;
    }

    public async Task DeleteOperationAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Transactions.Include(item => item.Debt).ThenInclude(debt => debt!.Transactions).Include(item => item.Debt).ThenInclude(debt => debt!.RecurringTransactions).SingleOrDefaultAsync(item => item.Id == transactionId, cancellationToken) ?? throw new EntityNotFoundException("Transaction not found.");
        if (transaction.Debt is null) throw new BusinessRuleException("This is not a debt transaction.");
        var debt = transaction.Debt;
        dbContext.Transactions.Remove(transaction);
        var remaining = CalculateRemaining(debt, transaction.Id);
        ApplyAutomaticStatus(debt, remaining);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Transaction> UpdateOperationAsync(Guid transactionId, DebtOperationInput input, CancellationToken cancellationToken)
    {
        var debtId = await dbContext.Transactions.AsNoTracking().Where(item => item.Id == transactionId).Select(item => item.DebtId).SingleOrDefaultAsync(cancellationToken) ?? throw new EntityNotFoundException("Debt transaction not found.");
        if (await dbContext.RecurringTransactionOccurrences.AnyAsync(item => item.TransactionId == transactionId, cancellationToken)) throw new BusinessRuleException("Automatically generated operations cannot be edited. Delete the operation or edit its recurring payment instead.");
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockDebtAsync(debtId, cancellationToken);
        var debt = await dbContext.Debts.Include(item => item.Transactions).Include(item => item.RecurringTransactions).SingleAsync(item => item.Id == debtId, cancellationToken);
        var existing = debt.Transactions.Single(item => item.Id == transactionId);
        debt.Transactions.Remove(existing);
        dbContext.Transactions.Remove(existing);
        if (debt.Status == DebtStatus.Closed) { debt.Status = DebtStatus.Active; debt.ClosedAt = null; }
        var replacement = await AddOperationCoreAsync(debt, input, cancellationToken, false);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return replacement;
    }

    public async Task CloseAsync(Guid id, CancellationToken cancellationToken)
    {
        var details = await GetTrackedAsync(id, cancellationToken);
        if (CalculateRemaining(details) != 0) throw new BusinessRuleException("Only a fully repaid debt can be closed.");
        ApplyAutomaticStatus(details, 0);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReopenAsync(Guid id, CancellationToken cancellationToken)
    {
        var debt = await GetTrackedAsync(id, cancellationToken);
        debt.Status = DebtStatus.Active; debt.ClosedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DebtFundingWarning>> GetFundingWarningsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var end = today.AddDays(3);
        var templates = await dbContext.RecurringTransactions.AsNoTracking().Include(item => item.Debt).Include(item => item.Account)
            .Where(item => item.Enabled && item.DebtId != null && item.Debt!.Status == DebtStatus.Active).ToListAsync(cancellationToken);
        var balances = await CalculateBalancesAsync(cancellationToken);
        return templates.Select(template => (Template: template, Date: RecurringSchedule.GetNextOccurrence(template, today)))
            .Where(item => item.Date is not null && item.Date <= end && balances[item.Template.AccountId] < item.Template.Amount)
            .Select(item => new DebtFundingWarning(item.Template.DebtId!.Value, item.Template.Debt!.Name, item.Template.Debt.Icon, item.Date!.Value, item.Template.Amount, item.Template.Account.Currency, item.Template.Account.Name, balances[item.Template.AccountId], item.Template.Amount - balances[item.Template.AccountId])).ToList();
    }

    internal async Task<Transaction> AddAutomaticOperationAsync(RecurringTransaction template, DateOnly date, CancellationToken cancellationToken)
    {
        var debt = await dbContext.Debts.Include(item => item.Transactions).Include(item => item.RecurringTransactions).SingleAsync(item => item.Id == template.DebtId, cancellationToken);
        var remaining = CalculateRemaining(debt);
        return await AddOperationCoreAsync(debt, new DebtOperationInput(template.DebtOperationType!.Value, DebtRules.GetAutomaticPaymentAmount(template.Amount, remaining), template.AccountId, date, TimeOnly.MinValue, template.Note), cancellationToken, false);
    }

    private async Task<Transaction> AddOperationCoreAsync(Debt debt, DebtOperationInput input, CancellationToken cancellationToken, bool save = true)
    {
        if (debt.Status != DebtStatus.Active) throw new BusinessRuleException("The debt is closed.");
        if (input.Amount <= 0 || input.Date == default) throw new BusinessRuleException("A positive amount and date are required.");
        if (DebtRules.RequiresAccount(input.Type) && input.AccountId is null) throw new BusinessRuleException("An account is required for this operation.");
        if (!DebtRules.AllowsAccount(input.Type) && input.AccountId is not null) throw new BusinessRuleException("This operation cannot use an account.");
        if (input.Type is DebtOperationType.ManualCorrectionIncrease or DebtOperationType.ManualCorrectionDecrease && string.IsNullOrWhiteSpace(input.Note)) throw new BusinessRuleException("A note is required for a manual correction.");
        if (debt.Direction == DebtDirection.Payable && input.Type is DebtOperationType.LoanDisbursement or DebtOperationType.ReceivedRepayment || debt.Direction == DebtDirection.Receivable && input.Type is DebtOperationType.Payment or DebtOperationType.EarlyRepayment) throw new BusinessRuleException("The operation does not match the debt direction.");
        Account? account = null;
        if (input.AccountId is not null) account = await dbContext.Accounts.SingleOrDefaultAsync(item => item.Id == input.AccountId, cancellationToken) ?? throw new BusinessRuleException("The selected account does not exist.");
        if (account is not null && account.Currency != debt.Currency) throw new BusinessRuleException("Debt and account currencies must match.");
        var remaining = CalculateRemaining(debt);
        var delta = DebtRules.GetDebtDelta(input.Type, input.Amount);
        if (remaining + delta < 0) throw new BusinessRuleException("The operation amount cannot exceed the remaining debt.");
        var transaction = new Transaction { Id = Guid.NewGuid(), Type = GetTransactionType(input.Type, account), AccountId = account?.Id, Amount = input.Amount, SourceCurrency = debt.Currency, TransactionDate = input.Date, TransactionTime = input.Time, OccurredAtUtc = userContext is null ? default : await userContext.ToUtcAsync(input.Date, input.Time, cancellationToken), Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(), DebtId = debt.Id, DebtOperationType = input.Type };
        dbContext.Transactions.Add(transaction);
        ApplyAutomaticStatus(debt, remaining + delta);
        if (save) await dbContext.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    private IQueryable<Debt> BaseQuery() => dbContext.Debts.AsNoTracking().Include(item => item.Account).Include(item => item.Transactions).ThenInclude(item => item.Account).Include(item => item.RecurringTransactions).ThenInclude(item => item.Account);
    private static DebtSummary ToSummary(Debt debt, DateOnly today)
    {
        var template = debt.RecurringTransactions.SingleOrDefault();
        var next = template?.Enabled == true ? RecurringSchedule.GetNextOccurrence(template, today) : null;
        return new DebtSummary(debt, CalculateRemaining(debt), template, next);
    }
    private async Task PrepareAndValidateAsync(Debt debt, CancellationToken cancellationToken)
    {
        debt.Name = debt.Name.Trim(); debt.CounterpartyName = debt.CounterpartyName.Trim(); debt.Currency = AccountRules.NormalizeAndValidateCurrency(debt.Currency); debt.Note = string.IsNullOrWhiteSpace(debt.Note) ? null : debt.Note.Trim();
        var account = debt.AccountId is null ? null : await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == debt.AccountId, cancellationToken) ?? throw new BusinessRuleException("The selected account does not exist.");
        DebtRules.Validate(debt, account);
    }
    private async Task<RecurringTransaction> CreateTemplateAsync(Debt debt, RecurringPaymentInput input, CancellationToken cancellationToken)
    {
        await ValidateRecurringAsync(debt, input, cancellationToken);
        return new RecurringTransaction { Id = Guid.NewGuid(), Type = debt.Direction == DebtDirection.Payable ? TransactionType.Expense : TransactionType.Income, AccountId = input.AccountId, Amount = input.Amount, Note = debt.Name, FirstOccurrence = input.NextOccurrence, LastOccurrence = input.LastOccurrence, AutomationStartsOn = await RequireUserContext().TodayAsync(cancellationToken), Frequency = input.Frequency, Enabled = input.Enabled, DebtId = debt.Id, DebtOperationType = debt.Direction == DebtDirection.Payable ? DebtOperationType.Payment : DebtOperationType.ReceivedRepayment };
    }
    private async Task ValidateRecurringAsync(Debt debt, RecurringPaymentInput input, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == input.AccountId, cancellationToken) ?? throw new BusinessRuleException("The selected account does not exist.");
        if (account.Currency != debt.Currency) throw new BusinessRuleException("Debt and account currencies must match.");
        if (input.Amount <= 0 || input.NextOccurrence == default || !Enum.IsDefined(input.Frequency)) throw new BusinessRuleException("Automatic payment settings are invalid.");
        if (input.LastOccurrence < input.NextOccurrence) throw new BusinessRuleException("Last occurrence cannot be before the next occurrence.");
    }
    private async Task<Debt> GetTrackedAsync(Guid id, CancellationToken cancellationToken) => await dbContext.Debts.Include(item => item.Transactions).Include(item => item.RecurringTransactions).SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new EntityNotFoundException("Debt not found.");
    private static decimal CalculateRemaining(Debt debt, Guid? excludingTransactionId = null) => DebtBalanceCalculator.Calculate(debt.OriginalAmount, debt.Transactions, excludingTransactionId);
    private static void ApplyAutomaticStatus(Debt debt, decimal remaining)
    {
        if (remaining != 0) { if (debt.Status == DebtStatus.Closed) { debt.Status = DebtStatus.Active; debt.ClosedAt = null; } return; }
        debt.Status = DebtStatus.Closed; debt.ClosedAt = DateTimeOffset.UtcNow;
        foreach (var template in debt.RecurringTransactions) template.Enabled = false;
    }
    private static TransactionType GetTransactionType(DebtOperationType type, Account? account) => account is null ? TransactionType.DebtEntry : type is DebtOperationType.ReceivedRepayment ? TransactionType.Income : TransactionType.Expense;
    private async Task<IReadOnlyDictionary<Guid, decimal>> CalculateBalancesAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken); var transactions = await dbContext.Transactions.AsNoTracking().ToListAsync(cancellationToken);
        return accounts.ToDictionary(account => account.Id, account => BalanceCalculator.Calculate(account.Id, account.InitialBalance, transactions));
    }
    private Task LockDebtAsync(Guid debtId, CancellationToken cancellationToken) => dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({debtId.ToString()}, 0))", cancellationToken);
    private IUserContextService RequireUserContext() => userContext ?? throw new InvalidOperationException("A request user context is required for user-date calculations.");
}
