using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels;
using PocketLedger.Models.ViewModels.Admin;
using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Controllers;

[Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminController(UserManager<ApplicationUser> userManager, IRequestTelemetryReader requestTelemetry, ISuspiciousRequestEventReader suspiciousRequestEvents,
    ICrowdSecDecisionReader crowdSecDecisions, TimeProvider timeProvider, IUserAccessPropagation accessPropagation,
    IdentityDbContext dbContext, IRegistrationSettingsService registrationSettings, IAuthenticationAuditService audit, IUserAccountDeletionService accountDeletion,
    ILogger<AdminController> logger) : Controller
{
    private const int PageSize = 10;
    private const int BucketMinutes = 5;
    private const int DefaultLookbackHours = 24;
    private static readonly IReadOnlyList<int> AllowedLookbackHours = [3, 6, 12, 24, 48];

    [Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName), HttpGet]
    public async Task<IActionResult> Index(int page = 1, string? bansSort = null, bool bansDesc = false, bool bansMerge = false, int bansPage = 1,
        CancellationToken cancellationToken = default)
    {
        var query = userManager.Users.AsNoTracking();
        var now = timeProvider.GetUtcNow();
        var totalUsers = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalUsers / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);
        var users = await query.OrderBy(user => user.LastSuccessfulLoginAtUtc == null).ThenByDescending(user => user.LastSuccessfulLoginAtUtc)
            .ThenBy(user => user.NormalizedUserName).ThenBy(user => user.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(user => new AdminUserOverviewItem(user.Id, user.UserName ?? "Unavailable", user.Email, user.LastSuccessfulLoginAtUtc, user.IsAccessBlocked,
                user.IsBootstrapAdministrator, user.LockoutEnd > now ? user.LockoutEnd : null, user.RegistrationProvider)).ToListAsync(cancellationToken);
        var suspiciousRequestsTask = suspiciousRequestEvents.GetRecentAsync(cancellationToken: cancellationToken);
        var crowdSecBansTask = crowdSecDecisions.GetActiveBansAsync(cancellationToken);
        await Task.WhenAll(suspiciousRequestsTask, crowdSecBansTask);
        var registration = await registrationSettings.GetStatusAsync(cancellationToken);
        return View(new AdminDashboardViewModel(users, page, totalPages, totalUsers, registration, new RequestActivityViewModel(AllowedLookbackHours, DefaultLookbackHours),
            await suspiciousRequestsTask, BuildCrowdSecBans(await crowdSecBansTask, page, bansSort, bansDesc, bansMerge, bansPage)));
    }

    private const int BansPageSize = 15;

    private static readonly IReadOnlyDictionary<string, Func<CrowdSecBanRow, IComparable?>> BanSortKeys = new Dictionary<string, Func<CrowdSecBanRow, IComparable?>>(StringComparer.Ordinal)
    {
        ["address"] = row => row.Address, ["scope"] = row => row.Scope, ["scenario"] = row => row.Scenario, ["origin"] = row => row.Origin,
        ["action"] = row => row.Action, ["remaining"] = row => row.Remaining, ["count"] = row => row.BanCount
    };

    private static CrowdSecBansViewModel BuildCrowdSecBans(CrowdSecBanDecisionsResult result, int usersPage, string? sort, bool descending, bool merge, int page)
    {
        var rows = result.Decisions.Select(item =>
        {
            var remaining = CrowdSecDuration.Parse(item.RemainingDuration);
            return new CrowdSecBanRow(item.Address, item.Scope, item.Scenario, item.Origin, item.Action, remaining, remaining is { } value ? CrowdSecDuration.Format(value) : item.RemainingDuration, 1);
        });
        if (merge)
        {
            // One row per address; the remaining time is the ban that expires first.
            rows = rows.GroupBy(row => row.Address, StringComparer.OrdinalIgnoreCase).Select(group =>
            {
                var first = group.OrderBy(row => row.Remaining is null).ThenBy(row => row.Remaining).First();
                static string Join(IEnumerable<string> values) => string.Join(", ", values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
                return new CrowdSecBanRow(group.Key, Join(group.Select(row => row.Scope)), Join(group.Select(row => row.Scenario)), Join(group.Select(row => row.Origin)),
                    Join(group.Select(row => row.Action)), first.Remaining, first.RemainingText, group.Count());
            });
        }
        var table = TablePaging.Apply(rows, sort, descending, BanSortKeys, "address", page, BansPageSize);
        return new CrowdSecBansViewModel(result.IsAvailable, result.Decisions.Count, merge, usersPage, table);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRegistration(bool isOpen, int? maximumUserCount, int page = 1, CancellationToken cancellationToken = default)
    {
        if (maximumUserCount is < 1 or > PublicRegistrationOptions.MaximumAllowedUserCount)
        {
            TempData["AdminWarning"] = $"The account limit must be between 1 and {PublicRegistrationOptions.MaximumAllowedUserCount:N0}, or left empty for no limit.";
            return RedirectToAction(nameof(Index), new { page });
        }
        await registrationSettings.UpdateAsync(isOpen, maximumUserCount, cancellationToken);
        await audit.WriteAsync("RegistrationSettingsChanged", "Success", Guid.TryParse(userManager.GetUserId(User), out var adminId) ? adminId : null,
            metadata: $"IsOpen={isOpen};MaximumUserCount={maximumUserCount?.ToString() ?? "Unlimited"}", cancellationToken: cancellationToken);
        var status = await registrationSettings.GetStatusAsync(cancellationToken);
        TempData["AdminMessage"] = !status.IsOpen ? "Registration settings saved. Public registration is closed."
            : status.RemainingSlots is { } remaining ? $"Registration settings saved. Public registration is open with {remaining} of {status.MaximumUserCount} accounts available."
            : "Registration settings saved. Public registration is open without an account limit.";
        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(Guid userId, int page = 1, CancellationToken cancellationToken = default)
    {
        var user = await FindManageableUserAsync(userId);
        if (user is null) return NotFound();
        await BlockInIdentityAsync(user, cancellationToken);
        try
        {
            await accessPropagation.BlockAsync(user.Id, cancellationToken);
            TempData["AdminMessage"] = $"{user.UserName} has been blocked and all of their sessions were terminated.";
        }
        catch (Exception exception) when (IsPropagationFailure(exception, cancellationToken))
        {
            logger.LogError(exception, "Blocking user {UserId} could not be propagated to the API or web app.", user.Id);
            TempData["AdminWarning"] = $"{user.UserName} is blocked and can no longer sign in, but the API or web app could not be reached. " +
                $"Their remaining sessions are terminated automatically within {UserAccessBlockSynchronizer.Interval.TotalSeconds:0} seconds once it is reachable.";
        }
        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unblock(Guid userId, int page = 1, CancellationToken cancellationToken = default)
    {
        var user = await FindManageableUserAsync(userId);
        if (user is null) return NotFound();
        user.IsAccessBlocked = false;
        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("The user could not be unblocked.");
        try
        {
            await accessPropagation.UnblockAsync(user.Id, cancellationToken);
            TempData["AdminMessage"] = $"{user.UserName} has been unblocked.";
        }
        catch (Exception exception) when (IsPropagationFailure(exception, cancellationToken))
        {
            logger.LogError(exception, "Unblocking user {UserId} could not be propagated to the API.", user.Id);
            TempData["AdminWarning"] = $"{user.UserName} has been unblocked in Identity, but the API could not be reached. API access is restored automatically within a few minutes.";
        }
        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(Guid userId, int page = 1, CancellationToken cancellationToken = default)
    {
        var user = await FindManageableUserAsync(userId);
        if (user is null) return NotFound();
        if (!await userManager.IsLockedOutAsync(user)) return BadRequest("The user is not locked out.");
        var result = await userManager.SetLockoutEndDateAsync(user, null);
        if (result.Succeeded) result = await userManager.ResetAccessFailedCountAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("The user could not be unlocked.");
        await audit.WriteAsync("LoginLockoutCleared", "Success", user.Id, user.NormalizedUserName,
            metadata: $"ClearedBy={userManager.GetUserId(User)}", cancellationToken: cancellationToken);
        TempData["AdminMessage"] = $"{user.UserName} can sign in again.";
        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid userId, string? confirmation, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            TempData["AdminWarning"] = "The user was not deleted because the confirmation text did not match DELETE.";
            return RedirectToAction(nameof(Index), new { page });
        }
        var user = await FindManageableUserAsync(userId);
        if (user is null) return NotFound();
        await accountDeletion.DeleteAsync(user, cancellationToken);

        TempData["AdminMessage"] = $"{user.UserName} has been deleted.";
        return RedirectToAction(nameof(Index), new { page });
    }

    [Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName), HttpGet]
    public async Task<IActionResult> RequestActivity(int hours = DefaultLookbackHours, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !AllowedLookbackHours.Contains(hours)) return BadRequest(new { Error = "The lookback must be 3, 6, 12, 24, or 48 hours." });

        string seriesLabel = "All activity";
        if (userId is { } selectedUserId)
        {
            var selectedUser = await userManager.Users.AsNoTracking().Where(user => user.Id == selectedUserId)
                .Select(user => new { user.UserName }).SingleOrDefaultAsync(cancellationToken);
            if (selectedUser is null) return NotFound(new { Error = "The selected user is no longer available." });
            seriesLabel = selectedUser.UserName ?? "Unavailable user";
        }

        var toUtc = timeProvider.GetUtcNow();
        var fromUtc = toUtc.AddHours(-hours);
        var statistics = await requestTelemetry.QueryAsync(fromUtc, toUtc, userId, cancellationToken);
        var points = statistics.Buckets.Select(bucket => new RequestActivityPoint(bucket.StartedAtUtc,
            (userId is null ? bucket.TotalRequests : bucket.SelectedUserRequests ?? 0) / (double)BucketMinutes,
            bucket.TotalRequests, bucket.AuthenticatedRequests, bucket.AnonymousRequests, bucket.SelectedUserRequests)).ToArray();
        var selectedRequestCount = userId is null ? statistics.TotalRequests : statistics.SelectedUserRequests ?? 0;
        var expectedFromUtc = RequestTelemetryBuckets.Start(fromUtc, BucketMinutes);
        var expectedToUtc = RequestTelemetryBuckets.Start(toUtc.AddMinutes(BucketMinutes).AddTicks(-1), BucketMinutes);
        var isPartial = IsPartial(statistics, userId, expectedFromUtc, expectedToUtc);
        var users = await userManager.Users.AsNoTracking().OrderBy(user => user.NormalizedUserName).ThenBy(user => user.Id)
            .Select(user => new RequestActivityUserOption(user.Id, user.UserName ?? "Unavailable")).ToListAsync(cancellationToken);

        return Json(new RequestActivityDataViewModel(statistics.IsAvailable, isPartial, selectedRequestCount == 0, hours, userId, seriesLabel, selectedRequestCount,
            statistics.TotalRequests, statistics.AnonymousRequests, users, points));
    }

    private static bool IsPartial(RequestTelemetryStatistics statistics, Guid? requestedUserId, DateTimeOffset expectedFromUtc, DateTimeOffset expectedToUtc)
    {
        if (statistics.UserId != requestedUserId || statistics.FromUtc != expectedFromUtc || statistics.ToUtc != expectedToUtc) return true;
        var expectedBucketCount = (int)((statistics.ToUtc - statistics.FromUtc).TotalMinutes / BucketMinutes);
        if (statistics.Buckets.Count != expectedBucketCount) return true;
        return statistics.Buckets.Where((bucket, index) => bucket.StartedAtUtc != statistics.FromUtc.AddMinutes(index * BucketMinutes)).Any();
    }

    private async Task<ApplicationUser?> FindManageableUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null || user.IsBootstrapAdministrator || user.Id.ToString() == userManager.GetUserId(User) ? null : user;
    }

    private async Task BlockInIdentityAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // The database flag is the source of truth: sign-in, token issuance and the Identity cookie check it on every request.
        user.IsAccessBlocked = true;
        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) throw new InvalidOperationException("The user could not be blocked.");
        await RevokeIdentitySessionsAsync(user.Id, cancellationToken);
    }

    private static bool IsPropagationFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    // Revokes every OpenIddict token and authorization of the user, so no refresh token can mint new access tokens.
    private async Task RevokeIdentitySessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().Where(item => item.Subject == subject && item.Status != OpenIddictConstants.Statuses.Revoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);
            await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(item => item.Subject == subject && item.Status != OpenIddictConstants.Statuses.Revoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);
            return;
        }

        foreach (var token in await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().Where(item => item.Subject == subject).ToListAsync(cancellationToken))
            token.Status = OpenIddictConstants.Statuses.Revoked;
        foreach (var authorization in await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(item => item.Subject == subject).ToListAsync(cancellationToken))
            authorization.Status = OpenIddictConstants.Statuses.Revoked;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

}
