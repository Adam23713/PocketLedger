using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Entities;

namespace PocketLedger.Models.ViewModels.Account;

public sealed class LoginViewModel { [Required] public string Username { get; set; } = string.Empty; [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty; public string? ReturnUrl { get; set; } }
public sealed class RegisterViewModel { [Required] public string Username { get; set; } = string.Empty; [Required, MinLength(14), DataType(DataType.Password)] public string Password { get; set; } = string.Empty; [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty; }
public sealed class TwoFactorViewModel { [Required] public string Code { get; set; } = string.Empty; public string? ReturnUrl { get; set; } }
public sealed class AuthenticatorSetupViewModel { public string SharedKey { get; set; } = string.Empty; public string QrCodeDataUri { get; set; } = string.Empty; [Required] public string Code { get; set; } = string.Empty; public string? ReturnUrl { get; set; } }
public sealed class RecoveryCodesViewModel { public string[] Codes { get; set; } = []; [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm that the recovery codes were saved.")] public bool Confirmed { get; set; } public string? ReturnUrl { get; set; } }
public sealed record AuditLogViewModel(IReadOnlyList<AuthenticationAuditEvent> Events, int Page, int TotalPages);
