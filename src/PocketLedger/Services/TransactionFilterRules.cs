namespace PocketLedger.Services;

public static class TransactionFilterRules
{
    public static void Validate(TransactionFilter filter)
    {
        if (filter.Month is < 1 or > 12) throw new BusinessRuleException("Month must be between 1 and 12.");
        if (filter.DateFrom > filter.DateTo) throw new BusinessRuleException("Start date cannot be after end date.");
        if (filter.AmountFrom < 0 || filter.AmountTo < 0) throw new BusinessRuleException("Amount filters cannot be negative.");
        if (filter.AmountFrom > filter.AmountTo) throw new BusinessRuleException("Minimum amount cannot exceed maximum amount.");
    }

    public static string EscapeLikePattern(string search)
    {
        return search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
