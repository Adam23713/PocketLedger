using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Goals;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class GoalsController(IGoalService goalService, IUserContextService userContext) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View((await goalService.GetAllAsync(cancellationToken)).Select(ToViewModel).ToList());

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var goal = await goalService.GetByIdAsync(id, cancellationToken);
        return goal is null ? NotFound() : View(new GoalDetailsViewModel { Goal = ToViewModel(goal) });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) => View(new GoalFormViewModel { Currency = (await userContext.GetUserAsync(cancellationToken)).DefaultCurrency });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GoalFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var created = await goalService.CreateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Goal created.";
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }
        catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); return View(model); }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var goal = await goalService.GetByIdAsync(id, cancellationToken);
        if (goal is null) return NotFound();
        return View(new GoalFormViewModel { Id = goal.Id, Name = goal.Name, TargetAmount = goal.TargetAmount, SavedAmount = goal.SavedAmount, Currency = goal.Currency, TargetDate = goal.TargetDate, BannerColor = goal.BannerColor, BannerImage = goal.BannerImage, Note = goal.Note });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, GoalFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);
        try
        {
            await goalService.UpdateAsync(ToEntity(model), cancellationToken);
            TempData["SuccessMessage"] = "Goal saved.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (BusinessRuleException exception) { ModelState.AddModelError(string.Empty, exception.Message); return View(model); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Contribute(Guid id, GoalContributionViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            if (model.Amount <= 0) throw new BusinessRuleException("Enter an amount greater than zero.");
            await goalService.AddContributionAsync(id, model.Withdraw ? -model.Amount : model.Amount, cancellationToken);
            TempData["SuccessMessage"] = model.Withdraw ? "Amount withdrawn from the goal." : "Amount added to the goal.";
        }
        catch (EntityNotFoundException) { return NotFound(); }
        catch (BusinessRuleException exception) { TempData["ErrorMessage"] = exception.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var goal = await goalService.GetByIdAsync(id, cancellationToken);
        return goal is null ? NotFound() : View(ToViewModel(goal));
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        try { await goalService.DeleteAsync(id, cancellationToken); }
        catch (EntityNotFoundException) { return NotFound(); }
        TempData["SuccessMessage"] = "Goal deleted.";
        return RedirectToAction(nameof(Index));
    }

    private static GoalViewModel ToViewModel(Goal goal) => new()
    {
        Id = goal.Id, Name = goal.Name, TargetAmount = goal.TargetAmount, SavedAmount = goal.SavedAmount, Currency = goal.Currency, TargetDate = goal.TargetDate,
        BannerColor = goal.BannerColor, BannerImagePath = GoalBanners.Find(goal.BannerImage)?.WebPath, Note = goal.Note
    };

    private static Goal ToEntity(GoalFormViewModel model) => new()
    {
        Id = model.Id, Name = model.Name, TargetAmount = model.TargetAmount, SavedAmount = model.SavedAmount, Currency = model.Currency, TargetDate = model.TargetDate,
        BannerColor = model.BannerColor, BannerImage = string.IsNullOrWhiteSpace(model.BannerImage) ? null : model.BannerImage, Note = model.Note
    };
}
