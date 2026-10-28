using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Models.ViewModels.Admin;

public sealed record AdminUserOverviewItem(Guid Id, string Username, string? Email, DateTimeOffset? LastSuccessfulLoginAtUtc, bool IsAccessBlocked, bool IsBootstrapAdministrator, DateTimeOffset? LockoutEndUtc = null, string? RegistrationProvider = null);

public sealed record RequestActivityUserOption(Guid Id, string Username);

public sealed record RequestActivityViewModel(IReadOnlyList<int> AllowedLookbackHours, int SelectedLookbackHours);

public sealed record RequestActivityPoint(DateTimeOffset StartedAtUtc, double AverageRequestsPerMinute, long TotalRequests, long AuthenticatedRequests, long AnonymousRequests, long? SelectedUserRequests);

public sealed record RequestActivityDataViewModel(bool IsAvailable, bool IsPartial, bool IsEmpty, int LookbackHours, Guid? UserId, string SeriesLabel, long SeriesRequests,
    long TotalRequests, long AnonymousRequests, IReadOnlyList<RequestActivityUserOption> Users, IReadOnlyList<RequestActivityPoint> Points);

public sealed record AdminDashboardViewModel(IReadOnlyList<AdminUserOverviewItem> Users, int Page, int TotalPages, int TotalUsers, RegistrationStatus Registration,
    RequestActivityViewModel RequestActivity, SuspiciousRequestEventsResult SuspiciousRequests, CrowdSecBansViewModel CrowdSecBans);

public sealed record CrowdSecBanRow(string Address, string Scope, string Scenario, string Origin, string Action, TimeSpan? Remaining, string? RemainingText, int BanCount);

public sealed record CrowdSecBansViewModel(bool IsAvailable, int TotalBans, bool Merge, int UsersPage, TablePage<CrowdSecBanRow> Table);
