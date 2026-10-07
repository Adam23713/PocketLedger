using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Admin;
using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Controllers;

[Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(UserManager<ApplicationUser> userManager, ISuspiciousRequestEventReader suspiciousRequestEvents, ICrowdSecDecisionReader crowdSecDecisions) : Controller
{
    private const int PageSize = 10;

    [Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName), HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        var query = userManager.Users.AsNoTracking();
        var totalUsers = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalUsers / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);
        var users = await query.OrderBy(user => user.LastSuccessfulLoginAtUtc == null).ThenByDescending(user => user.LastSuccessfulLoginAtUtc)
            .ThenBy(user => user.NormalizedUserName).ThenBy(user => user.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(user => new AdminUserOverviewItem(user.UserName ?? "Unavailable", user.LastSuccessfulLoginAtUtc)).ToListAsync(cancellationToken);
        var suspiciousRequestsTask = suspiciousRequestEvents.GetRecentAsync(cancellationToken: cancellationToken);
        var crowdSecBansTask = crowdSecDecisions.GetActiveBansAsync(cancellationToken);
        await Task.WhenAll(suspiciousRequestsTask, crowdSecBansTask);
        return View(new AdminDashboardViewModel(users, page, totalPages, totalUsers, await suspiciousRequestsTask, await crowdSecBansTask));
    }
}
