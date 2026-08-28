namespace PocketLedger.Models;

public record TransactionIconDefinition(string WebPath, string DisplayName);

public static class TransactionIcons
{
    public const string TransferWebPath = "/images/transaction-icons/transfer.svg";
    public const string TransferDisplayName = "Transfer";
    public const string AdjustmentIncreaseWebPath = "/images/transaction-icons/adjustment-increase.svg";
    public const string AdjustmentIncreaseDisplayName = "Adjustment increase";
    public const string AdjustmentDecreaseWebPath = "/images/transaction-icons/adjustment-decrease.svg";
    public const string AdjustmentDecreaseDisplayName = "Adjustment decrease";

    public static TransactionIconDefinition? Resolve(Enums.TransactionType type, Enums.AdjustmentDirection? adjustmentDirection)
    {
        if (type == Enums.TransactionType.Transfer) return new(TransferWebPath, TransferDisplayName);
        if (type != Enums.TransactionType.Adjustment) return null;
        return adjustmentDirection == Enums.AdjustmentDirection.Decrease
            ? new(AdjustmentDecreaseWebPath, AdjustmentDecreaseDisplayName)
            : new(AdjustmentIncreaseWebPath, AdjustmentIncreaseDisplayName);
    }

    public static TransactionIconDefinition? Resolve(Entities.Transaction transaction)
    {
        if (transaction.Category is not null)
        {
            var categoryIcon = CategoryIcons.Resolve(transaction.Category);
            return new(categoryIcon.WebPath, categoryIcon.DisplayName);
        }
        return Resolve(transaction.Type, transaction.AdjustmentDirection);
    }

    public static TransactionIconDefinition ResolveCategoryIcon(string? icon, Enums.CategoryType categoryType)
    {
        if (icon == AdjustmentIncreaseDisplayName) return new(AdjustmentIncreaseWebPath, AdjustmentIncreaseDisplayName);
        if (icon == AdjustmentDecreaseDisplayName) return new(AdjustmentDecreaseWebPath, AdjustmentDecreaseDisplayName);
        var categoryIcon = CategoryIcons.Resolve(icon, categoryType);
        return new(categoryIcon.WebPath, categoryIcon.DisplayName);
    }
}
