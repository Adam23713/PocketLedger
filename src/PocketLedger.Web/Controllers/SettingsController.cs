using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Contracts;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Account;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;
using PocketLedger.Web.Api;

namespace PocketLedger.Controllers;

[Authorize]
public sealed class SettingsController(IPreferencesApiClient preferences, IAccountService accounts) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var item = await preferences.GetAsync(token);
        var accountCurrencies = (await accounts.GetChoicesAsync(token)).Select(account => account.Currency).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return View("~/Views/Account/Settings.cshtml", ToViewModel(item, accountCurrencies));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel model, CancellationToken token)
    {
        if (!ModelState.IsValid) return View("~/Views/Account/Settings.cshtml", model);
        await preferences.UpdateAsync(new UserPreferenceUpdateRequest(model.DisplayName, model.AvatarId, Currencies.Get(model.DefaultCurrency).Code, UserTimeZones.Get(model.TimeZoneId).Id, model.CurrencyFormats.Select(item => new UserCurrencyFormatDto(Currencies.Get(item.CurrencyCode).Code, item.DecimalPlaces, item.DecimalSeparator, item.ThousandsSeparator, item.CurrencyDisplay, item.CurrencyPosition, item.UseSpace)).ToList()), token);
        TempData["SuccessMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }

    // Lists the currencies the user already configured plus the default and account currencies, so formats that matter are always editable.
    private static SettingsViewModel ToViewModel(UserPreferenceResponse item, IReadOnlySet<string> accountCurrencies) => new()
    {
        DisplayName = item.DisplayName, AvatarId = item.AvatarId, DefaultCurrency = item.DefaultCurrency, TimeZoneId = item.TimeZoneId,
        CurrencyFormats = Currencies.All
            .Where(definition => definition.Code == item.DefaultCurrency || accountCurrencies.Contains(definition.Code) || item.CurrencyFormats.Any(value => value.CurrencyCode == definition.Code))
            .Select(definition =>
            {
                var format = item.CurrencyFormats.SingleOrDefault(value => value.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
                return new CurrencyFormatViewModel { CurrencyCode = definition.Code, DecimalPlaces = format.DecimalPlaces, DecimalSeparator = format.DecimalSeparator, ThousandsSeparator = format.ThousandsSeparator, CurrencyDisplay = format.CurrencyDisplay, CurrencyPosition = format.CurrencyPosition, UseSpace = format.UseSpace };
            }).ToList()
    };

    public static UserCurrencyFormatDto DefaultFormat(CurrencyDefinition definition) => new(definition.Code, definition.DecimalDigits, ",", " ", PocketLedger.Models.Enums.CurrencyDisplay.Code, PocketLedger.Models.Enums.CurrencyPosition.After, true);
}
