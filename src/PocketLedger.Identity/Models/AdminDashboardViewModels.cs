using PocketLedger.Security;

namespace PocketLedger.Models.ViewModels.Admin;

public sealed record AdminUserOverviewItem(string Username, DateTimeOffset? LastSuccessfulLoginAtUtc);

public sealed record AdminDashboardViewModel(IReadOnlyList<AdminUserOverviewItem> Users, int Page, int TotalPages, int TotalUsers, SuspiciousRequestEventsResult SuspiciousRequests);
