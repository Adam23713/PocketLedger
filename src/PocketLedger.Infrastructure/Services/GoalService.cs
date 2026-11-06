using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class GoalService(PocketLedgerDbContext dbContext, TimeProvider timeProvider) : IGoalService
{
    public async Task<IReadOnlyList<Goal>> GetAllAsync(CancellationToken cancellationToken)
    {
        var goals = await dbContext.Goals.AsNoTracking().ToListAsync(cancellationToken);
        return goals.OrderBy(goal => goal.SavedAmount >= goal.TargetAmount).ThenBy(goal => goal.TargetDate ?? DateOnly.MaxValue).ThenBy(goal => goal.Name).ToList();
    }

    public Task<Goal?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => dbContext.Goals.AsNoTracking().SingleOrDefaultAsync(goal => goal.Id == id, cancellationToken);

    public async Task<Goal> CreateAsync(Goal goal, CancellationToken cancellationToken)
    {
        Normalize(goal);
        GoalRules.Validate(goal);
        goal.Id = goal.Id == Guid.Empty ? Guid.NewGuid() : goal.Id;
        goal.CreatedAt = timeProvider.GetUtcNow();
        dbContext.Goals.Add(goal);
        await dbContext.SaveChangesAsync(cancellationToken);
        return goal;
    }

    public async Task UpdateAsync(Goal goal, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Goals.SingleOrDefaultAsync(item => item.Id == goal.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Goal not found.");
        Normalize(goal);
        GoalRules.Validate(goal);
        existing.Name = goal.Name;
        existing.TargetAmount = goal.TargetAmount;
        existing.SavedAmount = goal.SavedAmount;
        existing.Currency = goal.Currency;
        existing.TargetDate = goal.TargetDate;
        existing.BannerColor = goal.BannerColor;
        existing.BannerImage = goal.BannerImage;
        existing.Note = goal.Note;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var goal = await dbContext.Goals.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Goal not found.");
        dbContext.Goals.Remove(goal);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Goal> AddContributionAsync(Guid id, decimal amount, CancellationToken cancellationToken)
    {
        var goal = await dbContext.Goals.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Goal not found.");
        goal.SavedAmount = GoalRules.ApplyContribution(goal.SavedAmount, decimal.Round(amount, 4));
        await dbContext.SaveChangesAsync(cancellationToken);
        return goal;
    }

    private static void Normalize(Goal goal)
    {
        goal.Name = goal.Name?.Trim() ?? string.Empty;
        goal.Currency = goal.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        goal.BannerColor = goal.BannerColor?.Trim().ToLowerInvariant() ?? GoalBanners.DefaultColor;
        goal.BannerImage = string.IsNullOrWhiteSpace(goal.BannerImage) ? null : goal.BannerImage.Trim();
        goal.Note = string.IsNullOrWhiteSpace(goal.Note) ? null : goal.Note.Trim();
    }
}
