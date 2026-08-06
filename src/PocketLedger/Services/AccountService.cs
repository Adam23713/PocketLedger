using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class AccountService(PocketLedgerDbContext dbContext) : IAccountService
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Accounts.AsNoTracking().OrderBy(account => account.DisplayOrder).ThenBy(account => account.Name).ToListAsync(cancellationToken);
    }

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
    }

    public async Task<Account> CreateAsync(Account account, CancellationToken cancellationToken)
    {
        PrepareAndValidate(account);
        account.Id = account.Id == Guid.Empty ? Guid.NewGuid() : account.Id;
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken)
    {
        PrepareAndValidate(account);
        var existing = await dbContext.Accounts.SingleOrDefaultAsync(item => item.Id == account.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Account not found.");

        existing.Name = account.Name;
        existing.Type = account.Type;
        existing.Currency = account.Currency;
        existing.InitialBalance = account.InitialBalance;
        existing.Icon = account.Icon;
        existing.Color = account.Color;
        existing.DisplayOrder = account.DisplayOrder;
        existing.IncludeInMainBalance = account.IncludeInMainBalance;
        existing.IncludeInNetWorth = account.IncludeInNetWorth;
        existing.IncludeInStatistics = account.IncludeInStatistics;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Account not found.");
        var hasTransactions = await dbContext.Transactions.AnyAsync(transaction => transaction.AccountId == id || transaction.TargetAccountId == id, cancellationToken);
        var hasRecurringTransactions = await dbContext.RecurringTransactions.AnyAsync(template => template.AccountId == id, cancellationToken);
        AccountRules.EnsureCanDelete(hasTransactions || hasRecurringTransactions);
        dbContext.Accounts.Remove(account);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal> GetCurrentBalanceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken)
            ?? throw new EntityNotFoundException("Account not found.");
        var transactions = await dbContext.Transactions.AsNoTracking().Where(transaction => transaction.AccountId == accountId || transaction.TargetAccountId == accountId).ToListAsync(cancellationToken);
        return BalanceCalculator.Calculate(account.Id, account.InitialBalance, transactions);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetCurrentBalancesAsync(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var transactions = await dbContext.Transactions.AsNoTracking().ToListAsync(cancellationToken);
        return accounts.ToDictionary(account => account.Id, account => BalanceCalculator.Calculate(account.Id, account.InitialBalance, transactions));
    }

    public async Task<IReadOnlyList<AccountChoice>> GetChoicesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Accounts.AsNoTracking()
            .OrderBy(account => account.DisplayOrder)
            .ThenBy(account => account.Name)
            .Select(account => new AccountChoice(account.Id, account.Name, account.Currency))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetRecentTransactionsAsync(Guid accountId, int count, CancellationToken cancellationToken)
    {
        return await dbContext.Transactions.AsNoTracking()
            .Include(transaction => transaction.Category)
            .ThenInclude(category => category!.ParentCategory)
            .Where(transaction => transaction.AccountId == accountId || transaction.TargetAccountId == accountId)
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.TransactionTime)
            .ThenByDescending(transaction => transaction.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    private static void PrepareAndValidate(Account account)
    {
        AccountRules.Validate(account.Name, account.Type, account.DisplayOrder);
        account.Name = account.Name.Trim();
        account.Currency = AccountRules.NormalizeAndValidateCurrency(account.Currency);
        account.Icon = AccountRules.ValidateIcon(account.Icon);
    }
}
