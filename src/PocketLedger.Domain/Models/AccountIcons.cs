using PocketLedger.Models.Enums;

namespace PocketLedger.Models;

public record AccountIconDefinition(string Id, AccountType AccountType, string WebPath, string DisplayName);

public static class AccountIcons
{
    public static readonly IReadOnlyList<AccountIconDefinition> All =
    [
        .. Create(AccountType.Cash, "cash", "Cash"),
        .. Create(AccountType.BankAccount, "bank-account", "Bank account"),
        .. Create(AccountType.Savings, "savings", "Savings"),
        .. Create(AccountType.CreditCard, "credit-card", "Credit card"),
        .. Create(AccountType.Other, "other", "Other")
    ];

    public static IReadOnlyList<AccountIconDefinition> For(AccountType accountType) => All.Where(icon => icon.AccountType == accountType).ToList();

    public static AccountIconDefinition DefaultFor(AccountType accountType) => For(accountType).First();

    public static AccountIconDefinition Resolve(string? id, AccountType accountType)
    {
        return All.FirstOrDefault(icon => string.Equals(icon.Id, id, StringComparison.Ordinal))
            ?? DefaultFor(accountType);
    }

    public static bool Exists(string? id)
    {
        return All.Any(icon => string.Equals(icon.Id, id, StringComparison.Ordinal));
    }

    public static AccountIconDefinition? Find(string? id)
    {
        return All.FirstOrDefault(icon => string.Equals(icon.Id, id, StringComparison.Ordinal));
    }

    private static IEnumerable<AccountIconDefinition> Create(AccountType accountType, string slug, string displayName)
    {
        return Enumerable.Range(1, 5).Select(index => new AccountIconDefinition(
            $"{slug}-{index}",
            accountType,
            $"/images/account-icons/{slug}/{slug}-{index}.png",
            $"{displayName} icon {index}"));
    }
}
