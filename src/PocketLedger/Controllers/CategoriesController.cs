using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Categories;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class CategoriesController(ICategoryService categoryService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetAllAsync(cancellationToken);
        return View(new CategoryListViewModel
        {
            IncomeCategories = categories.Where(category => category.Type == CategoryType.Income).Select(category => ToListItem(category)).ToList(),
            ExpenseCategories = categories.Where(category => category.Type == CategoryType.Expense).Select(category => ToListItem(category)).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CategoryType type = CategoryType.Expense, CancellationToken cancellationToken = default)
    {
        var model = new CategoryFormViewModel { Type = type, Icon = CategoryIcons.DefaultFor(type).Id };
        await PopulateParentsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateParentsAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            await categoryService.CreateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Category created successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await PopulateParentsAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var model = new CategoryFormViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Type = category.Type,
            Icon = category.ParentCategoryId is null ? CategoryIcons.Resolve(category.Icon, category.Type).Id : null,
            ParentCategoryId = category.ParentCategoryId,
            DisplayOrder = category.DisplayOrder
        };
        await PopulateParentsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await PopulateParentsAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            await categoryService.UpdateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Category updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (BusinessRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            await PopulateParentsAsync(model, cancellationToken);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : View(new CategoryDeleteViewModel { Id = id, Name = category.Name, Type = category.Type, ParentName = category.ParentCategory?.Name });
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await categoryService.DeleteAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "Category deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
        catch (BusinessRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToAction(nameof(Delete), new { id });
        }
    }

    private async Task PopulateParentsAsync(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        var choices = await categoryService.GetChoicesAsync(null, model.Id == Guid.Empty ? null : model.Id, cancellationToken);
        model.ParentCategories = choices.Where(choice => !choice.IsSubcategory).Select(choice => new CategoryParentOptionViewModel { Id = choice.Id, Name = choice.Name, Type = choice.Type }).ToList();
    }

    private static Category ToEntity(CategoryFormViewModel model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Type = model.Type,
        Icon = model.Icon,
        ParentCategoryId = model.ParentCategoryId,
        DisplayOrder = model.DisplayOrder
    };

    private static CategoryListItemViewModel ToListItem(Category category, CategoryIconDefinition? inheritedIcon = null)
    {
        var icon = inheritedIcon ?? CategoryIcons.Resolve(category.Icon, category.Type);
        return new CategoryListItemViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Type = category.Type,
            Icon = category.Icon,
            IconPath = icon?.WebPath,
            IconAlt = icon?.DisplayName,
            DisplayOrder = category.DisplayOrder,
            Subcategories = category.Subcategories.Select(subcategory => ToListItem(subcategory, icon)).ToList()
        };
    }
}
