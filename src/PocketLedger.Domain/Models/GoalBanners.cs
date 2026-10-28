namespace PocketLedger.Models;

public sealed record GoalBannerImage(string Id, string DisplayName)
{
    public string WebPath => $"/images/goal-banners/{Id}.svg";
}

// Goal banners are either a solid color or one of these bundled illustrations.
public static class GoalBanners
{
    public const string DefaultColor = "#2563eb";

    public static IReadOnlyList<GoalBannerImage> Images { get; } =
    [
        new("mountains", "Mountains"),
        new("beach", "Beach"),
        new("city", "City"),
        new("forest", "Forest"),
        new("night-sky", "Night sky"),
        new("home", "Home")
    ];

    public static GoalBannerImage? Find(string? id) => Images.FirstOrDefault(image => string.Equals(image.Id, id, StringComparison.Ordinal));
}
