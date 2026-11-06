using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Models.ViewModels.Goals;

public class GoalViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal TargetAmount { get; init; }
    public decimal SavedAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly? TargetDate { get; init; }
    public string BannerColor { get; init; } = GoalBanners.DefaultColor;
    public string? BannerImagePath { get; init; }
    public string? Note { get; init; }
    public decimal RemainingAmount => Math.Max(TargetAmount - SavedAmount, 0);
    public int ProgressPercent => TargetAmount <= 0 ? 0 : (int)Math.Clamp(Math.Floor(SavedAmount / TargetAmount * 100), 0, 100);
    public bool IsReached => SavedAmount >= TargetAmount;
}

public class GoalDetailsViewModel
{
    public GoalViewModel Goal { get; init; } = new();
    public GoalContributionViewModel Contribution { get; init; } = new();
}

public class GoalContributionViewModel
{
    [Display(Name = "Amount")] public decimal Amount { get; set; }
    public bool Withdraw { get; set; }
}

public class GoalFormViewModel
{
    public Guid Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Target amount"), Range(typeof(decimal), "0.0001", "999999999999999")]
    public decimal TargetAmount { get; set; }

    [Display(Name = "Saved so far"), Range(typeof(decimal), "0", "999999999999999")]
    public decimal SavedAmount { get; set; }

    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must contain exactly three letters.")]
    public string Currency { get; set; } = "EUR";

    [Display(Name = "Target date"), DataType(DataType.Date)]
    public DateOnly? TargetDate { get; set; }

    [Display(Name = "Banner color"), Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Choose a valid color.")]
    public string BannerColor { get; set; } = GoalBanners.DefaultColor;

    [Display(Name = "Banner image")]
    public string? BannerImage { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
