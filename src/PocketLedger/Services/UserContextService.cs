using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public interface IUserContextService
{
    Task<ApplicationUser> GetUserAsync(CancellationToken cancellationToken = default);
    Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default);
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);
    Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default);
    string Format(decimal amount, string? currency);
    MoneyInputFormat GetMoneyInputFormat(string currency);
}

public sealed record MoneyInputFormat(int DecimalPlaces, string DecimalSeparator, string ThousandsSeparator);

public sealed class UserContextService(ICurrentUser currentUser, PocketLedgerDbContext dbContext, TimeProvider clock) : IUserContextService
{
    private ApplicationUser? cachedUser;

    public async Task<ApplicationUser> GetUserAsync(CancellationToken cancellationToken = default) => cachedUser ??= await dbContext.Users.Include(user => user.CurrencyFormats).SingleAsync(user => user.Id == currentUser.UserId, cancellationToken);

    public async Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default)
    {
        var zone = GetTimeZone((await GetUserAsync(cancellationToken)).TimeZoneId);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new BusinessRuleException("The selected local time does not exist because of a daylight-saving transition.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default)
    {
        var zone = GetTimeZone((await GetUserAsync(cancellationToken)).TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
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
        cachedUser ??= dbContext.Users.Include(user => user.CurrencyFormats).Single(user => user.Id == currentUser.UserId);
        var format = cachedUser.CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        var numberFormat = new System.Globalization.NumberFormatInfo { NumberDecimalDigits = format.DecimalPlaces, NumberDecimalSeparator = format.DecimalSeparator, NumberGroupSeparator = format.ThousandsSeparator };
        var number = amount.ToString($"N{format.DecimalPlaces}", numberFormat);
        var marker = format.CurrencyDisplay == CurrencyDisplay.Symbol ? definition.Symbol : definition.Code;
        var space = format.UseSpace ? " " : string.Empty;
        return format.CurrencyPosition == CurrencyPosition.Before ? marker + space + number : number + space + marker;
    }

    public MoneyInputFormat GetMoneyInputFormat(string currency)
    {
        var definition = Currencies.Get(currency);
        cachedUser ??= dbContext.Users.Include(user => user.CurrencyFormats).Single(user => user.Id == currentUser.UserId);
        var format = cachedUser.CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        return new MoneyInputFormat(format.DecimalPlaces, format.DecimalSeparator, format.ThousandsSeparator);
    }

    public static UserCurrencyFormat DefaultFormat(CurrencyDefinition definition) => new()
    {
        CurrencyCode = definition.Code, DecimalPlaces = definition.DecimalDigits, DecimalSeparator = ",", ThousandsSeparator = " ",
        CurrencyDisplay = CurrencyDisplay.Code, CurrencyPosition = CurrencyPosition.After, UseSpace = true
    };

    public static TimeZoneInfo GetTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new BusinessRuleException("The selected time zone is not available."); }
        catch (InvalidTimeZoneException) { throw new BusinessRuleException("The selected time zone is invalid."); }
    }
}
