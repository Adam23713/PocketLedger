namespace PocketLedger.Models;

public sealed record ChangelogEntry(string Version, DateOnly ReleasedOn, string Title, IReadOnlyList<string> Changes);

// Release notes shown to users. Add new releases to the top; the first entry is treated as the current version.
public static class Changelog
{
    public static IReadOnlyList<ChangelogEntry> All { get; } =
    [
        new("1.1.0", new DateOnly(2026, 10, 28), "Goals, more currencies and starter categories",
        [
            "Savings goals: set a target amount, pick a banner color or image and follow your progress.",
            "More than 150 currencies are now supported.",
            "Currency settings were redesigned: pick a currency from the list to adjust its format, and choose your default currency.",
            "The initial balance on the account form now follows your currency format.",
            "New users start with default categories and subcategories.",
            "Statistics now shows income by main category in a doughnut chart, just like expenses.",
            "The transaction list shows the category first, with the type and note underneath.",
            "A contact link is available in the footer on every page.",
            "Your selected avatar is now shown on the account pages too.",
            "Login events can be filtered, sorted and paged.",
            "Large amounts in the calendar's monthly overview are listed under each other so they stay readable.",
            "This changelog: see what's new after an update, and browse earlier releases from your account page."
        ])
    ];

    public static ChangelogEntry Latest => All[0];

    // Returns the releases the user has not seen yet, newest first. A missing or unknown version means everything is new.
    public static IReadOnlyList<ChangelogEntry> Since(string? lastSeenVersion)
    {
        if (!Version.TryParse(lastSeenVersion, out var lastSeen)) return All;
        return All.Where(entry => Version.Parse(entry.Version) > lastSeen).ToList();
    }

    public static bool Exists(string? version) => All.Any(entry => entry.Version == version);
}
