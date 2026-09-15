using System.ComponentModel.DataAnnotations;
using PocketLedger.Services;

namespace PocketLedger.Models.ViewModels.ImportExport;

public class CsvImportViewModel
{
    public string Csv { get; set; } = string.Empty;
    public CsvImportPreview? Preview { get; set; }
}

public class RestoreViewModel
{
    public string EncryptedContent { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public RestorePreview? Preview { get; set; }
    public bool Confirm { get; set; }
}

public class ImportExportIndexViewModel
{
    public ExcelExportViewModel ExcelExport { get; set; } = new();
    public EncryptedBackupExportViewModel EncryptedBackup { get; set; } = new();
}

public class ExcelExportViewModel : IValidatableObject
{
    public TransactionFilter Filter { get; set; } = new();

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(Password)) return [];
        int length;
        try { length = BackupProtectionFormat.PasswordLength(Password); }
        catch (ArgumentException)
        {
            return [new ValidationResult("The Excel export password contains invalid Unicode characters.", [nameof(Password)])];
        }
        if (length is < BackupProtectionFormat.MinimumPasswordLength or > BackupProtectionFormat.MaximumPasswordLength)
            return [new ValidationResult($"The Excel export password must contain between {BackupProtectionFormat.MinimumPasswordLength} and {BackupProtectionFormat.MaximumPasswordLength} characters.", [nameof(Password)])];
        return [];
    }
}

public class EncryptedBackupExportViewModel : IValidatableObject
{
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrEmpty(Password)) return [];
        int length;
        try { length = BackupProtectionFormat.PasswordLength(Password); }
        catch (ArgumentException)
        {
            return [new ValidationResult("The backup password contains invalid Unicode characters.", [nameof(Password)])];
        }
        if (length is < BackupProtectionFormat.MinimumPasswordLength or > BackupProtectionFormat.MaximumPasswordLength)
            return [new ValidationResult($"The backup password must contain between {BackupProtectionFormat.MinimumPasswordLength} and {BackupProtectionFormat.MaximumPasswordLength} characters.", [nameof(Password)])];
        return [];
    }
}
