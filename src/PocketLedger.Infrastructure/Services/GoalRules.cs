using System.Text.RegularExpressions;
using PocketLedger.Models;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public static partial class GoalRules
{
    public static void Validate(Goal goal)
    {
        if (string.IsNullOrWhiteSpace(goal.Name)) throw new BusinessRuleException("Goal name is required.");
        if (goal.Name.Trim().Length > 200) throw new BusinessRuleException("Goal name cannot be longer than 200 characters.");
        if (goal.TargetAmount <= 0) throw new BusinessRuleException("Target amount must be greater than zero.");
        if (goal.SavedAmount < 0) throw new BusinessRuleException("Saved amount cannot be negative.");
        if (!Currencies.Exists(goal.Currency)) throw new BusinessRuleException("The selected currency is not supported.");
        if (!HexColor().IsMatch(goal.BannerColor ?? string.Empty)) throw new BusinessRuleException("Banner color must be a hex color such as #2563eb.");
        if (goal.BannerImage is not null && GoalBanners.Find(goal.BannerImage) is null) throw new BusinessRuleException("The selected banner image is invalid.");
        if (goal.Note?.Length > 500) throw new BusinessRuleException("Note cannot be longer than 500 characters.");
    }

    public static decimal ApplyContribution(decimal savedAmount, decimal amount)
    {
        if (amount == 0) throw new BusinessRuleException("Contribution amount cannot be zero.");
        var result = savedAmount + amount;
        if (result < 0) throw new BusinessRuleException("You cannot withdraw more than the saved amount.");
        return result;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
