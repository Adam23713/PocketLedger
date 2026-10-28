using PocketLedger.Models.Enums;

namespace PocketLedger.Models;

public sealed record DefaultCategoryDefinition(string Name, CategoryType Type, string Icon, IReadOnlyList<string> Subcategories);

// Starter categories offered to every user. Subcategories inherit the icon of their main category.
public static class DefaultCategories
{
    public static IReadOnlyList<DefaultCategoryDefinition> All { get; } =
    [
        new("Housing", CategoryType.Expense, "accommodation-1", ["Rent", "Mortgage", "Maintenance", "Furniture", "Common charges"]),
        new("Utilities", CategoryType.Expense, "bills-1", ["Electricity", "Gas", "Water", "Heating", "Waste collection"]),
        new("Food", CategoryType.Expense, "food-1", ["Groceries", "Restaurants", "Takeaway", "Cafés"]),
        new("Transportation", CategoryType.Expense, "transportation-1", ["Public transport", "Taxi", "Fuel", "Parking", "Car service", "Tolls"]),
        new("Healthcare", CategoryType.Expense, "health-1", ["Doctor", "Dentist", "Pharmacy", "Optician", "Therapy", "Lab tests"]),
        new("Insurance", CategoryType.Expense, "insurance-1", ["Health insurance", "Life insurance", "Home insurance", "Car insurance", "Travel insurance"]),
        new("Streaming & subscriptions", CategoryType.Expense, "streaming-1", ["Video streaming", "Music streaming", "Gaming", "Software & apps", "News & magazines"]),
        new("Phone & internet", CategoryType.Expense, "phone-1", ["Mobile plan", "Home internet", "TV"]),
        new("Shopping", CategoryType.Expense, "shopping-1", ["Clothing", "Electronics", "Household", "Personal care"]),
        new("Entertainment", CategoryType.Expense, "entertainment-1", ["Cinema", "Concerts & events", "Hobbies", "Sports & fitness", "Books"]),
        new("Travel", CategoryType.Expense, "travel-1", ["Flights", "Accommodation", "Activities"]),
        new("Gifts & donations", CategoryType.Expense, "gift-1", ["Gifts", "Donations"]),
        new("Bank fees", CategoryType.Expense, "bank-1", ["Account fees", "Card fees", "Transfer fees"]),
        new("Other expense", CategoryType.Expense, "other-expense-1", []),
        new("Salary", CategoryType.Income, "salary-1", ["Wages", "Bonus", "Overtime"]),
        new("Investment", CategoryType.Income, "investment-1", ["Dividends", "Capital gains", "Rental income"]),
        new("Interest", CategoryType.Income, "interest-1", ["Savings interest"]),
        new("Cashback", CategoryType.Income, "cashback-1", ["Card cashback", "Rewards"]),
        new("Other income", CategoryType.Income, "other-income-1", ["Gifts received", "Refunds", "Sales"])
    ];
}
