using Microsoft.AspNetCore.Mvc.Rendering;
using PocketLedger.Localization;

namespace PocketLedger.Web.Services;

internal static class LocalizedEnumSelectList
{
    internal static IEnumerable<SelectListItem> GetLocalizedEnumSelectList<T>(this IHtmlHelper html) where T : struct, Enum
    {
        foreach (var item in html.GetEnumSelectList<T>())
        {
            item.Text = CommonText.Get(item.Text);
            yield return item;
        }
    }
}
