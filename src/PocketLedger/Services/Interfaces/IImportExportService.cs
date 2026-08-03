namespace PocketLedger.Services.Interfaces;

public interface IImportExportService
{
    Task<string> ExportCsvAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken cancellationToken);
    Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken cancellationToken);
    Task<string> ExportBackupAsync(CancellationToken cancellationToken);
    RestorePreview PreviewRestore(string json);
    Task RestoreAsync(string json, CancellationToken cancellationToken);
}
