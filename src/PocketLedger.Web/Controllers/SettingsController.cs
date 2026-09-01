using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Contracts;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Account;
using PocketLedger.Services;
using PocketLedger.Web.Api;

namespace PocketLedger.Controllers;

[Authorize]
public sealed class SettingsController(IPreferencesApiClient preferences) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var item = await preferences.GetAsync(token);
        return View("~/Views/Account/Settings.cshtml", ToViewModel(item));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel model, CancellationToken token)
    {
        if (!ModelState.IsValid) return View("~/Views/Account/Settings.cshtml", model);
        await preferences.UpdateAsync(new UserPreferenceUpdateRequest(model.DisplayName, model.AvatarId, Currencies.Get(model.DefaultCurrency).Code, UserTimeZones.Get(model.TimeZoneId).Id, model.CurrencyFormats.Select(item => new UserCurrencyFormatDto(Currencies.Get(item.CurrencyCode).Code, item.DecimalPlaces, item.DecimalSeparator, item.ThousandsSeparator, item.CurrencyDisplay, item.CurrencyPosition, item.UseSpace)).ToList()), token);
        TempData["SuccessMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private static SettingsViewModel ToViewModel(UserPreferenceResponse item) => new()
    {
        DisplayName = item.DisplayName, AvatarId = item.AvatarId, DefaultCurrency = item.DefaultCurrency, TimeZoneId = item.TimeZoneId,
        CurrencyFormats = Currencies.All.Select(definition =>
        {
            var format = item.CurrencyFormats.SingleOrDefault(value => value.CurrencyCode == definition.Code) ?? new UserCurrencyFormatDto(definition.Code, definition.DecimalDigits, ",", " ", PocketLedger.Models.Enums.CurrencyDisplay.Code, PocketLedger.Models.Enums.CurrencyPosition.After, true);
            return new CurrencyFormatViewModel { CurrencyCode = definition.Code, DecimalPlaces = format.DecimalPlaces, DecimalSeparator = format.DecimalSeparator, ThousandsSeparator = format.ThousandsSeparator, CurrencyDisplay = format.CurrencyDisplay, CurrencyPosition = format.CurrencyPosition, UseSpace = format.UseSpace };
        }).ToList()
    };
}
