using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuthenticationAuditEvent> AuthenticationAuditEvents => Set<AuthenticationAuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
