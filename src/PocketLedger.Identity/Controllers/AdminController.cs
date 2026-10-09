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
public sealed class AdminController(UserManager<ApplicationUser> userManager, IRequestTelemetryReader requestTelemetry, ISuspiciousRequestEventReader suspiciousRequestEvents,
    ICrowdSecDecisionReader crowdSecDecisions, TimeProvider timeProvider) : Controller
{
    private const int PageSize = 10;
    private const int BucketMinutes = 5;
    private const int DefaultLookbackHours = 24;
    private static readonly IReadOnlyList<int> AllowedLookbackHours = [3, 6, 12, 24, 48];

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
        return View(new AdminDashboardViewModel(users, page, totalPages, totalUsers, new RequestActivityViewModel(AllowedLookbackHours, DefaultLookbackHours),
            await suspiciousRequestsTask, await crowdSecBansTask));
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
}
