using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Web.Data;

namespace PocketLedger.Web.Authentication;

public sealed class DatabaseTicketStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider protectionProvider, IHttpContextAccessor? httpContextAccessor = null) : ITicketStore, ISessionTicketReader
{
    public const string SessionKeyProperty = ".session-key";
    public const string RevokedSessionItemKey = "PocketLedger.Web.BffSession.Revoked";
    private readonly IDataProtector protector = protectionProvider.CreateProtector("PocketLedger.Web.BffSession.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket) => await StoreAsync(ticket, CancellationToken.None);
    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var userId = GetUserId(ticket) ?? throw new InvalidOperationException("A BFF session must belong to a user.");
        var key = Guid.NewGuid().ToString("N");
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        db.Sessions.Add(new BffSession { Id = key, UserId = userId, ProtectedTicket = protector.Protect(TicketSerializer.Default.Serialize(ticket)), ExpiresAtUtc = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8) });
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        var item = await db.Sessions.SingleOrDefaultAsync(session => session.Id == key, cancellationToken);
        // A revoked session must never be brought back to life by a request that was already in flight.
        if (item is null || item.RevokedAtUtc is not null) return;
        item.ProtectedTicket = protector.Protect(TicketSerializer.Default.Serialize(ticket));
        item.ExpiresAtUtc = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);
    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        var item = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(session => session.Id == key, cancellationToken);
        if (item is null || item.ExpiresAtUtc <= DateTimeOffset.UtcNow) return null;
        if (item.RevokedAtUtc is not null)
        {
            if (httpContextAccessor?.HttpContext is { } httpContext) httpContext.Items[RevokedSessionItemKey] = true;
            return null;
        }
        if (item.UserId is null) return null;
        var ticket = TicketSerializer.Default.Deserialize(protector.Unprotect(item.ProtectedTicket)) ?? throw new InvalidOperationException("The stored BFF session ticket is invalid.");
        ticket.Properties.Items[SessionKeyProperty] = key;
        return ticket;
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        await db.Sessions.Where(session => session.Id == key).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Immediately terminates every web session of the given users: the stored tickets, including their access and
    /// refresh tokens, are destroyed. Only a marker remains so the browser is shown the "access blocked" page.
    /// </summary>
    public async Task<int> RevokeUserSessionsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0) return 0;
        var ids = userIds.Distinct().ToArray();
        var now = DateTimeOffset.UtcNow;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        var sessions = await db.Sessions.Where(session => session.UserId.HasValue && ids.Contains(session.UserId.Value) && session.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.ProtectedTicket = [];
            session.RevokedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    private static Guid? GetUserId(AuthenticationTicket ticket)
    {
        var value = ticket.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? ticket.Principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}
