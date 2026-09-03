using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class UserContextServiceTests
{
    [Fact]
    public void DefaultFormat_UsesCurrencyDecimalDigitsAndHungarianSeparators()
    {
        var format = UserContextService.DefaultFormat(Currencies.Get("EUR"));

        Assert.Equal(2, format.DecimalPlaces);
        Assert.Equal(",", format.DecimalSeparator);
        Assert.Equal(" ", format.ThousandsSeparator);
        Assert.Equal(CurrencyDisplay.Code, format.CurrencyDisplay);
        Assert.Equal(CurrencyPosition.After, format.CurrencyPosition);
        Assert.True(format.UseSpace);
    }

    [Fact]
    public async Task FormatMoney_UsesUserSpecificFormat()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, "Europe/Budapest", [new UserCurrencyFormat { CurrencyCode = "EUR", DecimalPlaces = 2, DecimalSeparator = ".", ThousandsSeparator = ",", CurrencyDisplay = CurrencyDisplay.Symbol, CurrencyPosition = CurrencyPosition.Before, UseSpace = false }]);
        var service = CreateService(userId, db, TimeProvider.System);

        Assert.Equal("€1,234.50", await service.FormatMoneyAsync(1234.5m, "EUR"));
        Assert.Equal(new MoneyInputFormat(2, ".", ",", "€", CurrencyPosition.Before, false), service.GetMoneyInputFormat("EUR"));
    }

    [Fact]
    public async Task MoneyInputFormat_UsesPersistedCurrencyCodeSelection()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, "Europe/Budapest", [new UserCurrencyFormat { CurrencyCode = "USD", DecimalPlaces = 2, DecimalSeparator = ",", ThousandsSeparator = " ", CurrencyDisplay = CurrencyDisplay.Code, CurrencyPosition = CurrencyPosition.After, UseSpace = true }]);
        var service = CreateService(userId, db, TimeProvider.System);

        Assert.Equal(new MoneyInputFormat(2, ",", " ", "USD", CurrencyPosition.After, true), service.GetMoneyInputFormat("USD"));
    }

    [Fact]
    public async Task Today_UsesUsersTimeZone()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, "Pacific/Kiritimati", []);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 8, 15), await CreateService(userId, db, clock).TodayAsync());
    }

    [Fact]
    public async Task ToUtc_RejectsNonexistentDaylightSavingTime()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, "Europe/Budapest", []);
        var service = CreateService(userId, db, TimeProvider.System);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ToUtcAsync(new DateOnly(2026, 3, 29), new TimeOnly(2, 30)));
    }

    [Fact]
    public async Task ToUtc_ConvertsLocalTimeUsingUsersZone()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb(userId, "Europe/Budapest", []);

        var result = await CreateService(userId, db, TimeProvider.System).ToUtcAsync(new DateOnly(2026, 1, 15), new TimeOnly(12, 0));

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void GetTimeZone_RejectsUnknownZone()
    {
        Assert.Throws<BusinessRuleException>(() => new UserDateProvider(TimeProvider.System).NormalizeTimeZoneId("Not/A-Time-Zone"));
    }

    private static UserContextService CreateService(Guid userId, PocketLedgerDbContext db, TimeProvider clock) => new(new TestCurrentUser(userId), db, new UserDateProvider(clock), Options.Create(new UserDateOptions()));

    private static PocketLedgerDbContext CreateDb(Guid userId, string timeZoneId, IReadOnlyCollection<UserCurrencyFormat> formats)
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new PocketLedgerDbContext(options);
        var user = new UserPreference { UserId = userId, TimeZoneId = timeZoneId };
        foreach (var format in formats) { format.UserId = userId; user.CurrencyFormats.Add(format); }
        db.UserPreferences.Add(user);
        db.SaveChanges();
        return db;
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
