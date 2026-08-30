using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Contracts;

public sealed record ApiError(string Code, string Message);
public sealed record AccountUpdateRequest(Account Account, bool CreateInitialBalanceAdjustment);
public sealed record DebtWriteRequest(Debt Debt, RecurringPaymentInput? RecurringPayment);
public sealed record DebtOperationWriteRequest(DebtOperationInput Operation);
public sealed record TextPayload(string Content);
public sealed record UserPreferenceResponse(string? DisplayName, int AvatarId, string DefaultCurrency, string TimeZoneId, IReadOnlyList<UserCurrencyFormat> CurrencyFormats);
public sealed record UserPreferenceUpdateRequest(string? DisplayName, int AvatarId, string DefaultCurrency, string TimeZoneId, IReadOnlyList<UserCurrencyFormat> CurrencyFormats);
