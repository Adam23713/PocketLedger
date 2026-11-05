using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Models.ViewModels.Admin;

public sealed record AdminUserOverviewItem(Guid Id, string Username, string? Email, DateTimeOffset? LastSuccessfulLoginAtUtc, bool IsAccessBlocked, bool IsBootstrapAdministrator, DateTimeOffset? LockoutEndUtc = null, string? RegistrationProvider = null);

public sealed record RequestActivityUserOption(Guid Id, string Username);

public sealed record RequestActivityViewModel(IReadOnlyList<int> AllowedLookbackHours, int SelectedLookbackHours, string? SelectedClientIpAddress = null, int UsersPage = 1,
    bool CanManageBans = false, IReadOnlyList<int>? AllowedBanHours = null);

public sealed record RequestActivityPoint(DateTimeOffset StartedAtUtc, double AverageRequestsPerMinute, long TotalRequests, long AuthenticatedRequests, long AnonymousRequests, long? SelectedUserRequests,
    long SeriesRequests = 0, long FailedRequests = 0, int DistinctClients = 0, string? TopClientIpAddress = null, long TopClientRequests = 0);

public sealed record RequestActivityClient(string Address, string Kind, long Requests, long FailedRequests, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, bool IsBanned, bool IsCurrentAdministrator);

public sealed record RequestActivityDataViewModel(bool IsAvailable, bool IsPartial, bool IsEmpty, int LookbackHours, Guid? UserId, string SeriesLabel, long SeriesRequests,
    long TotalRequests, long AnonymousRequests, IReadOnlyList<RequestActivityUserOption> Users, IReadOnlyList<RequestActivityPoint> Points,
    string? ClientIpAddress = null, bool IsClientActivityAvailable = false, int TotalClients = 0, long SeriesFailedRequests = 0, long OmittedClientRequests = 0, IReadOnlyList<RequestActivityClient>? Clients = null);

public sealed record RequestClientUser(Guid Id, string Username);

public sealed record RequestClientBan(string Address, string Scenario, string Origin, string? RemainingText);

public sealed record RequestClientDetailsViewModel(bool IsAvailable, string Address, string Kind, bool IsCurrentAdministrator, bool AreBansAvailable, IReadOnlyList<RequestClientBan> Bans, string? BanRefusal,
    IReadOnlyList<RequestClientUser> Users, IReadOnlyList<RequestClientEndpoint> Endpoints, long OmittedRequests, bool IsPossiblyIncomplete);

public sealed record AdminDashboardViewModel(IReadOnlyList<AdminUserOverviewItem> Users, int Page, int TotalPages, int TotalUsers, RegistrationStatus Registration,
    RequestActivityViewModel RequestActivity, SuspiciousRequestEventsResult SuspiciousRequests, CrowdSecBansViewModel CrowdSecBans);

public sealed record CrowdSecBanRow(string Address, string Scope, string Scenario, string Origin, string Action, TimeSpan? Remaining, string? RemainingText, int BanCount);

public sealed record CrowdSecBansViewModel(bool IsAvailable, int TotalBans, bool Merge, int UsersPage, TablePage<CrowdSecBanRow> Table, bool CanManageBans = false);
