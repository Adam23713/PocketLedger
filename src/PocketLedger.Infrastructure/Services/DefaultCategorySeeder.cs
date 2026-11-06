using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

// Creates the starter categories for a new user. Categories are matched by type and name, so existing ones are left untouched.
public static class DefaultCategorySeeder
{
    public static async Task<int> SeedAsync(PocketLedgerDbContext dbContext, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Categories.ToListAsync(cancellationToken);
        var added = 0;
        foreach (var definition in DefaultCategories.All)
        {
            var main = existing.FirstOrDefault(item => item.ParentCategoryId is null && item.Type == definition.Type && string.Equals(item.Name, definition.Name, StringComparison.OrdinalIgnoreCase));
            if (main is null)
            {
                main = new Category { Id = Guid.NewGuid(), Name = definition.Name, Type = definition.Type, Icon = definition.Icon, DisplayOrder = existing.Count(item => item.ParentCategoryId is null && item.Type == definition.Type) };
                dbContext.Categories.Add(main);
                existing.Add(main);
                added++;
            }

            for (var index = 0; index < definition.Subcategories.Count; index++)
            {
                var name = definition.Subcategories[index];
                if (existing.Any(item => item.ParentCategoryId == main.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                var subcategory = new Category { Id = Guid.NewGuid(), Name = name, Type = definition.Type, ParentCategoryId = main.Id, DisplayOrder = index };
                dbContext.Categories.Add(subcategory);
                existing.Add(subcategory);
                added++;
            }
        }

        if (added > 0) await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}
