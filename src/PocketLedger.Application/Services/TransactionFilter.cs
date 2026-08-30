using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public class TransactionFilter
{
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? CategoryId { get; init; }
    public TransactionType? Type { get; init; }
    public decimal? AmountFrom { get; init; }
    public decimal? AmountTo { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record TransactionDailyTotal(DateOnly Date, string Currency, decimal Income, decimal Expenses);
