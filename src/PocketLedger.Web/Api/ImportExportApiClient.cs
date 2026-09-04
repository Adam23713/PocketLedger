using PocketLedger.Contracts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class ImportExportApiClient(HttpClient client) : ApiClientBase(client), IImportExportService
{
    public async Task<string> ExportCsvAsync(TransactionFilter filter, CancellationToken token) => (await PostAsync<TransactionFilter, TextPayload>("api/v1/import-export/csv/export", filter, token)).Content;
    public Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken token) => PostAsync<TextPayload, CsvImportPreview>("api/v1/import-export/csv/preview", new TextPayload(csv), token);
    public Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken token) => PostAsync<TextPayload, CsvImportResult>("api/v1/import-export/csv/import", new TextPayload(csv), token);
    public async Task<string> ExportBackupAsync(CancellationToken token) => (await GetAsync<TextPayload>("api/v1/import-export/backup", token)).Content;
    public RestorePreview PreviewRestore(string json) => PostAsync<TextPayload, RestorePreview>("api/v1/import-export/restore/preview", new TextPayload(json), CancellationToken.None).GetAwaiter().GetResult();
    public Task RestoreAsync(string json, CancellationToken token) => PostAsync("api/v1/import-export/restore", new TextPayload(json), token);
}
