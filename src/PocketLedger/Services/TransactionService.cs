using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class TransactionService(PocketLedgerDbContext dbContext) : ITransactionService
{
    public async Task<IReadOnlyList<Transaction>> GetForMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        if (year < 1 || month is < 1 or > 12)
        {
            throw new BusinessRuleException("The selected month is invalid.");
        }

        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        return await BaseReadQuery()
            .Where(transaction => transaction.TransactionDate >= start && transaction.TransactionDate < end)
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.TransactionTime)
            .ThenByDescending(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        return await BaseReadQuery().OrderByDescending(transaction => transaction.TransactionDate).ThenByDescending(transaction => transaction.TransactionTime).ThenByDescending(transaction => transaction.Id).Take(count).ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Transaction>> GetFilteredAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        TransactionFilterRules.Validate(filter);
        var query = ApplyFilter(BaseReadQuery(), filter);
        var totalCount = await query.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var page = Math.Max(filter.Page, 1);
        var items = await query.OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.TransactionTime)
            .ThenByDescending(transaction => transaction.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Transaction>(items, totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<TransactionDailyTotal>> GetDailyTotalsAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        TransactionFilterRules.Validate(filter);
        var totals = await ApplyFilter(BaseReadQuery(), filter)
            .Where(transaction => transaction.Type == TransactionType.Income || transaction.Type == TransactionType.Expense)
            .Select(transaction => new
            {
                Date = transaction.TransactionDate,
                transaction.Account.Currency,
                Income = transaction.Type == TransactionType.Income ? transaction.Amount : 0m,
                Expenses = transaction.Type == TransactionType.Expense ? transaction.Amount : 0m
            })
            .GroupBy(transaction => new { transaction.Date, transaction.Currency })
            .Select(group => new
            {
                group.Key.Date,
                group.Key.Currency,
                Income = group.Sum(transaction => transaction.Income),
                Expenses = group.Sum(transaction => transaction.Expenses)
            })
            .ToListAsync(cancellationToken);

        return totals.Select(total => new TransactionDailyTotal(total.Date, total.Currency, total.Income, total.Expenses)).ToList();
    }

    public async Task<IReadOnlyList<Transaction>> GetForExportAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        TransactionFilterRules.Validate(filter);
        return await ApplyFilter(BaseReadQuery(), filter).OrderByDescending(transaction => transaction.TransactionDate).ThenByDescending(transaction => transaction.TransactionTime).ThenByDescending(transaction => transaction.Id).ToListAsync(cancellationToken);
    }

    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return BaseReadQuery().SingleOrDefaultAsync(transaction => transaction.Id == id, cancellationToken);
    }

    public async Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        await PrepareAndValidateAsync(transaction, cancellationToken);
        transaction.Id = transaction.Id == Guid.Empty ? Guid.NewGuid() : transaction.Id;
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        await PrepareAndValidateAsync(transaction, cancellationToken);
        var existing = await dbContext.Transactions.SingleOrDefaultAsync(item => item.Id == transaction.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Transaction not found.");

        existing.Type = transaction.Type;
        existing.AccountId = transaction.AccountId;
        existing.TargetAccountId = transaction.TargetAccountId;
        existing.Amount = transaction.Amount;
        existing.TargetAmount = transaction.TargetAmount;
        existing.AdjustmentDirection = transaction.AdjustmentDirection;
        existing.TransactionDate = transaction.TransactionDate;
        existing.TransactionTime = transaction.TransactionTime;
        existing.CategoryId = transaction.CategoryId;
        existing.Note = transaction.Note;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Transactions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Transaction not found.");
        dbContext.Transactions.Remove(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> CalculateAccountBalancesAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var transactions = await dbContext.Transactions.AsNoTracking().ToListAsync(cancellationToken);
        return accounts.ToDictionary(account => account.Id, account => BalanceCalculator.Calculate(account.Id, account.InitialBalance, transactions));
    }

    public async Task<decimal> CalculateMainBalanceAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var balances = await CalculateAccountBalancesAsync(cancellationToken);
        return BalanceCalculator.CalculateMainBalance(accounts.Select(account => (balances[account.Id], account.IncludeInMainBalance)));
    }

    private IQueryable<Transaction> BaseReadQuery()
    {
        return dbContext.Transactions.AsNoTracking().Include(transaction => transaction.Account).Include(transaction => transaction.TargetAccount).Include(transaction => transaction.Category).ThenInclude(category => category!.ParentCategory);
    }

    private async Task PrepareAndValidateAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.TransactionDate == default)
        {
            throw new BusinessRuleException("Transaction date is required.");
        }

        var account = await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == transaction.AccountId, cancellationToken);
        Category? category = null;
        if (transaction.CategoryId is not null)
        {
            category = await dbContext.Categories.AsNoTracking().SingleOrDefaultAsync(item => item.Id == transaction.CategoryId, cancellationToken);
            if (category is null)
            {
                throw new BusinessRuleException("The selected category does not exist.");
            }
        }

        if (transaction.Type == TransactionType.Transfer)
        {
            var targetAccount = transaction.TargetAccountId is null
                ? null
                : await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == transaction.TargetAccountId, cancellationToken);
            if (account is not null && targetAccount is not null && account.Currency == targetAccount.Currency)
            {
                transaction.TargetAmount = transaction.Amount;
            }

            TransactionRules.ValidateTransfer(transaction, account, targetAccount);
        }
        else
        {
            TransactionRules.Validate(transaction, account, category);
            transaction.TargetAccountId = null;
            transaction.TargetAmount = null;
        }

        transaction.Note = string.IsNullOrWhiteSpace(transaction.Note) ? null : transaction.Note.Trim();
    }

    private static IQueryable<Transaction> ApplyFilter(IQueryable<Transaction> query, TransactionFilter filter)
    {
        if (filter.DateFrom is not null) query = query.Where(transaction => transaction.TransactionDate >= filter.DateFrom);
        if (filter.DateTo is not null) query = query.Where(transaction => transaction.TransactionDate <= filter.DateTo);
        if (filter.Year is not null) query = query.Where(transaction => transaction.TransactionDate.Year == filter.Year);
        if (filter.Month is not null) query = query.Where(transaction => transaction.TransactionDate.Month == filter.Month);
        if (filter.AccountId is not null) query = query.Where(transaction => transaction.AccountId == filter.AccountId || transaction.TargetAccountId == filter.AccountId);
        if (filter.CategoryId is not null) query = query.Where(transaction => transaction.CategoryId == filter.CategoryId);
        if (filter.Type is not null) query = query.Where(transaction => transaction.Type == filter.Type);
        if (filter.AmountFrom is not null) query = query.Where(transaction => transaction.Amount >= filter.AmountFrom);
        if (filter.AmountTo is not null) query = query.Where(transaction => transaction.Amount <= filter.AmountTo);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = TransactionFilterRules.EscapeLikePattern(filter.Search);
            query = query.Where(transaction => transaction.Note != null && EF.Functions.ILike(transaction.Note, $"%{search}%", "\\"));
        }

        return query;
    }
}
