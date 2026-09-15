using System.Net.Http.Headers;
using PocketLedger.Contracts;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class ImportExportApiClient(HttpClient client) : ApiClientBase(client), IImportExportService, IEncryptedBackupService
{
    public async Task<byte[]> ExportExcelAsync(TransactionFilter filter, string password, CancellationToken token)
    {
        using var response = await HttpClient.PostAsJsonAsync("api/v1/import-export/excel/export", new ExcelExportRequest(filter, password), JsonOptions, token);
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadAsByteArrayAsync(token);
    }
    public Task<CsvImportPreview> PreviewCsvAsync(string csv, CancellationToken token) => PostAsync<TextPayload, CsvImportPreview>("api/v1/import-export/csv/preview", new TextPayload(csv), token);
    public Task<CsvImportResult> ImportCsvAsync(string csv, CancellationToken token) => PostAsync<TextPayload, CsvImportResult>("api/v1/import-export/csv/import", new TextPayload(csv), token);
    public async Task<byte[]> ExportEncryptedBackupAsync(string password, CancellationToken token)
    {
        using var content = PasswordContent(password);
        using var response = await HttpClient.PostAsync("api/v1/import-export/backup/encrypted", content, token);
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadAsByteArrayAsync(token);
    }
    public RestorePreview PreviewEncryptedRestore(byte[] encryptedBackup, string password) => PostEncryptedAsync<RestorePreview>("api/v1/import-export/restore/encrypted/preview", encryptedBackup, password, CancellationToken.None).GetAwaiter().GetResult();
    public Task RestoreEncryptedAsync(byte[] encryptedBackup, string password, CancellationToken token) => PostEncryptedAsync("api/v1/import-export/restore/encrypted", encryptedBackup, password, token);

    private async Task<T> PostEncryptedAsync<T>(string uri, byte[] encryptedBackup, string password, CancellationToken token)
    {
        using var content = EncryptedBackupContent(encryptedBackup, password);
        using var response = await HttpClient.PostAsync(uri, content, token);
        await EnsureSuccessAsync(response, token);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, token))!;
    }

    private async Task PostEncryptedAsync(string uri, byte[] encryptedBackup, string password, CancellationToken token)
    {
        using var content = EncryptedBackupContent(encryptedBackup, password);
        using var response = await HttpClient.PostAsync(uri, content, token);
        await EnsureSuccessAsync(response, token);
    }

    private static MultipartFormDataContent PasswordContent(string password)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(password), "password");
        return content;
    }

    private static MultipartFormDataContent EncryptedBackupContent(byte[] encryptedBackup, string password)
    {
        var content = PasswordContent(password);
        var file = new ByteArrayContent(encryptedBackup);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", "backup.plbackup");
        return content;
    }
}
