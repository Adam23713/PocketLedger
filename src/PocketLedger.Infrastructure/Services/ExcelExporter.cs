using System.Text;
using OfficeOpenXml;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

internal static class ExcelExporter
{
    private const int MaximumDataRows = 1_048_575;

    static ExcelExporter()
    {
        // EPPlus is configured under its noncommercial license here. Commercial deployments must configure a commercial license or replace this component.
        ExcelPackage.License.SetNonCommercialOrganization("PocketLedger");
    }

    public static byte[] Create(IReadOnlyList<Transaction> transactions, string password)
    {
        var normalizedPassword = ValidatePassword(password);
        if (transactions.Count > MaximumDataRows) throw new BusinessRuleException($"Excel export supports at most {MaximumDataRows:N0} transactions.");

        using var package = new ExcelPackage();
        package.Encryption.Algorithm = EncryptionAlgorithm.AES256;
        var worksheet = package.Workbook.Worksheets.Add("Transactions");
        var headers = new[] { "Date", "Account", "Type", "Category", "Amount", "Currency", "Note" };
        for (var column = 0; column < headers.Length; column++) worksheet.Cells[1, column + 1].Value = headers[column];

        for (var index = 0; index < transactions.Count; index++)
        {
            var transaction = transactions[index];
            var row = index + 2;
            worksheet.Cells[row, 1].Value = transaction.TransactionDate.ToDateTime(TimeOnly.MinValue);
            worksheet.Cells[row, 2].Value = transaction.Account?.Name;
            worksheet.Cells[row, 3].Value = transaction.Type.ToString();
            worksheet.Cells[row, 4].Value = transaction.Category?.Name;
            worksheet.Cells[row, 5].Value = transaction.Amount;
            worksheet.Cells[row, 6].Value = transaction.Account?.Currency ?? transaction.Debt?.Currency;
            worksheet.Cells[row, 7].Value = transaction.Note;
        }

        worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
        worksheet.Cells[1, 1, Math.Max(1, transactions.Count + 1), headers.Length].AutoFilter = true;
        worksheet.Cells[2, 1, Math.Max(2, transactions.Count + 1), 1].Style.Numberformat.Format = "yyyy-mm-dd";
        worksheet.Cells[2, 5, Math.Max(2, transactions.Count + 1), 5].Style.Numberformat.Format = "#,##0.####";
        worksheet.Column(1).Width = 12;
        worksheet.Column(2).Width = 24;
        worksheet.Column(3).Width = 16;
        worksheet.Column(4).Width = 24;
        worksheet.Column(5).Width = 16;
        worksheet.Column(6).Width = 12;
        worksheet.Column(7).Width = 48;
        worksheet.View.FreezePanes(2, 1);
        return package.GetAsByteArray(normalizedPassword);
    }

    private static string ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new BusinessRuleException("Excel export password is required.");
        int length;
        try { length = BackupProtectionFormat.PasswordLength(password); }
        catch (ArgumentException) { throw new BusinessRuleException("Excel export password contains invalid Unicode characters."); }
        if (length is < BackupProtectionFormat.MinimumPasswordLength or > BackupProtectionFormat.MaximumPasswordLength)
            throw new BusinessRuleException($"Excel export password must contain between {BackupProtectionFormat.MinimumPasswordLength} and {BackupProtectionFormat.MaximumPasswordLength} characters.");
        return password.Normalize(NormalizationForm.FormC);
    }
}
