namespace PocketLedger.Services.Interfaces;

public interface IImportExportService
{
    Task<byte[]> ExportExcelAsync(TransactionFilter filter, string password, CancellationToken cancellationToken);
    Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken cancellationToken);
    Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken cancellationToken);
}
