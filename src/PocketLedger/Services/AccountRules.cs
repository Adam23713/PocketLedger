using PocketLedger.Models.Enums;
using PocketLedger.Models;

namespace PocketLedger.Services;

public static class AccountRules
{
    public static string NormalizeAndValidateCurrency(string? currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || !normalized.All(char.IsLetter))
        {
            throw new BusinessRuleException("Currency must contain exactly three letters.");
        }

        return normalized;
    }

    public static void Validate(string? name, AccountType type, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("Account name is required.");
        }

        if (displayOrder < 0)
        {
            throw new BusinessRuleException("Display order cannot be negative.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new BusinessRuleException("The selected account type is invalid.");
        }
    }

    public static string ValidateIcon(string? icon)
    {
        if (!AccountIcons.Exists(icon))
        {
            throw new BusinessRuleException("The selected icon is invalid.");
        }

        return icon!;
    }

    public static void EnsureCanDelete(bool hasTransactions)
    {
        if (hasTransactions)
        {
            throw new BusinessRuleException("The account cannot be deleted because transactions reference it.");
        }
    }
}
