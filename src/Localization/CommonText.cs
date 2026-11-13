using System.Resources;

namespace PocketLedger.Localization;

internal static class CommonText
{
    private static readonly ResourceManager Resources = new(
        $"{typeof(CommonText).Assembly.GetName().Name}.Resources.Common", typeof(CommonText).Assembly) { IgnoreCase = true };

    internal static string IconLabel(string? value)
    {
        if (value is null) return string.Empty;
        const string separator = " icon ";
        var index = value.LastIndexOf(separator, StringComparison.Ordinal);
        if (index < 0) return Get(value);
        var group = value[..index];
        var label = PocketLedger.Models.DefaultCategories.IsStarterCategoryKey(group)
            ? PocketLedger.Models.DefaultCategories.DisplayName(group, group)
            : Get(group);
        return Format("{0} icon {1}", label, value[(index + separator.Length)..]);
    }

    internal static string EnumLabel(Enum? value) => value is null ? string.Empty : Get(value.ToString());

    internal static string Format(string key, params object?[] values) => string.Format(System.Globalization.CultureInfo.CurrentUICulture, Get(key), values);

    internal static string Get(string key) => Resources.GetString(key) ?? key;
}
