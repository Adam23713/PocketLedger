using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services.Interfaces;

public interface IDebtService
{
    Task<IReadOnlyList<DebtSummary>> GetAllAsync(CancellationToken cancellationToken);
    Task<DebtDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Transaction?> GetOperationAsync(Guid transactionId, CancellationToken cancellationToken);
    Task<Debt> CreateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken cancellationToken);
    Task UpdateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken cancellationToken);
    Task<DebtDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Transaction> AddOperationAsync(Guid debtId, DebtOperationInput input, CancellationToken cancellationToken);
    Task<Transaction> UpdateOperationAsync(Guid transactionId, DebtOperationInput input, CancellationToken cancellationToken);
    Task DeleteOperationAsync(Guid transactionId, CancellationToken cancellationToken);
    Task CloseAsync(Guid id, CancellationToken cancellationToken);
    Task ReopenAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DebtFundingWarning>> GetFundingWarningsAsync(DateOnly today, CancellationToken cancellationToken);
}

public record DebtSummary(Debt Debt, decimal RemainingAmount, RecurringTransaction? AutomaticPayment, DateOnly? NextPayment);
public record DebtDetails(Debt Debt, decimal RemainingAmount, IReadOnlyList<Transaction> Transactions, RecurringTransaction? AutomaticPayment, DateOnly? NextPayment);
public record DebtDeletionSummary(int TransactionCount, int RecurringTransactionCount, int AffectedAccountCount);
public record RecurringPaymentInput(Guid AccountId, decimal Amount, DateOnly NextOccurrence, DateOnly? LastOccurrence, RecurringFrequency Frequency, bool Enabled);
public record DebtOperationInput(DebtOperationType Type, decimal Amount, Guid? AccountId, DateOnly Date, TimeOnly Time, string? Note);
public record DebtFundingWarning(Guid DebtId, string DebtName, string DebtIcon, DateOnly Date, decimal Amount, string Currency, string AccountName, decimal AccountBalance, decimal Shortfall);
