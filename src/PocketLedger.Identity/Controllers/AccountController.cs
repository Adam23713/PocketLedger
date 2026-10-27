using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.ViewModels.Account;
using PocketLedger.Security;
using PocketLedger.Services;
using QRCoder;

namespace PocketLedger.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IdentityDbContext dbContext,
    IAuthenticationRateLimiter rateLimiter, IAuthenticationAuditService audit, IClientIpAddressResolver clientIpAddressResolver,
    IRegistrationSettingsService registrationSettings, IConfiguration configuration, IUserDataKeyService dataKeys, IPendingSignInKeys pendingKeys,
    IUserAccountDeletionService accountDeletion) : Controller
{
    private const string DataUnlockFailedMessage = "Your data could not be unlocked. Contact the administrator.";

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> AccessBlocked()
    {
        pendingKeys.Clear(HttpContext);
        await signInManager.SignOutAsync();
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        return View();
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true
        ? ContinueAfterAuthentication(returnUrl)
        : View(await BuildLoginModelAsync(returnUrl));

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(await BuildLoginModelAsync(model.ReturnUrl, model));
        if (!await AllowRequestAsync(model.Username, cancellationToken)) return TooManyRequests();
        var normalized = userManager.NormalizeName(model.Username.Trim());
        var user = await userManager.FindByNameAsync(model.Username.Trim());
        if (user?.IsAccessBlocked == true)
        {
            await audit.WriteAsync("LoginBlocked", "Failure", user.Id, normalized, "AccessBlocked", cancellationToken: cancellationToken);
            return RedirectToAction(nameof(AccessBlocked));
        }
        var wasLocked = user is not null && await userManager.IsLockedOutAsync(user);
        var result = await signInManager.PasswordSignInAsync(model.Username.Trim(), model.Password, false, lockoutOnFailure: true);
        if ((result.Succeeded || result.RequiresTwoFactor) && user is not null)
        {
            var unlock = await UnlockDataKeyAsync(user, model.Password, cancellationToken);
            if (unlock != DataKeyUnlock.Unlocked)
            {
                await signInManager.SignOutAsync();
                if (unlock == DataKeyUnlock.Busy) return TooManyRequests();
                ModelState.AddModelError(string.Empty, DataUnlockFailedMessage);
                return View(await BuildLoginModelAsync(model.ReturnUrl, model));
            }
        }
        if (result.RequiresTwoFactor) return RedirectToAction(nameof(TwoFactor), new { returnUrl = model.ReturnUrl });
        if (result.Succeeded && user is not null)
        {
            if (user.AuthenticatorSetupComplete)
            {
                user.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow;
                user.LastSuccessfulLoginIpAddress = clientIpAddressResolver.GetClientIpAddress(HttpContext);
                await userManager.UpdateAsync(user);
            }
            await audit.WriteAsync(user.AuthenticatorSetupComplete ? "LoginSucceeded" : "PasswordAcceptedSetupRequired", "Success", user.Id, normalized, cancellationToken: cancellationToken);
            return user.AuthenticatorSetupComplete ? ContinueAfterAuthentication(model.ReturnUrl) : RedirectToAction(nameof(SetupAuthenticator), new { returnUrl = model.ReturnUrl });
        }
        var nowLocked = user is not null && await userManager.IsLockedOutAsync(user);
        var reason = result.IsLockedOut || wasLocked || nowLocked ? "LockedOut" : "InvalidCredentials";
        await audit.WriteAsync(reason == "LockedOut" ? "LoginLockout" : "LoginFailed", "Failure", user?.Id, normalized, reason, cancellationToken: cancellationToken);
        ModelState.AddModelError(string.Empty, reason == "LockedOut" ? "Login is temporarily unavailable." : "Invalid login attempt.");
        return View(await BuildLoginModelAsync(model.ReturnUrl, model));
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Register(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var status = await registrationSettings.GetStatusAsync(cancellationToken);
        if (!status.IsOpen) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(returnUrl ?? "/");
        if (status.IsFull) return View("RegistrationFull");
        return View(await BuildRegisterModelAsync(returnUrl));
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!(await registrationSettings.GetStatusAsync(cancellationToken)).IsOpen) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(model.ReturnUrl ?? "/");
        if (!await AllowRegistrationRequestAsync(cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid)
        {
            await audit.WriteAsync("Registration", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()), failureReason: "ValidationFailed", cancellationToken: cancellationToken);
            return View(await BuildRegisterModelAsync(model.ReturnUrl, model));
        }

        if (await EmailExistsAsync(model.Email, cancellationToken))
        {
            await audit.WriteAsync("Registration", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()), failureReason: "ExistingEmail", cancellationToken: cancellationToken);
            ModelState.AddModelError(nameof(model.Email), "An account already exists for this email address.");
            return View(await BuildRegisterModelAsync(model.ReturnUrl, model));
        }

        using var registrationLock = await registrationSettings.AcquireRegistrationLockAsync(cancellationToken);
        var status = await registrationSettings.GetStatusAsync(cancellationToken);
        if (!status.AcceptsRegistrations)
        {
            await audit.WriteAsync("Registration", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()),
                failureReason: status.IsOpen ? "RegistrationFull" : "RegistrationDisabled", cancellationToken: cancellationToken);
            return status.IsOpen ? View("RegistrationFull") : NotFound();
        }

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = model.Username.Trim(), Email = model.Email.Trim(), EmailConfirmed = false };
        IdentityResult? result = null;
        CreatedUserDataKey? created = null;
        try
        {
            // The account and its data key are created together, so no user can exist without a data key.
            await using (var transaction = await BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    result = await userManager.CreateAsync(user, model.Password);
                }
                catch (DbUpdateException)
                {
                    dbContext.Entry(user).State = EntityState.Detached;
                }
                if (result?.Succeeded == true)
                {
                    created = await dataKeys.CreateAsync(user.Id, model.Password, cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                }
            }
        }
        catch (UserKeyDerivationBusyException)
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return TooManyRequests();
        }
        if (result is null)
        {
            await audit.WriteAsync("Registration", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()), failureReason: "EmailCollision", cancellationToken: cancellationToken);
            ModelState.AddModelError(nameof(model.Email), "An account already exists for this email address.");
            return View(await BuildRegisterModelAsync(model.ReturnUrl, model));
        }
        if (!result.Succeeded)
        {
            await audit.WriteAsync("Registration", "Failure", normalizedUsername: userManager.NormalizeName(model.Username.Trim()), failureReason: "IdentityValidationFailed", cancellationToken: cancellationToken);
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(await BuildRegisterModelAsync(model.ReturnUrl, model));
        }

        using (created)
        {
            await signInManager.SignInAsync(user, false);
            pendingKeys.Issue(HttpContext, created!.Key);
            await audit.WriteAsync("Registration", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
            await audit.WriteAsync("DataKeyCreated", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
            return View("DataRecoveryKey", new DataRecoveryKeyViewModel(created.RecoveryKey, Url.Action(nameof(SetupAuthenticator))!, model.ReturnUrl));
        }
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExternalLogin(string provider, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (!await AllowExternalLoginRequestAsync("challenge", provider, cancellationToken)) return TooManyRequests();
        var scheme = (await signInManager.GetExternalAuthenticationSchemesAsync()).SingleOrDefault(item => string.Equals(item.Name, provider, StringComparison.Ordinal));
        if (scheme is null || string.IsNullOrWhiteSpace(scheme.DisplayName))
        {
            await audit.WriteAsync("ExternalLogin", "Failure", failureReason: "UnsupportedProvider", cancellationToken: cancellationToken);
            return BadRequest("The selected external login provider is unavailable.");
        }
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
        return Challenge(signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl), provider);
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null, CancellationToken cancellationToken = default)
    {
        if (!await AllowExternalLoginRequestAsync("callback", null, cancellationToken)) return TooManyRequests();
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            await audit.WriteAsync("ExternalLogin", "Failure", failureReason: "ProviderError", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "The external login was cancelled or rejected by the provider.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            await audit.WriteAsync("ExternalLogin", "Failure", failureReason: "MissingExternalLoginInfo", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "The external login response could not be validated.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }
        var linkedUser = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linkedUser is not null)
        {
            if (linkedUser.IsAccessBlocked)
            {
                await audit.WriteAsync("ExternalLogin", "Failure", linkedUser.Id, linkedUser.NormalizedUserName, "AccessBlocked", cancellationToken: cancellationToken);
                return RedirectToAction(nameof(AccessBlocked));
            }
            var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, bypassTwoFactor: false);
            // External providers prove identity but supply no secret, so the data password unlocks the data key next.
            if (result.RequiresTwoFactor)
            {
                await audit.WriteAsync("ExternalLoginTotpRequired", "Success", linkedUser.Id, linkedUser.NormalizedUserName, cancellationToken: cancellationToken);
                return RedirectToAction(nameof(UnlockData), new { returnUrl });
            }
            if (result.Succeeded)
            {
                await RecordSuccessfulLoginAsync(linkedUser);
                await audit.WriteAsync(linkedUser.AuthenticatorSetupComplete ? "ExternalLoginSucceeded" : "ExternalLoginAcceptedSetupRequired", "Success", linkedUser.Id, linkedUser.NormalizedUserName, cancellationToken: cancellationToken);
                return RedirectToAction(await dataKeys.ExistsAsync(linkedUser.Id, cancellationToken) ? nameof(UnlockData) : nameof(CreateDataPassword), new { returnUrl });
            }

            await audit.WriteAsync("ExternalLogin", "Failure", linkedUser.Id, linkedUser.NormalizedUserName, result.IsLockedOut ? "LockedOut" : "SignInRejected", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, result.IsLockedOut ? "Login is temporarily unavailable." : "The external login could not be completed.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }

        if (!TryGetVerifiedEmail(info.Principal, out var email))
        {
            await audit.WriteAsync("ExternalRegistration", "Failure", failureReason: "VerifiedEmailRequired", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "The external provider must supply a verified email address.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }
        using var registrationLock = await registrationSettings.AcquireRegistrationLockAsync(cancellationToken);
        var registrationStatus = await registrationSettings.GetStatusAsync(cancellationToken);
        if (!registrationStatus.AcceptsRegistrations)
        {
            await audit.WriteAsync("ExternalRegistration", "Failure", normalizedUsername: userManager.NormalizeEmail(email),
                failureReason: registrationStatus.IsOpen ? "RegistrationFull" : "RegistrationDisabled", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, registrationStatus.IsOpen ? "All available accounts are currently taken." : "Registration is currently unavailable.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }
        if (await EmailExistsAsync(email, cancellationToken))
        {
            await audit.WriteAsync("ExternalRegistration", "Failure", normalizedUsername: userManager.NormalizeEmail(email), failureReason: "ExistingEmail", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "An account already exists for this email address. Sign in with the existing account instead.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true };
        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(user).State = EntityState.Detached;
            await audit.WriteAsync("ExternalRegistration", "Failure", normalizedUsername: userManager.NormalizeEmail(email), failureReason: "EmailCollision", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "An account already exists for this email address. Sign in with the existing account instead.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }
        if (!createResult.Succeeded)
        {
            await audit.WriteAsync("ExternalRegistration", "Failure", normalizedUsername: userManager.NormalizeEmail(email), failureReason: "IdentityValidationFailed", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "The external account could not be registered.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }

        var addLoginResult = await userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            await audit.WriteAsync("ExternalRegistration", "Failure", normalizedUsername: userManager.NormalizeEmail(email), failureReason: "ExternalLoginLinkFailed", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "The external account could not be registered.");
            return View(nameof(Login), await BuildLoginModelAsync(returnUrl));
        }

        await signInManager.SignInAsync(user, false, info.LoginProvider);
        await audit.WriteAsync("ExternalRegistration", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return RedirectToAction(nameof(CreateDataPassword), new { returnUrl });
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> UnlockData(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var user = await GetSigningInUserAsync();
        if (user is null) return RedirectToAction(nameof(Login), new { returnUrl });
        if (user.IsAccessBlocked) return RedirectToAction(nameof(AccessBlocked));
        if (!await dataKeys.ExistsAsync(user.Id, cancellationToken))
            return User.Identity?.IsAuthenticated == true ? RedirectToAction(nameof(CreateDataPassword), new { returnUrl }) : RedirectToAction(nameof(Login), new { returnUrl });
        return View(new UnlockDataViewModel { ReturnUrl = returnUrl, IsDataPassword = !await userManager.HasPasswordAsync(user) });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlockData(UnlockDataViewModel model, CancellationToken cancellationToken)
    {
        var pending = await signInManager.GetTwoFactorAuthenticationUserAsync();
        var user = pending ?? (User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null);
        if (user is null) return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl });
        if (user.IsAccessBlocked) return RedirectToAction(nameof(AccessBlocked));
        if (!await AllowRequestAsync($"data-unlock:{user.Id}", cancellationToken)) return TooManyRequests();
        model.IsDataPassword = !await userManager.HasPasswordAsync(user);
        if (!ModelState.IsValid) return View(model);
        if (await userManager.IsLockedOutAsync(user)) return await SignInStoppedAsync("Login is temporarily unavailable.", model.ReturnUrl);

        var unlock = await UnlockDataKeyAsync(user, model.Password, cancellationToken);
        if (unlock == DataKeyUnlock.Busy) return TooManyRequests();
        if (unlock != DataKeyUnlock.Unlocked)
        {
            // Wrong data passwords count toward the same lockout as wrong login passwords.
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                await audit.WriteAsync("LoginLockout", "Failure", user.Id, user.NormalizedUserName, "LockedOut", cancellationToken: cancellationToken);
                return await SignInStoppedAsync("Login is temporarily unavailable.", model.ReturnUrl);
            }
            ModelState.AddModelError(nameof(model.Password), model.IsDataPassword ? "Incorrect data password." : "Incorrect password.");
            return View(model);
        }
        await userManager.ResetAccessFailedCountAsync(user);
        if (pending is not null) return RedirectToAction(nameof(TwoFactor), new { returnUrl = model.ReturnUrl });
        return user.AuthenticatorSetupComplete ? ContinueAfterAuthentication(model.ReturnUrl) : RedirectToAction(nameof(SetupAuthenticator), new { returnUrl = model.ReturnUrl });
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> CreateDataPassword(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await dataKeys.ExistsAsync(user.Id, cancellationToken)) return RedirectToAction(nameof(UnlockData), new { returnUrl });
        return View(new CreateDataPasswordViewModel { ReturnUrl = returnUrl });
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDataPassword(CreateDataPasswordViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (await dataKeys.ExistsAsync(user.Id, cancellationToken)) return RedirectToAction(nameof(UnlockData), new { returnUrl = model.ReturnUrl });
        if (!await AllowRequestAsync($"data-password:{user.Id}", cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid || !await ValidateNewPasswordAsync(user, model.Password, nameof(model.Password))) return View(model);
        CreatedUserDataKey created;
        try
        {
            created = await dataKeys.CreateAsync(user.Id, model.Password, cancellationToken);
        }
        catch (UserKeyDerivationBusyException)
        {
            return TooManyRequests();
        }
        using (created)
        {
            pendingKeys.Issue(HttpContext, created.Key);
            await audit.WriteAsync("DataKeyCreated", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
            return View("DataRecoveryKey", new DataRecoveryKeyViewModel(created.RecoveryKey, Url.Action(nameof(SetupAuthenticator))!, model.ReturnUrl));
        }
    }

    [AllowAnonymous, HttpGet]
    public IActionResult TwoFactor(string? returnUrl = null) => View(new TwoFactorViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactor(TwoFactorViewModel model, CancellationToken cancellationToken)
    {
        var pending = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (pending?.IsAccessBlocked == true) return RedirectToAction(nameof(AccessBlocked));
        var key = pending?.NormalizedUserName ?? "UNKNOWN";
        if (!await AllowRequestAsync(key, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        if (pending is not null && !pendingKeys.Has(HttpContext, pending.Id)) return await SignInStoppedAsync("Your sign-in expired. Please sign in again.", model.ReturnUrl);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(model.Code.Replace(" ", string.Empty).Replace("-", string.Empty), false, false);
        await audit.WriteAsync("TotpChallenge", result.Succeeded ? "Success" : "Failure", pending?.Id, pending?.NormalizedUserName, result.Succeeded ? null : "InvalidCode", cancellationToken: cancellationToken);
        if (result.Succeeded)
        {
            if (pending is not null)
            {
                pending.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow;
                pending.LastSuccessfulLoginIpAddress = clientIpAddressResolver.GetClientIpAddress(HttpContext);
                await userManager.UpdateAsync(pending);
                await audit.WriteAsync("LoginSucceeded", "Success", pending.Id, pending.NormalizedUserName, cancellationToken: cancellationToken);
            }
            return ContinueAfterAuthentication(model.ReturnUrl);
        }
        ModelState.AddModelError(string.Empty, "Invalid authentication attempt.");
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult RecoveryCode(string? returnUrl = null) => View(new TwoFactorViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoveryCode(TwoFactorViewModel model, CancellationToken cancellationToken)
    {
        var pending = await signInManager.GetTwoFactorAuthenticationUserAsync();
        var user = pending ?? (User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null);
        if (user?.IsAccessBlocked == true) return RedirectToAction(nameof(AccessBlocked));
        var key = user?.NormalizedUserName ?? "UNKNOWN";
        if (!await AllowRequestAsync(key, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        if (pending is not null && !pendingKeys.Has(HttpContext, pending.Id)) return await SignInStoppedAsync("Your sign-in expired. Please sign in again.", model.ReturnUrl);
        var succeeded = pending is not null
            ? (await signInManager.TwoFactorRecoveryCodeSignInAsync(model.Code)).Succeeded
            : user is not null && (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, model.Code)).Succeeded;
        await audit.WriteAsync("RecoveryCodeAuthentication", succeeded ? "Success" : "Failure", user?.Id, user?.NormalizedUserName, succeeded ? null : "InvalidCode", cancellationToken: cancellationToken);
        if (!succeeded || user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid authentication attempt.");
            return View(model);
        }
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1);
        user.AuthenticatorSetupComplete = false;
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.UpdateSecurityStampAsync(user);
        await userManager.UpdateAsync(user);
        await signInManager.SignInAsync(user, false);
        await audit.WriteAsync("AuthenticatorResetThroughRecovery", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        await audit.WriteAsync("SecurityStampInvalidation", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return RedirectToAction(nameof(SetupAuthenticator), new { returnUrl = model.ReturnUrl });
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> SetupAuthenticator(string? returnUrl = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) { await userManager.ResetAuthenticatorKeyAsync(user); key = await userManager.GetAuthenticatorKeyAsync(user); }
        return View(BuildSetupModel(user, key!, returnUrl: returnUrl));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetupAuthenticator(AuthenticatorSetupViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await AllowRequestAsync(user.NormalizedUserName ?? user.Id.ToString(), cancellationToken)) return TooManyRequests();
        var key = await userManager.GetAuthenticatorKeyAsync(user) ?? string.Empty;
        if (!ModelState.IsValid) return View(BuildSetupModel(user, key, model.Code, model.ReturnUrl));
        var valid = await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, model.Code.Replace(" ", string.Empty).Replace("-", string.Empty));
        if (!valid)
        {
            await audit.WriteAsync("AuthenticatorSetup", "Failure", user.Id, user.NormalizedUserName, "InvalidCode", cancellationToken: cancellationToken);
            ModelState.AddModelError(nameof(model.Code), "Invalid authentication code.");
            return View(BuildSetupModel(user, key, model.Code, model.ReturnUrl));
        }
        await userManager.SetTwoFactorEnabledAsync(user, true);
        user.AuthenticatorSetupComplete = false;
        await userManager.UpdateAsync(user);
        var codes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        await audit.WriteAsync("AuthenticatorSetupCompleted", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        await audit.WriteAsync("RecoveryCodesRegenerated", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return View(nameof(RecoveryCodes), new RecoveryCodesViewModel { Codes = codes, ReturnUrl = model.ReturnUrl });
    }

    [Authorize, HttpGet]
    public IActionResult RecoveryCodes() => NotFound();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoveryCodes(RecoveryCodesViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest("Recovery code storage confirmation is required.");
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.TwoFactorEnabled) return Challenge();
        user.AuthenticatorSetupComplete = true;
        await RecordSuccessfulLoginAsync(user);
        await signInManager.RefreshSignInAsync(user);
        await audit.WriteAsync("LoginSucceeded", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return ContinueAfterAuthentication(model.ReturnUrl);
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> ChangePassword()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        ViewData["IsDataPassword"] = !await userManager.HasPasswordAsync(user);
        return View(new ChangePasswordViewModel());
    }

    // For password users this changes the login password; for external-login users it changes the data password.
    // Either way the data key itself is unchanged and only re-wrapped, so no data has to be re-encrypted.
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var hasPassword = await userManager.HasPasswordAsync(user);
        ViewData["IsDataPassword"] = !hasPassword;
        if (!await AllowPasswordChangeRequestAsync(user, cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid)
        {
            await audit.WriteAsync("PasswordChange", "Failure", user.Id, user.NormalizedUserName, "ValidationFailed", cancellationToken: cancellationToken);
            return View(model);
        }

        IActionResult InvalidCurrentPassword()
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Password change was not completed. Check your current password and try again.");
            return View(model);
        }
        if (hasPassword && !await userManager.CheckPasswordAsync(user, model.CurrentPassword))
        {
            await audit.WriteAsync("PasswordChange", "Failure", user.Id, user.NormalizedUserName, "InvalidCurrentPassword", cancellationToken: cancellationToken);
            return InvalidCurrentPassword();
        }
        if (!hasPassword && !await ValidateNewPasswordAsync(user, model.NewPassword, nameof(model.NewPassword)))
        {
            await audit.WriteAsync("PasswordChange", "Failure", user.Id, user.NormalizedUserName, "InvalidNewPassword", cancellationToken: cancellationToken);
            return View(model);
        }

        UserDataKey? key;
        try
        {
            key = await dataKeys.UnlockAsync(user.Id, model.CurrentPassword, UserKeySecretKind.Password, cancellationToken);
        }
        catch (UserKeyException)
        {
            key = null;
        }
        catch (UserKeyDerivationBusyException)
        {
            return TooManyRequests();
        }
        if (key is null)
        {
            await audit.WriteAsync("PasswordChange", "Failure", user.Id, user.NormalizedUserName, hasPassword ? "DataKeyUnavailable" : "InvalidCurrentPassword", cancellationToken: cancellationToken);
            if (hasPassword)
            {
                ModelState.AddModelError(string.Empty, DataUnlockFailedMessage);
                return View(model);
            }
            return InvalidCurrentPassword();
        }

        using (key)
        {
            IdentityResult result;
            try
            {
                await using var transaction = await BeginTransactionAsync(cancellationToken);
                result = hasPassword ? await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword) : await userManager.UpdateSecurityStampAsync(user);
                if (result.Succeeded)
                {
                    await dataKeys.ReplacePasswordAsync(key, model.NewPassword, cancellationToken);
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                }
            }
            catch (UserKeyDerivationBusyException)
            {
                return TooManyRequests();
            }
            if (!result.Succeeded)
            {
                var invalidCurrentPassword = result.Errors.Any(error => error.Code == "PasswordMismatch");
                await audit.WriteAsync("PasswordChange", "Failure", user.Id, user.NormalizedUserName, invalidCurrentPassword ? "InvalidCurrentPassword" : "InvalidNewPassword", cancellationToken: cancellationToken);
                if (invalidCurrentPassword) return InvalidCurrentPassword();
                foreach (var error in result.Errors) ModelState.AddModelError(nameof(model.NewPassword), error.Description);
                return View(model);
            }
        }

        await signInManager.RefreshSignInAsync(user);
        await audit.WriteAsync("PasswordChange", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        await audit.WriteAsync("SecurityStampInvalidation", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        TempData["PasswordChangeStatus"] = "Your password has been changed.";
        return RedirectToAction(nameof(ChangePassword));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult RecoverAccount() => User.Identity?.IsAuthenticated == true ? RedirectToAction(nameof(Security)) : View(new RecoverAccountViewModel());

    // The recovery key is the only way back to the data after a forgotten password; nobody else can reset it.
    // It replaces the password (or data password) only: signing in afterwards still requires the authenticator.
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoverAccount(RecoverAccountViewModel model, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Security));
        if (!await AllowRequestAsync($"recover:{model.Username}", cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        var normalized = userManager.NormalizeName(model.Username.Trim());
        var user = await userManager.FindByNameAsync(model.Username.Trim());
        IActionResult Rejected()
        {
            ModelState.AddModelError(string.Empty, "Account recovery was not completed. Check the username and recovery key and try again.");
            return View(model);
        }
        if (user is null || user.IsAccessBlocked || await userManager.IsLockedOutAsync(user))
        {
            await audit.WriteAsync("AccountRecovery", "Failure", user?.Id, normalized, user is null ? "UnknownUser" : user.IsAccessBlocked ? "AccessBlocked" : "LockedOut", cancellationToken: cancellationToken);
            return Rejected();
        }

        UserDataKey? key;
        try
        {
            key = await dataKeys.UnlockAsync(user.Id, model.RecoveryKey, UserKeySecretKind.RecoveryKey, cancellationToken);
        }
        catch (UserKeyException)
        {
            key = null;
        }
        catch (UserKeyDerivationBusyException)
        {
            return TooManyRequests();
        }
        if (key is null)
        {
            await userManager.AccessFailedAsync(user);
            await audit.WriteAsync("AccountRecovery", "Failure", user.Id, normalized, "InvalidRecoveryKey", cancellationToken: cancellationToken);
            return Rejected();
        }

        string newRecoveryKey;
        using (key)
        {
            var hasPassword = await userManager.HasPasswordAsync(user);
            if (!hasPassword && !await ValidateNewPasswordAsync(user, model.NewPassword, nameof(model.NewPassword))) return View(model);
            IdentityResult result;
            try
            {
                await using var transaction = await BeginTransactionAsync(cancellationToken);
                result = hasPassword
                    ? await userManager.ResetPasswordAsync(user, await userManager.GeneratePasswordResetTokenAsync(user), model.NewPassword)
                    : await userManager.UpdateSecurityStampAsync(user);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors) ModelState.AddModelError(nameof(model.NewPassword), error.Description);
                    return View(model);
                }
                await dataKeys.ReplacePasswordAsync(key, model.NewPassword, cancellationToken);
                // The used recovery key has been typed into a browser, so a fresh one replaces it.
                newRecoveryKey = await dataKeys.ReplaceRecoveryKeyAsync(key, cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            catch (UserKeyDerivationBusyException)
            {
                return TooManyRequests();
            }
        }
        await userManager.ResetAccessFailedCountAsync(user);
        await audit.WriteAsync("AccountRecovery", "Success", user.Id, normalized, cancellationToken: cancellationToken);
        await audit.WriteAsync("SecurityStampInvalidation", "Success", user.Id, normalized, cancellationToken: cancellationToken);
        return View("DataRecoveryKey", new DataRecoveryKeyViewModel(newRecoveryKey, Url.Action(nameof(Login))!, null, AfterAccountRecovery: true));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> RegenerateRecoveryKey()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(new RegenerateRecoveryKeyViewModel { IsDataPassword = !await userManager.HasPasswordAsync(user) });
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateRecoveryKey(RegenerateRecoveryKeyViewModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        model.IsDataPassword = !await userManager.HasPasswordAsync(user);
        if (!await AllowRequestAsync($"recovery-key:{user.Id}", cancellationToken)) return TooManyRequests();
        if (!ModelState.IsValid) return View(model);
        UserDataKey? key;
        try
        {
            key = await dataKeys.UnlockAsync(user.Id, model.CurrentPassword, UserKeySecretKind.Password, cancellationToken);
        }
        catch (UserKeyException)
        {
            key = null;
        }
        catch (UserKeyDerivationBusyException)
        {
            return TooManyRequests();
        }
        if (key is null)
        {
            await audit.WriteAsync("DataRecoveryKeyRegenerated", "Failure", user.Id, user.NormalizedUserName, "InvalidCurrentPassword", cancellationToken: cancellationToken);
            ModelState.AddModelError(nameof(model.CurrentPassword), "The recovery key was not replaced. Check your current password and try again.");
            return View(model);
        }
        string recoveryKey;
        using (key)
        {
            try
            {
                recoveryKey = await dataKeys.ReplaceRecoveryKeyAsync(key, cancellationToken);
            }
            catch (UserKeyDerivationBusyException)
            {
                return TooManyRequests();
            }
        }
        await audit.WriteAsync("DataRecoveryKeyRegenerated", "Success", user.Id, user.NormalizedUserName, cancellationToken: cancellationToken);
        return View("DataRecoveryKey", new DataRecoveryKeyViewModel(recoveryKey, Url.Action(nameof(Security))!, null));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Manage()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var webBaseUrl = configuration["OpenIddict:WebBaseUrl"] ?? throw new InvalidOperationException("OpenIddict:WebBaseUrl is required.");
        return View(new AccountManagementViewModel(user.UserName ?? "Unavailable", user.Email, !user.IsBootstrapAdministrator,
            $"{webBaseUrl.TrimEnd('/')}/ImportExport#encrypted-backup"));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(string? confirmation, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.IsBootstrapAdministrator) return Forbid();
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            TempData["AccountWarning"] = "Your account was not deleted because the confirmation text did not match DELETE.";
            return RedirectToAction(nameof(Manage));
        }

        await accountDeletion.DeleteAsync(user, cancellationToken);
        pendingKeys.Clear(HttpContext);
        await signInManager.SignOutAsync();
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        TempData["LoginSuccessMessage"] = "Your account and all PocketLedger data have been permanently deleted.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Security(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var events = await dbContext.AuthenticationAuditEvents.AsNoTracking().Where(item => item.UserId == user.Id)
            .OrderByDescending(item => item.TimestampUtc).ThenByDescending(item => item.Id).Take(5).ToListAsync(cancellationToken);
        return View(new SecurityDashboardViewModel(events));
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> LoginEvents(int page = 1, CancellationToken cancellationToken = default)
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
        pendingKeys.Clear(HttpContext);
        await signInManager.SignOutAsync();
        var webBaseUrl = configuration["OpenIddict:WebBaseUrl"] ?? throw new InvalidOperationException("OpenIddict:WebBaseUrl is required.");
        return Redirect($"{webBaseUrl.TrimEnd('/')}/Session/CompleteLogout");
    }

    [Authorize] public IActionResult AccessDenied() => View();

    private async Task<bool> AllowRequestAsync(string username, CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.AcquireAsync(username, cancellationToken);
        if (lease.IsAcquired) return true;
        await audit.WriteAsync("RateLimitRejected", "Failure", normalizedUsername: userManager.NormalizeName(username.Trim()), failureReason: "RateLimited", cancellationToken: cancellationToken);
        return false;
    }

    private async Task<bool> AllowPasswordChangeRequestAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.AcquireAsync($"password-change:{user.Id}", cancellationToken);
        if (lease.IsAcquired) return true;
        await audit.WriteAsync("PasswordChange", "Failure", user.Id, user.NormalizedUserName, "RateLimited", cancellationToken: cancellationToken);
        return false;
    }

    private async Task<bool> AllowRegistrationRequestAsync(CancellationToken cancellationToken)
    {
        var clientAddress = clientIpAddressResolver.GetClientIpAddress(HttpContext) ?? "UNKNOWN";
        using var lease = await rateLimiter.AcquireAsync($"registration:{clientAddress}", cancellationToken);
        if (lease.IsAcquired) return true;
        await audit.WriteAsync("Registration", "Failure", failureReason: "RateLimited", cancellationToken: cancellationToken);
        return false;
    }

    private async Task<bool> AllowExternalLoginRequestAsync(string stage, string? provider, CancellationToken cancellationToken)
    {
        var clientAddress = clientIpAddressResolver.GetClientIpAddress(HttpContext) ?? "UNKNOWN";
        using var lease = await rateLimiter.AcquireAsync($"external-login:{stage}:{provider}:{clientAddress}", cancellationToken);
        if (lease.IsAcquired) return true;
        await audit.WriteAsync("ExternalLogin", "Failure", failureReason: "RateLimited", cancellationToken: cancellationToken);
        return false;
    }

    private enum DataKeyUnlock { Unlocked, Failed, Busy }

    // Opens the user's data key with the password (or data password) and keeps it for the rest of this sign-in.
    private async Task<DataKeyUnlock> UnlockDataKeyAsync(ApplicationUser user, string password, CancellationToken cancellationToken)
    {
        try
        {
            using var key = await dataKeys.UnlockAsync(user.Id, password, UserKeySecretKind.Password, cancellationToken);
            if (key is null)
            {
                await audit.WriteAsync("DataKeyUnlock", "Failure", user.Id, user.NormalizedUserName, "DataKeyMissing", cancellationToken: cancellationToken);
                return DataKeyUnlock.Failed;
            }
            pendingKeys.Issue(HttpContext, key);
            return DataKeyUnlock.Unlocked;
        }
        catch (UserKeyException)
        {
            await audit.WriteAsync("DataKeyUnlock", "Failure", user.Id, user.NormalizedUserName, "InvalidSecret", cancellationToken: cancellationToken);
            return DataKeyUnlock.Failed;
        }
        catch (UserKeyDerivationBusyException)
        {
            await audit.WriteAsync("DataKeyUnlock", "Failure", user.Id, user.NormalizedUserName, "Busy", cancellationToken: cancellationToken);
            return DataKeyUnlock.Busy;
        }
    }

    private async Task<ApplicationUser?> GetSigningInUserAsync()
        => await signInManager.GetTwoFactorAuthenticationUserAsync() ?? (User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(User) : null);

    private async Task<IActionResult> SignInStoppedAsync(string message, string? returnUrl)
    {
        pendingKeys.Clear(HttpContext);
        await signInManager.SignOutAsync();
        TempData["LoginMessage"] = message;
        return RedirectToAction(nameof(Login), new { returnUrl });
    }

    private async Task<bool> ValidateNewPasswordAsync(ApplicationUser user, string password, string field)
    {
        var valid = true;
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            if (result.Succeeded) continue;
            valid = false;
            foreach (var error in result.Errors) ModelState.AddModelError(field, error.Description);
        }
        return valid;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
        => dbContext.Database.IsRelational() ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

    private async Task<LoginViewModel> BuildLoginModelAsync(string? returnUrl, LoginViewModel? model = null)
    {
        model ??= new LoginViewModel();
        model.ReturnUrl = returnUrl;
        model.RegistrationEnabled = (await registrationSettings.GetStatusAsync()).AcceptsRegistrations;
        model.ExternalProviders = await GetExternalProvidersAsync();
        return model;
    }

    private async Task<RegisterViewModel> BuildRegisterModelAsync(string? returnUrl, RegisterViewModel? model = null)
    {
        model ??= new RegisterViewModel();
        model.ReturnUrl = returnUrl;
        model.ExternalProviders = await GetExternalProvidersAsync();
        return model;
    }

    private async Task<IReadOnlyList<ExternalLoginProviderViewModel>> GetExternalProvidersAsync()
        => (await signInManager.GetExternalAuthenticationSchemesAsync()).Where(item => !string.IsNullOrWhiteSpace(item.DisplayName))
            .Select(item => new ExternalLoginProviderViewModel(item.Name, item.DisplayName!)).OrderBy(item => item.DisplayName, StringComparer.Ordinal).ToArray();

    private async Task RecordSuccessfulLoginAsync(ApplicationUser user)
    {
        if (!user.AuthenticatorSetupComplete) return;
        user.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow;
        user.LastSuccessfulLoginIpAddress = clientIpAddressResolver.GetClientIpAddress(HttpContext);
        await userManager.UpdateAsync(user);
    }

    private IActionResult ContinueAfterAuthentication(string? returnUrl)
    {
        if (IsAuthorizationReturnUrl(returnUrl)) return LocalRedirect(returnUrl!);
        var webBaseUrl = configuration["OpenIddict:WebBaseUrl"] ?? throw new InvalidOperationException("OpenIddict:WebBaseUrl is required.");
        return Redirect($"{webBaseUrl.TrimEnd('/')}/Session/CompleteSignIn");
    }

    private bool IsAuthorizationReturnUrl(string? returnUrl)
    {
        if (!Url.IsLocalUrl(returnUrl)) return false;
        var path = returnUrl!.Split('?', 2)[0];
        return string.Equals(path, "/connect/authorize", StringComparison.OrdinalIgnoreCase);
    }

    private Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = userManager.NormalizeEmail(email.Trim());
        return userManager.Users.AsNoTracking().AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    private static bool TryGetVerifiedEmail(ClaimsPrincipal principal, out string email)
    {
        email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email") ?? string.Empty;
        var emailVerified = principal.FindAll("email_verified").Any(claim => string.Equals(claim.Value, bool.TrueString, StringComparison.OrdinalIgnoreCase));
        if (!emailVerified || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
        {
            email = string.Empty;
            return false;
        }
        email = email.Trim();
        return true;
    }

    private ViewResult TooManyRequests()
    {
        Response.Headers.RetryAfter = "60";
        Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return View("TooManyRequests");
    }

    private static AuthenticatorSetupViewModel BuildSetupModel(ApplicationUser user, string key, string code = "", string? returnUrl = null)
    {
        var issuer = "PocketLedger";
        var account = user.UserName ?? user.Id.ToString();
        var uri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";
        using var data = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return new AuthenticatorSetupViewModel { SharedKey = key, QrCodeDataUri = "data:image/png;base64," + Convert.ToBase64String(png), Code = code, ReturnUrl = returnUrl };
    }

}
