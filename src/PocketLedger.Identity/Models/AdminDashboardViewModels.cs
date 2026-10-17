using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Models.ViewModels.Admin;

public sealed record AdminUserOverviewItem(string Username, DateTimeOffset? LastSuccessfulLoginAtUtc);

public sealed record RequestActivityUserOption(Guid Id, string Username);

public sealed record RequestActivityViewModel(IReadOnlyList<int> AllowedLookbackHours, int SelectedLookbackHours);

public sealed record RequestActivityPoint(DateTimeOffset StartedAtUtc, double AverageRequestsPerMinute, long TotalRequests, long AuthenticatedRequests, long AnonymousRequests, long? SelectedUserRequests);

public sealed record RequestActivityDataViewModel(bool IsAvailable, bool IsPartial, bool IsEmpty, int LookbackHours, Guid? UserId, string SeriesLabel, long SeriesRequests,
    long TotalRequests, long AnonymousRequests, IReadOnlyList<RequestActivityUserOption> Users, IReadOnlyList<RequestActivityPoint> Points);

public sealed record AdminDashboardViewModel(IReadOnlyList<AdminUserOverviewItem> Users, int Page, int TotalPages, int TotalUsers, RequestActivityViewModel RequestActivity,
    SuspiciousRequestEventsResult SuspiciousRequests, CrowdSecBanDecisionsResult CrowdSecBans);
