using PocketLedger.Models.Enums;

namespace PocketLedger.Services.Interfaces;

public record PlannerItemInput(DateOnly Month, DateOnly? PlannedDate, TransactionType Type, Guid AccountId, Guid? TargetAccountId, Guid? CategoryId, decimal Amount, string Currency, decimal AccountAmount, decimal? TargetAmount, string? Note);
public enum PlannerEventSource { Actual, Recurring, Manual }
public record PlannerEvent(Guid Id, PlannerEventSource Source, TransactionType Type, DateOnly? Date, Guid? AccountId, string AccountName, Guid? TargetAccountId, string? TargetAccountName, string? CategoryName, string? Note, decimal Amount, string Currency, decimal AccountAmount, string AccountCurrency, decimal? TargetAmount, string? TargetCurrency, decimal SourceChange, decimal TargetChange, TransactionReportingClassification Classification);
public record PlannerAccountBalance(Guid Id, string Name, string Currency, bool IncludeInMainBalance, decimal CurrentBalance, decimal OpeningBalance, decimal ClosingBalance)
{
    public decimal Change => ClosingBalance - CurrentBalance;
    public decimal? ChangePercent => CurrentBalance > 0 ? Change / CurrentBalance * 100 : null;
}
public record PlannerCurrencySummary(string Currency, decimal Income, decimal Expenses, decimal ClosingBalance, decimal Available, decimal PreviousIncome, decimal PreviousExpenses, decimal PreviousClosingBalance, decimal PreviousAvailable);
public record PlannerBalancePoint(DateOnly Date, string Currency, decimal Balance);
public record PlannerMonth(DateOnly Month, DateOnly Today, IReadOnlyList<PlannerEvent> Events, IReadOnlyList<PlannerAccountBalance> Accounts, IReadOnlyList<PlannerCurrencySummary> Totals, IReadOnlyList<PlannerBalancePoint> Points);

public interface IPlannerService
{
    Task<PlannerMonth> GetMonthAsync(int year, int month, CancellationToken cancellationToken);
    Task<PlannerItemInput?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(PlannerItemInput input, CancellationToken cancellationToken);
    Task UpdateAsync(Guid id, PlannerItemInput input, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
