using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger;

public static class CommandRunner
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        return args[0] switch
        {
            "bootstrap-identity" => await BootstrapAsync(provider),
            "account" when args.Length == 3 => await AccountCommandAsync(provider, args[1], args[2]),
            _ => Fail("Unknown command.")
        };
    }

    private static async Task<int> BootstrapAsync(IServiceProvider provider)
    {
        await provider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        var username = Environment.GetEnvironmentVariable("POCKETLEDGER_INITIAL_USERNAME")?.Trim();
        var password = Environment.GetEnvironmentVariable("POCKETLEDGER_INITIAL_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return Fail("Initial username and password environment variables are required.");
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.Users.AnyAsync()) return Fail("The Identity database already contains a user.");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = username, CreatedAtUtc = DateTimeOffset.UtcNow };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) return Fail("Initial user creation failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
        Console.WriteLine("Identity bootstrap completed successfully.");
        return 0;
    }

    private static async Task<int> AccountCommandAsync(IServiceProvider provider, string command, string username)
    {
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = provider.GetRequiredService<IdentityDbContext>();
        var user = await users.FindByNameAsync(username);
        if (user is null) return Fail("User not found.");
        IdentityResult result;
        string eventType;
        switch (command)
        {
            case "unlock":
                result = await users.SetLockoutEndDateAsync(user, null);
                if (result.Succeeded) result = await users.ResetAccessFailedCountAsync(user);
                eventType = "AdministrativeUnlock";
                break;
            case "invalidate-sessions":
                result = await users.UpdateSecurityStampAsync(user);
                eventType = "SecurityStampInvalidation";
                break;
            case "reset-authenticator":
                result = await users.ResetAuthenticatorKeyAsync(user);
                if (result.Succeeded) result = await users.SetTwoFactorEnabledAsync(user, false);
                user.AuthenticatorSetupComplete = false;
                if (result.Succeeded) result = await users.UpdateSecurityStampAsync(user);
                if (result.Succeeded) result = await users.UpdateAsync(user);
                eventType = "AdministrativeAuthenticatorReset";
                break;
            default: return Fail("Unknown account command.");
        }
        if (!result.Succeeded) return Fail("Account command failed.");
        db.AuthenticationAuditEvents.Add(new AuthenticationAuditEvent { Id = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow, UserId = user.Id, NormalizedUsername = user.NormalizedUserName, EventType = eventType, Outcome = "Success", RequestPath = "CLI", HttpMethod = "CLI" });
        await db.SaveChangesAsync();
        Console.WriteLine("Account command completed successfully.");
        return 0;
    }

    private static int Fail(string message) { Console.Error.WriteLine(message); return 1; }
}
