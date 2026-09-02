using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Web.Data;

namespace PocketLedger.Web.Authentication;

public sealed class DatabaseTicketStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider protectionProvider) : ITicketStore, ISessionTicketReader
{
    public const string SessionKeyProperty = ".session-key";
    private readonly IDataProtector protector = protectionProvider.CreateProtector("PocketLedger.Web.BffSession.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket) => await StoreAsync(ticket, CancellationToken.None);
    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = Guid.NewGuid().ToString("N");
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        db.Sessions.Add(new BffSession { Id = key, ProtectedTicket = protector.Protect(TicketSerializer.Default.Serialize(ticket)), ExpiresAtUtc = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8) });
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);
    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebDbContext>();
        var item = await db.Sessions.SingleOrDefaultAsync(session => session.Id == key, cancellationToken);
        if (item is null) return;
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
}
