namespace PocketLedger.Models.Enums;

public enum DebtOperationType
{
    Payment,
    EarlyRepayment,
    Increase,
    ManualCorrectionIncrease,
    ManualCorrectionDecrease,
    LoanDisbursement,
    ReceivedRepayment
}
