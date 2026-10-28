using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public sealed class ReleaseFeatureTests
{
    private readonly Guid ownerId = Guid.NewGuid();

    [Fact]
    public void Currencies_SupportsIsoCodesWithUniqueCodesAndKeepsCommonOnesFirst()
    {
        Assert.True(Currencies.All.Count > 150);
        Assert.Equal(Currencies.All.Count, Currencies.All.Select(item => item.Code).Distinct().Count());
        Assert.Equal(["HUF", "EUR", "USD"], Currencies.All.Take(3).Select(item => item.Code));
        Assert.Equal(3, Currencies.Get("kwd").DecimalDigits);
        Assert.Equal(0, Currencies.Get("JPY").DecimalDigits);
        Assert.False(Currencies.Exists("XYZ"));
    }

    [Fact]
    public void Changelog_ReturnsOnlyReleasesNewerThanTheLastSeenVersion()
    {
        Assert.Equal(Changelog.All, Changelog.Since(null));
        Assert.Empty(Changelog.Since(Changelog.Latest.Version));
        Assert.Contains(Changelog.Latest, Changelog.Since("1.0.0"));
        Assert.Equal(Changelog.All.Count, Changelog.All.Select(item => item.Version).Distinct().Count());
    }

    [Fact]
    public void DefaultCategories_UseIconsMatchingTheirType()
    {
        Assert.All(DefaultCategories.All, item => Assert.True(CategoryIcons.IsCompatible(item.Icon, item.Type), item.Name));
        Assert.Contains(DefaultCategories.All, item => item.Name == "Healthcare" && item.Subcategories.Count > 1);
    }

    [Fact]
    public async Task DefaultCategorySeeder_IsIdempotentAndKeepsExistingCategories()
    {
        await using var db = CreateDb();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "healthcare", Type = CategoryType.Expense, Icon = "health-2" });
        await db.SaveChangesAsync();
        var added = await DefaultCategorySeeder.SeedAsync(db, CancellationToken.None);
        var addedAgain = await DefaultCategorySeeder.SeedAsync(db, CancellationToken.None);

        Assert.True(added > 0);
        Assert.Equal(0, addedAgain);
        var healthcare = await db.Categories.SingleAsync(item => item.ParentCategoryId == null && item.Name == "healthcare");
        Assert.Equal("health-2", healthcare.Icon);
        Assert.Contains(db.Categories, item => item.ParentCategoryId == healthcare.Id && item.Name == "Dentist");
        Assert.Equal(DefaultCategories.All.Count + DefaultCategories.All.Sum(item => item.Subcategories.Count), await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Goals_TrackContributionsAndRejectOverdrawing()
    {
        await using var db = CreateDb();
        var service = new GoalService(db, TimeProvider.System);
        var goal = await service.CreateAsync(new Goal { Name = "  Holiday ", TargetAmount = 1000, SavedAmount = 100, Currency = "eur", BannerColor = "#FF0000", BannerImage = "beach" }, CancellationToken.None);

        var updated = await service.AddContributionAsync(goal.Id, 250, CancellationToken.None);

        Assert.Equal("Holiday", goal.Name);
        Assert.Equal("EUR", goal.Currency);
        Assert.Equal("#ff0000", goal.BannerColor);
        Assert.Equal(350, updated.SavedAmount);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.AddContributionAsync(goal.Id, -400, CancellationToken.None));
    }

    [Theory]
    [InlineData("", 100, 0, "HUF", "#123456", null)]
    [InlineData("Car", 0, 0, "HUF", "#123456", null)]
    [InlineData("Car", 100, -1, "HUF", "#123456", null)]
    [InlineData("Car", 100, 0, "XYZ", "#123456", null)]
    [InlineData("Car", 100, 0, "HUF", "red", null)]
    [InlineData("Car", 100, 0, "HUF", "#123456", "unknown")]
    public void GoalRules_RejectInvalidGoals(string name, decimal target, decimal saved, string currency, string color, string? image)
    {
        Assert.Throws<BusinessRuleException>(() => GoalRules.Validate(new Goal { Name = name, TargetAmount = target, SavedAmount = saved, Currency = currency, BannerColor = color, BannerImage = image }));
    }

    private PocketLedgerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
