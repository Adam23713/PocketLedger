using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public sealed class UserContextService(ICurrentUser currentUser, PocketLedgerDbContext dbContext, IUserDateProvider userDates, Microsoft.Extensions.Options.IOptions<UserDateOptions> dateOptions) : IUserContextService
{
    private UserPreference? cachedUser;

    public async Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default)
    {
        if (cachedUser is not null) return cachedUser;
        cachedUser = await dbContext.UserPreferences.Include(user => user.CurrencyFormats).SingleOrDefaultAsync(user => user.UserId == currentUser.UserId, cancellationToken);
        if (cachedUser is not null) return cachedUser;
        cachedUser = new UserPreference { UserId = currentUser.UserId, TimeZoneId = userDates.NormalizeTimeZoneId(dateOptions.Value.DefaultTimeZoneId) };
        dbContext.UserPreferences.Add(cachedUser);
        await dbContext.SaveChangesAsync(cancellationToken);
        return cachedUser;
    }

    public async Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default)
    {
        return userDates.ToUtc(date, time, (await GetUserAsync(cancellationToken)).TimeZoneId);
    }

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
    {
        return userDates.Today((await GetUserAsync(cancellationToken)).TimeZoneId);
    }

    public async Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        var definition = Currencies.Get(currency);
        var format = (await GetUserAsync(cancellationToken)).CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code)
            ?? DefaultFormat(definition);
        var numberFormat = new System.Globalization.NumberFormatInfo
        {
            NumberDecimalDigits = format.DecimalPlaces,
            NumberDecimalSeparator = format.DecimalSeparator,
            NumberGroupSeparator = format.ThousandsSeparator
        };
        var number = amount.ToString($"N{format.DecimalPlaces}", numberFormat);
        var marker = format.CurrencyDisplay == CurrencyDisplay.Symbol ? definition.Symbol : definition.Code;
        var space = format.UseSpace ? " " : string.Empty;
        return format.CurrencyPosition == CurrencyPosition.Before ? marker + space + number : number + space + marker;
    }

    public string Format(decimal amount, string? currency)
    {
        var definition = Currencies.Get(currency);
        cachedUser ??= dbContext.UserPreferences.Include(user => user.CurrencyFormats).Single(user => user.UserId == currentUser.UserId);
        var format = cachedUser.CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        var numberFormat = new System.Globalization.NumberFormatInfo { NumberDecimalDigits = format.DecimalPlaces, NumberDecimalSeparator = format.DecimalSeparator, NumberGroupSeparator = format.ThousandsSeparator };
        var number = amount.ToString($"N{format.DecimalPlaces}", numberFormat);
        var marker = format.CurrencyDisplay == CurrencyDisplay.Symbol ? definition.Symbol : definition.Code;
        var space = format.UseSpace ? " " : string.Empty;
        return format.CurrencyPosition == CurrencyPosition.Before ? marker + space + number : number + space + marker;
    }

    public string FormatNumber(decimal amount, string? currency)
    {
        var definition = Currencies.Get(currency);
        cachedUser ??= dbContext.UserPreferences.Include(user => user.CurrencyFormats).Single(user => user.UserId == currentUser.UserId);
        var format = cachedUser.CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        var numberFormat = new System.Globalization.NumberFormatInfo { NumberDecimalDigits = format.DecimalPlaces, NumberDecimalSeparator = format.DecimalSeparator, NumberGroupSeparator = format.ThousandsSeparator };
        return amount.ToString($"N{format.DecimalPlaces}", numberFormat);
    }

    public MoneyInputFormat GetMoneyInputFormat(string currency)
    {
        var definition = Currencies.Get(currency);
        cachedUser ??= dbContext.UserPreferences.Include(user => user.CurrencyFormats).Single(user => user.UserId == currentUser.UserId);
        var format = cachedUser.CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        var marker = format.CurrencyDisplay == CurrencyDisplay.Symbol ? definition.Symbol : definition.Code;
        return new MoneyInputFormat(format.DecimalPlaces, format.DecimalSeparator, format.ThousandsSeparator, marker, format.CurrencyPosition, format.UseSpace);
    }

    public static UserCurrencyFormat DefaultFormat(CurrencyDefinition definition) => new()
    {
        CurrencyCode = definition.Code, DecimalPlaces = definition.DecimalDigits, DecimalSeparator = ",", ThousandsSeparator = " ",
        CurrencyDisplay = CurrencyDisplay.Code, CurrencyPosition = CurrencyPosition.After, UseSpace = true
    };

}
