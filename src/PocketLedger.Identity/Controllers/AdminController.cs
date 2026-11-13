using PocketLedger.Localization;
using System.Net;
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
    IRequestClientActivityReader clientActivity, ICrowdSecBanManager banManager, IClientIpAddressResolver clientIpAddresses, ILogger<AdminController> logger) : Controller
{
    private const int PageSize = 10;
    private const int BucketMinutes = 5;
    private const int DefaultLookbackHours = 24;
    private static readonly IReadOnlyList<int> AllowedLookbackHours = [3, 6, 12, 24, 48];
    private static readonly IReadOnlyList<int> AllowedBanHours = [1, 24, 168, 720];

    [Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName), HttpGet]
    public async Task<IActionResult> Index(int page = 1, string? bansSort = null, bool bansDesc = false, bool bansMerge = false, int bansPage = 1, string? ip = null,
        CancellationToken cancellationToken = default)
    {
        RequestClientAddress.TryNormalize(ip, out var selectedClientIpAddress);
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
        return View(new AdminDashboardViewModel(users, page, totalPages, totalUsers, registration,
            new RequestActivityViewModel(AllowedLookbackHours, DefaultLookbackHours, selectedClientIpAddress, page, banManager.IsConfigured, AllowedBanHours),
            await suspiciousRequestsTask, BuildCrowdSecBans(await crowdSecBansTask, page, bansSort, bansDesc, bansMerge, bansPage, banManager.IsConfigured)));
    }

    private const int BansPageSize = 15;

    private static readonly IReadOnlyDictionary<string, Func<CrowdSecBanRow, IComparable?>> BanSortKeys = new Dictionary<string, Func<CrowdSecBanRow, IComparable?>>(StringComparer.Ordinal)
    {
        ["address"] = row => row.Address, ["scope"] = row => row.Scope, ["scenario"] = row => row.Scenario, ["origin"] = row => row.Origin,
        ["action"] = row => row.Action, ["remaining"] = row => row.Remaining, ["count"] = row => row.BanCount
    };

    private static CrowdSecBansViewModel BuildCrowdSecBans(CrowdSecBanDecisionsResult result, int usersPage, string? sort, bool descending, bool merge, int page, bool canManageBans)
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
        return new CrowdSecBansViewModel(result.IsAvailable, result.Decisions.Count, merge, usersPage, table, canManageBans);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BanIp(string? ip, int durationHours, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!RequestClientAddress.TryNormalize(ip, out var address))
        {
            TempData["AdminWarning"] = CommonText.Get("No ban was created because the address is not a complete IPv4 or IPv6 address.");
            return RedirectToAction(nameof(Index), new { page });
        }
        var refusal = ClientAddressPolicy.GetBanRefusal(address!, IsCurrentAdministratorAddress(address!));
        if (refusal is null && !AllowedBanHours.Contains(durationHours)) refusal = CommonText.Get("The selected ban duration is not supported.");
        if (refusal is null && !banManager.IsConfigured) refusal = CommonText.Get("Banning is not configured: set CROWDSEC_IDENTITY_MACHINE_PASSWORD on the server.");
        if (refusal is not null)
        {
            TempData["AdminWarning"] = CommonText.Format("No ban was created. {0}", CommonText.Get(refusal));
            return RedirectToAction(nameof(Index), null, new { page, ip = address }, "request-activity-heading");
        }

        if (await banManager.BanAsync(address!, TimeSpan.FromHours(durationHours), cancellationToken))
        {
            await audit.WriteAsync("ClientAddressBanned", "Success", Guid.TryParse(userManager.GetUserId(User), out var adminId) ? adminId : null,
                metadata: $"Address={address};DurationHours={durationHours}", cancellationToken: cancellationToken);
            TempData["AdminMessage"] = CommonText.Format("{0} is banned for {1}. Every PocketLedger site starts refusing it within a few seconds.", address, FormatBanDuration(durationHours));
        }
        else
        {
            TempData["AdminWarning"] = CommonText.Format("{0} was not banned because CrowdSec could not be reached or refused the request.", address);
        }
        return RedirectToAction(nameof(Index), null, new { page, ip = address }, "request-activity-heading");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnbanIp(string? address, int page = 1, CancellationToken cancellationToken = default)
    {
        var text = address?.Trim();
        var isValid = RequestClientAddress.TryNormalize(text, out _) || (text?.Contains('/') == true && IPNetwork.TryParse(text, out _));
        if (!isValid)
        {
            TempData["AdminWarning"] = CommonText.Get("No ban was removed because the address is not valid.");
            return RedirectToAction(nameof(Index), new { page });
        }

        if (await banManager.RemoveAsync(text!, cancellationToken))
        {
            await audit.WriteAsync("ClientAddressBanRemoved", "Success", Guid.TryParse(userManager.GetUserId(User), out var adminId) ? adminId : null,
                metadata: $"Address={text}", cancellationToken: cancellationToken);
            TempData["AdminMessage"] = CommonText.Format("The bans on {0} were removed. Every PocketLedger site accepts it again within a few seconds.", text);
        }
        else
        {
            TempData["AdminWarning"] = CommonText.Format("The bans on {0} were not removed because CrowdSec could not be reached or refused the request.", text);
        }
        return RedirectToAction(nameof(Index), new { page });
    }

    private static string FormatBanDuration(int hours) => hours switch { 1 => CommonText.Get("1 hour"), < 24 => CommonText.Format("{0} hours", hours), 24 => CommonText.Get("1 day"), _ => CommonText.Format("{0} days", hours / 24) };

    private bool IsCurrentAdministratorAddress(string address)
    {
        // Both views of the caller count: the forwarded headers Identity trusts for audit data and the connection address after forwarded-header processing.
        RequestClientAddress.TryNormalize(clientIpAddresses.GetClientIpAddress(HttpContext), out var forwarded);
        var connection = HttpContext.Connection.RemoteIpAddress is { } remote ? RequestClientAddress.Normalize(remote) : null;
        return address == forwarded || address == connection;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRegistration(bool isOpen, int? maximumUserCount, int page = 1, CancellationToken cancellationToken = default)
    {
        if (maximumUserCount is < 1 or > PublicRegistrationOptions.MaximumAllowedUserCount)
        {
            TempData["AdminWarning"] = CommonText.Format("The account limit must be between 1 and {0:N0}, or left empty for no limit.", PublicRegistrationOptions.MaximumAllowedUserCount);
            return RedirectToAction(nameof(Index), new { page });
        }
        await registrationSettings.UpdateAsync(isOpen, maximumUserCount, cancellationToken);
        await audit.WriteAsync("RegistrationSettingsChanged", "Success", Guid.TryParse(userManager.GetUserId(User), out var adminId) ? adminId : null,
            metadata: $"IsOpen={isOpen};MaximumUserCount={maximumUserCount?.ToString() ?? "Unlimited"}", cancellationToken: cancellationToken);
        var status = await registrationSettings.GetStatusAsync(cancellationToken);
        TempData["AdminMessage"] = !status.IsOpen ? CommonText.Get("Registration settings saved. Public registration is closed.")
            : status.RemainingSlots is { } remaining ? CommonText.Format("Registration settings saved. Public registration is open with {0} of {1} accounts available.", remaining, status.MaximumUserCount)
            : CommonText.Get("Registration settings saved. Public registration is open without an account limit.");
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
            TempData["AdminMessage"] = CommonText.Format("{0} has been blocked and all of their sessions were terminated.", user.UserName);
        }
        catch (Exception exception) when (IsPropagationFailure(exception, cancellationToken))
        {
            logger.LogError(exception, "Blocking user {UserId} could not be propagated to the API or web app.", user.Id);
            TempData["AdminWarning"] = CommonText.Format("{0} is blocked and can no longer sign in, but the API or web app could not be reached. Their remaining sessions are terminated automatically within {1:0} seconds once it is reachable.", user.UserName, UserAccessBlockSynchronizer.Interval.TotalSeconds);
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
            TempData["AdminMessage"] = CommonText.Format("{0} has been unblocked.", user.UserName);
        }
        catch (Exception exception) when (IsPropagationFailure(exception, cancellationToken))
        {
            logger.LogError(exception, "Unblocking user {UserId} could not be propagated to the API.", user.Id);
            TempData["AdminWarning"] = CommonText.Format("{0} has been unblocked in Identity, but the API could not be reached. API access is restored automatically within a few minutes.", user.UserName);
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
        TempData["AdminMessage"] = CommonText.Format("{0} can sign in again.", user.UserName);
        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid userId, string? confirmation, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            TempData["AdminWarning"] = CommonText.Get("The user was not deleted because the confirmation text did not match DELETE.");
            return RedirectToAction(nameof(Index), new { page });
        }
        var user = await FindManageableUserAsync(userId);
        if (user is null) return NotFound();
        await accountDeletion.DeleteAsync(user, cancellationToken);

        TempData["AdminMessage"] = CommonText.Format("{0} has been deleted.", user.UserName);
        return RedirectToAction(nameof(Index), new { page });
    }

    [Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName), HttpGet]
    public async Task<IActionResult> RequestActivity(int hours = DefaultLookbackHours, Guid? userId = null, string? ip = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !AllowedLookbackHours.Contains(hours)) return BadRequest(new { Error = "The lookback must be 3, 6, 12, 24, or 48 hours." });
        string? clientAddress = null;
        if (!string.IsNullOrWhiteSpace(ip) && !RequestClientAddress.TryNormalize(ip, out clientAddress)) return BadRequest(new { Error = "Enter a complete IPv4 or IPv6 address." });
        if (clientAddress is not null && userId is not null) return BadRequest(new { Error = "Filter by an activity owner or a client address, not both." });

        string seriesLabel = clientAddress ?? "All activity";
        if (userId is { } selectedUserId)
        {
            var selectedUser = await userManager.Users.AsNoTracking().Where(user => user.Id == selectedUserId)
                .Select(user => new { user.UserName }).SingleOrDefaultAsync(cancellationToken);
            if (selectedUser is null) return NotFound(new { Error = "The selected user is no longer available." });
            seriesLabel = selectedUser.UserName ?? "Unavailable user";
        }

        var toUtc = timeProvider.GetUtcNow();
        var fromUtc = toUtc.AddHours(-hours);
        var statisticsTask = requestTelemetry.QueryAsync(fromUtc, toUtc, userId, cancellationToken);
        var clientsTask = clientActivity.QueryAsync(fromUtc, toUtc, clientAddress, cancellationToken);
        var bansTask = crowdSecDecisions.GetActiveBansAsync(cancellationToken);
        await Task.WhenAll(statisticsTask, clientsTask, bansTask);
        var statistics = await statisticsTask;
        var clients = await clientsTask;
        var bans = await bansTask;
        var clientBuckets = clients.Buckets.ToDictionary(bucket => bucket.StartedAtUtc);
        var points = statistics.Buckets.Select(bucket =>
        {
            var client = clientBuckets.GetValueOrDefault(bucket.StartedAtUtc);
            var series = clientAddress is not null ? client?.SelectedClientRequests ?? 0 : userId is null ? bucket.TotalRequests : bucket.SelectedUserRequests ?? 0;
            var failed = clientAddress is not null ? client?.SelectedClientFailedRequests ?? 0 : client?.FailedRequests ?? 0;
            return new RequestActivityPoint(bucket.StartedAtUtc, series / (double)BucketMinutes, bucket.TotalRequests, bucket.AuthenticatedRequests, bucket.AnonymousRequests, bucket.SelectedUserRequests,
                series, failed, client?.DistinctClients ?? 0, client?.TopClientIpAddress, client?.TopClientRequests ?? 0);
        }).ToArray();
        var selectedRequestCount = points.Sum(point => point.SeriesRequests);
        var clientRows = clients.Clients.Select(item => new RequestActivityClient(item.ClientIpAddress, ClientAddressPolicy.Classify(item.ClientIpAddress).ToString(), item.TotalRequests, item.FailedRequests,
            item.FirstSeenUtc, item.LastSeenUtc, bans.Decisions.Any(ban => ClientAddressPolicy.IsCoveredBy(item.ClientIpAddress, ban.Address)), IsCurrentAdministratorAddress(item.ClientIpAddress))).ToArray();
        var expectedFromUtc = RequestTelemetryBuckets.Start(fromUtc, BucketMinutes);
        var expectedToUtc = RequestTelemetryBuckets.Start(toUtc.AddMinutes(BucketMinutes).AddTicks(-1), BucketMinutes);
        var isPartial = IsPartial(statistics, userId, expectedFromUtc, expectedToUtc);
        var users = await userManager.Users.AsNoTracking().OrderBy(user => user.NormalizedUserName).ThenBy(user => user.Id)
            .Select(user => new RequestActivityUserOption(user.Id, user.UserName ?? "Unavailable")).ToListAsync(cancellationToken);

        return Json(new RequestActivityDataViewModel(statistics.IsAvailable, isPartial, selectedRequestCount == 0, hours, userId, seriesLabel, selectedRequestCount,
            statistics.TotalRequests, statistics.AnonymousRequests, users, points, clientAddress, clients.IsAvailable, clients.TotalClients, points.Sum(point => point.FailedRequests), clients.OmittedRequests, clientRows));
    }

    [Authorize(Policy = BootstrapAdministratorAuthorization.PolicyName), HttpGet]
    public async Task<IActionResult> RequestClient(string? ip, int hours = DefaultLookbackHours, CancellationToken cancellationToken = default)
    {
        if (!AllowedLookbackHours.Contains(hours)) return BadRequest(new { Error = "The lookback must be 3, 6, 12, 24, or 48 hours." });
        if (!RequestClientAddress.TryNormalize(ip, out var address)) return BadRequest(new { Error = "Enter a complete IPv4 or IPv6 address." });

        var toUtc = timeProvider.GetUtcNow();
        var detailsTask = clientActivity.GetDetailsAsync(address!, toUtc.AddHours(-hours), toUtc, cancellationToken);
        var bansTask = crowdSecDecisions.GetActiveBansAsync(cancellationToken);
        await Task.WhenAll(detailsTask, bansTask);
        var details = await detailsTask;
        var bans = await bansTask;

        var userIds = details.UserIds.ToArray();
        var usernames = userIds.Length == 0 ? [] : await userManager.Users.AsNoTracking().Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.UserName }).ToDictionaryAsync(user => user.Id, user => user.UserName, cancellationToken);
        var isCurrentAdministrator = IsCurrentAdministratorAddress(address!);
        return Json(new RequestClientDetailsViewModel(details.IsAvailable, address!, ClientAddressPolicy.Classify(address!).ToString(), isCurrentAdministrator, bans.IsAvailable,
            bans.Decisions.Where(ban => ClientAddressPolicy.IsCoveredBy(address!, ban.Address))
                .Select(ban => new RequestClientBan(ban.Address, ban.Scenario, ban.Origin, CrowdSecDuration.Parse(ban.RemainingDuration) is { } remaining ? CrowdSecDuration.Format(remaining) : ban.RemainingDuration)).ToArray(),
            ClientAddressPolicy.GetBanRefusal(address!, isCurrentAdministrator),
            userIds.Select(id => new RequestClientUser(id, usernames.GetValueOrDefault(id) ?? "Deleted account")).ToArray(), details.Endpoints, details.OmittedRequests, details.IsPossiblyIncomplete));
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
