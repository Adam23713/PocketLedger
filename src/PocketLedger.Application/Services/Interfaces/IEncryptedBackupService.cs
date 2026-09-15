namespace PocketLedger.Services.Interfaces;

public interface IEncryptedBackupService
{
    Task<byte[]> ExportEncryptedBackupAsync(string password, CancellationToken cancellationToken);
    RestorePreview PreviewEncryptedRestore(byte[] encryptedBackup, string password);
    Task RestoreEncryptedAsync(byte[] encryptedBackup, string password, CancellationToken cancellationToken);
}
