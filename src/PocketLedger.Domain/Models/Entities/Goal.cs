namespace PocketLedger.Models.Entities;

public class Goal
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal SavedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly? TargetDate { get; set; }
    public string BannerColor { get; set; } = GoalBanners.DefaultColor;
    public string? BannerImage { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
