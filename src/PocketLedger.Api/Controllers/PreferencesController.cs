using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Contracts;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Services;

namespace PocketLedger.Api.Controllers;

[ApiController, Authorize, Route("api/v1/preferences")]
public sealed class PreferencesController(PocketLedgerDbContext dbContext, ICurrentUser currentUser, IUserDateProvider userDates, Microsoft.Extensions.Options.IOptions<UserDateOptions> dateOptions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var item = await GetOrCreateAsync(token);
        return Ok(new UserPreferenceResponse(item.DisplayName, item.AvatarId, item.DefaultCurrency, item.TimeZoneId, item.CurrencyFormats.Select(ApiContractMapper.ToDto).ToArray(), item.LastSeenChangelogVersion));
    }

    [HttpPut]
    public async Task<IActionResult> Update(UserPreferenceUpdateRequest request, CancellationToken token)
    {
        var item = await GetOrCreateAsync(token);
        item.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        item.AvatarId = request.AvatarId;
        item.DefaultCurrency = Currencies.Get(request.DefaultCurrency).Code;
        item.TimeZoneId = userDates.NormalizeTimeZoneId(request.TimeZoneId);
        dbContext.UserCurrencyFormats.RemoveRange(item.CurrencyFormats);
        item.CurrencyFormats = request.CurrencyFormats.Select(format => new UserCurrencyFormat { UserId = currentUser.UserId, CurrencyCode = Currencies.Get(format.CurrencyCode).Code, DecimalPlaces = format.DecimalPlaces, DecimalSeparator = format.DecimalSeparator, ThousandsSeparator = format.ThousandsSeparator, CurrencyDisplay = format.CurrencyDisplay, CurrencyPosition = format.CurrencyPosition, UseSpace = format.UseSpace }).ToList();
        await dbContext.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpPut("changelog")]
    public async Task<IActionResult> MarkChangelogSeen(ChangelogSeenRequest request, CancellationToken token)
    {
        if (!Changelog.Exists(request.Version)) return BadRequest();
        var item = await GetOrCreateAsync(token);
        item.LastSeenChangelogVersion = request.Version;
        await dbContext.SaveChangesAsync(token);
        return NoContent();
    }

    private async Task<UserPreference> GetOrCreateAsync(CancellationToken token)
    {
        var item = await dbContext.UserPreferences.Include(user => user.CurrencyFormats).SingleOrDefaultAsync(user => user.UserId == currentUser.UserId, token);
        if (item is not null) return item;
        // New users have nothing to catch up on, so they start at the current release.
        item = new UserPreference { UserId = currentUser.UserId, TimeZoneId = userDates.NormalizeTimeZoneId(dateOptions.Value.DefaultTimeZoneId), LastSeenChangelogVersion = Changelog.Latest.Version };
        dbContext.UserPreferences.Add(item);
        await dbContext.SaveChangesAsync(token);
        // The preference row is created on the user's first visit, which is when new users receive the starter categories.
        await DefaultCategorySeeder.SeedAsync(dbContext, token);
        return item;
    }
}
