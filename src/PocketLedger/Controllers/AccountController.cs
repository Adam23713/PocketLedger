using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Account;
using PocketLedger.Services;
using QRCoder;

namespace PocketLedger.Controllers;

public class AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, PocketLedgerDbContext dbContext,
    IOptions<AccountManagementOptions> accountOptions, IAuthenticationRateLimiter rateLimiter, IAuthenticationAuditService audit) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View(new LoginViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!await AllowRequestAsync(model.Username, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        var normalized = userManager.NormalizeName(model.Username.Trim());
        var user = await userManager.FindByNameAsync(model.Username.Trim());
        var wasLocked = user is not null && await userManager.IsLockedOutAsync(user);
        var result = await signInManager.PasswordSignInAsync(model.Username.Trim(), model.Password, false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor) return RedirectToAction(nameof(TwoFactor));
        if (result.Succeeded && user is not null)
        {
            if (user.AuthenticatorSetupComplete)
            {
                user.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow;
                user.LastSuccessfulLoginIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                await userManager.UpdateAsync(user);
            }
            await audit.WriteAsync(user.AuthenticatorSetupComplete ? "LoginSucceeded" : "PasswordAcceptedSetupRequired", "Success", user.Id, normalized, cancellationToken: cancellationToken);
            return RedirectToAction("Index", "Home");
        }
        var nowLocked = user is not null && await userManager.IsLockedOutAsync(user);
        var reason = result.IsLockedOut || wasLocked || nowLocked ? "LockedOut" : "InvalidCredentials";
        await audit.WriteAsync(reason == "LockedOut" ? "LoginLockout" : "LoginFailed", "Failure", user?.Id, normalized, reason, cancellationToken: cancellationToken);
        ModelState.AddModelError(string.Empty, reason == "LockedOut" ? "Login is temporarily unavailable." : "Invalid login attempt.");
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Register(CancellationToken cancellationToken)
    {
        if (await userManager.Users.CountAsync(cancellationToken) >= accountOptions.Value.MaximumUserCount)
        {
            await audit.WriteAsync("RegistrationPageRejected", "Failure", failureReason: "UserLimitReached", cancellationToken: cancellationToken);
            return NotFound();
        }
        return View(new RegisterViewModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!await AllowRequestAsync(model.Username, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(673492817)", cancellationToken);
        if (await userManager.Users.CountAsync(cancellationToken) >= accountOptions.Value.MaximumUserCount)
        {
            await audit.WriteAsync("RegistrationRejected", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()), failureReason: "UserLimitReached", cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NotFound();
        }
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = model.Username.Trim(), CreatedAtUtc = DateTimeOffset.UtcNow };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            await audit.WriteAsync("RegistrationFailed", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()), failureReason: "Validation", cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return View(model);
        }
        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, false);
        await audit.WriteAsync("RegistrationSucceeded", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return RedirectToAction(nameof(SetupAuthenticator));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult TwoFactor() => View(new TwoFactorViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactor(TwoFactorViewModel model, CancellationToken cancellationToken)
    {
        var pending = await signInManager.GetTwoFactorAuthenticationUserAsync();
        var key = pending?.NormalizedUserName ?? "UNKNOWN";
        if (!await AllowRequestAsync(key, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(model.Code.Replace(" ", string.Empty).Replace("-", string.Empty), false, false);
        await audit.WriteAsync("TotpChallenge", result.Succeeded ? "Success" : "Failure", pending?.Id, pending?.NormalizedUserName, result.Succeeded ? null : "InvalidCode", cancellationToken: cancellationToken);
        if (result.Succeeded)
        {
            if (pending is not null)
            {
                pending.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow;
                pending.LastSuccessfulLoginIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                await userManager.UpdateAsync(pending);
                await audit.WriteAsync("LoginSucceeded", "Success", pending.Id, pending.NormalizedUserName, cancellationToken: cancellationToken);
            }
            return RedirectToAction("Index", "Home");
        }
        ModelState.AddModelError(string.Empty, "Invalid authentication attempt.");
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult RecoveryCode() => View(new TwoFactorViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoveryCode(TwoFactorViewModel model, CancellationToken cancellationToken)
    {
        var pending = await signInManager.GetTwoFactorAuthenticationUserAsync();
        var key = pending?.NormalizedUserName ?? "UNKNOWN";
        if (!await AllowRequestAsync(key, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(model.Code);
        await audit.WriteAsync("RecoveryCodeAuthentication", result.Succeeded ? "Success" : "Failure", pending?.Id, pending?.NormalizedUserName, result.Succeeded ? null : "InvalidCode", cancellationToken: cancellationToken);
        if (!result.Succeeded || pending is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid authentication attempt.");
            return View(model);
        }
        await userManager.ResetAuthenticatorKeyAsync(pending);
        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(pending, 1);
        pending.AuthenticatorSetupComplete = false;
        await userManager.SetTwoFactorEnabledAsync(pending, false);
        await userManager.UpdateSecurityStampAsync(pending);
        await userManager.UpdateAsync(pending);
        await signInManager.SignInAsync(pending, false);
        await audit.WriteAsync("AuthenticatorResetThroughRecovery", "Success", pending.Id, pending.NormalizedUserName, cancellationToken: cancellationToken);
        await audit.WriteAsync("SecurityStampInvalidation", "Success", pending.Id, pending.NormalizedUserName, cancellationToken: cancellationToken);
        return RedirectToAction(nameof(SetupAuthenticator));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> SetupAuthenticator()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) { await userManager.ResetAuthenticatorKeyAsync(user); key = await userManager.GetAuthenticatorKeyAsync(user); }
        return View(BuildSetupModel(user, key!));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupAuthenticator(AuthenticatorSetupViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await AllowRequestAsync(user.NormalizedUserName ?? user.Id.ToString(), cancellationToken)) return TooManyRequests();
        var key = await userManager.GetAuthenticatorKeyAsync(user) ?? string.Empty;
        if (!ModelState.IsValid) return View(BuildSetupModel(user, key, model.Code));
        var valid = await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, model.Code.Replace(" ", string.Empty).Replace("-", string.Empty));
        if (!valid)
        {
            await audit.WriteAsync("AuthenticatorSetup", "Failure", user.Id, user.NormalizedUserName, "InvalidCode", cancellationToken: cancellationToken);
            ModelState.AddModelError(nameof(model.Code), "Invalid authentication code.");
            return View(BuildSetupModel(user, key, model.Code));
        }
        await userManager.SetTwoFactorEnabledAsync(user, true);
        user.AuthenticatorSetupComplete = false;
        await userManager.UpdateAsync(user);
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        await audit.WriteAsync("AuthenticatorSetupCompleted", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        await audit.WriteAsync("RecoveryCodesRegenerated", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return View(nameof(RecoveryCodes), new RecoveryCodesViewModel { Codes = codes });
    }

    [Authorize, HttpGet]
    public IActionResult RecoveryCodes() => NotFound();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoveryCodes(RecoveryCodesViewModel model)
    {
        if (!model.Confirmed) return BadRequest("Recovery code storage confirmation is required.");
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.TwoFactorEnabled) return Challenge();
        user.AuthenticatorSetupComplete = true;
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);
        return RedirectToAction("Index", "Home");
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Security(int page = 1, CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        const int pageSize = 30;
        page = Math.Max(page, 1);
        var query = dbContext.AuthenticationAuditEvents.AsNoTracking().Where(item => item.UserId == user.Id).OrderByDescending(item => item.TimestampUtc);
        var count = await query.CountAsync(cancellationToken);
        var events = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return View(new AuditLogViewModel(events, page, Math.Max(1, (int)Math.Ceiling(count / (double)pageSize))));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        await audit.WriteAsync("Logout", "Success", user?.Id, user?.NormalizedUserName, cancellationToken: cancellationToken);
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous] public IActionResult AccessDenied() => View();

    private async Task<bool> AllowRequestAsync(string username, CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.AcquireAsync(username, cancellationToken);
        if (lease.IsAcquired) return true;
        await audit.WriteAsync("RateLimitRejected", "Failure", normalizedUsername: userManager.NormalizeName(username.Trim()), failureReason: "RateLimited", cancellationToken: cancellationToken);
        return false;
    }

    private ObjectResult TooManyRequests() { Response.Headers.RetryAfter = "60"; return StatusCode(StatusCodes.Status429TooManyRequests, "Too many authentication requests. Try again later."); }

    private static AuthenticatorSetupViewModel BuildSetupModel(ApplicationUser user, string key, string code = "")
    {
        var issuer = "PocketLedger";
        var account = user.UserName ?? user.Id.ToString();
        var uri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";
        using var data = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return new AuthenticatorSetupViewModel { SharedKey = key, QrCodeDataUri = "data:image/png;base64," + Convert.ToBase64String(png), Code = code };
    }
}
