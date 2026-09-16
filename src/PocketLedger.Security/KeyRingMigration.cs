using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace PocketLedger.Security;

public static class KeyRingMigrationCommand
{
    public const string Name = "rewrap-key-ring";

    public static int Run(IServiceProvider services)
    {
        try
        {
            var result = services.GetRequiredService<KeyRingMigration>().Rewrap();
            Console.WriteLine($"Rewrapped {result.KeyCount} Data Protection key(s) with the {result.ProviderName} provider. Backup: {result.BackupDirectory}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Key-ring migration failed: {exception.Message}");
            return 1;
        }
    }
}

internal sealed class KeyRingMigration(IServiceProvider services)
{
    internal const string InProgressMarker = ".rewrap-in-progress";

    public KeyRingMigrationResult Rewrap()
    {
        var storage = services.GetRequiredService<KeyRingStorageOptions>();
        var targetProvider = services.GetRequiredService<IKeyEncryptionProvider>();
        if (!Directory.Exists(storage.Directory)) throw new InvalidOperationException("The configured key-ring directory does not exist.");
        var markerPath = Path.Combine(storage.Directory, InProgressMarker);
        if (File.Exists(markerPath)) throw new InvalidOperationException($"A previous key-ring migration was interrupted. Restore the top-level key files from the backup named in {markerPath}, then remove the marker before retrying.");
        var keyFiles = Directory.EnumerateFiles(storage.Directory, "key-*.xml", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray();
        if (keyFiles.Length == 0) throw new InvalidOperationException("The configured key-ring directory contains no Data Protection keys.");

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var backupDirectory = Path.Combine(storage.Directory, $".rewrap-backup-{timestamp}");
        var stagingDirectory = Path.Combine(storage.Directory, $".rewrap-staging-{Guid.NewGuid():N}");
        CreatePrivateDirectory(backupDirectory);
        CreatePrivateDirectory(stagingDirectory);
        var committed = new List<string>();
        try
        {
            File.WriteAllText(markerPath, backupDirectory);
            SetPrivateFileMode(markerPath);
            foreach (var keyFile in keyFiles)
            {
                var fileName = Path.GetFileName(keyFile);
                var backupPath = Path.Combine(backupDirectory, fileName);
                File.Copy(keyFile, backupPath, overwrite: false);
                SetPrivateFileMode(backupPath);
                var migrated = Rewrap(XElement.Load(keyFile, LoadOptions.PreserveWhitespace), targetProvider);
                var stagedPath = Path.Combine(stagingDirectory, fileName);
                Save(migrated, stagedPath);
                ValidateTargetEncryption(XElement.Load(stagedPath, LoadOptions.PreserveWhitespace));
            }

            foreach (var keyFile in keyFiles)
            {
                var fileName = Path.GetFileName(keyFile);
                File.Move(Path.Combine(stagingDirectory, fileName), keyFile, overwrite: true);
                committed.Add(fileName);
                SetPrivateFileMode(keyFile);
            }
            Directory.Delete(stagingDirectory);
            File.Delete(markerPath);
            return new KeyRingMigrationResult(keyFiles.Length, targetProvider.Name, backupDirectory);
        }
        catch
        {
            foreach (var fileName in committed)
            {
                var keyFile = Path.Combine(storage.Directory, fileName);
                File.Copy(Path.Combine(backupDirectory, fileName), keyFile, overwrite: true);
                SetPrivateFileMode(keyFile);
            }
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            if (File.Exists(markerPath)) File.Delete(markerPath);
            throw;
        }
    }

    private XElement Rewrap(XElement document, IKeyEncryptionProvider targetProvider)
    {
        var encryptedSecret = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "encryptedSecret");
        XElement plaintext;
        XElement replacedNode;
        if (encryptedSecret is not null)
        {
            var decryptorTypeName = (string?)encryptedSecret.Attribute("decryptorType");
            if (string.IsNullOrWhiteSpace(decryptorTypeName)) throw new CryptographicException("A key-ring entry has no decryptor type.");
            var encryptedElement = encryptedSecret.Elements().SingleOrDefault() ?? throw new CryptographicException("A key-ring entry has no encrypted payload.");
            var decryptor = CreateDecryptor(decryptorTypeName);
            plaintext = decryptor.Decrypt(encryptedElement);
            replacedNode = encryptedSecret;
        }
        else
        {
            plaintext = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "masterKey" && element.Attributes().Any(attribute => attribute.Name.LocalName == "requiresEncryption" && attribute.Value == "true"))
                ?? throw new CryptographicException("A key-ring entry contains no rewrappable key material.");
            replacedNode = plaintext;
        }

        var wrapped = targetProvider.Encrypt(plaintext);
        var targetDecryptorTypeName = wrapped.DecryptorType.AssemblyQualifiedName ?? throw new CryptographicException("The target key provider has no serializable decryptor type.");
        replacedNode.ReplaceWith(new XElement("encryptedSecret", new XAttribute("decryptorType", targetDecryptorTypeName), wrapped.EncryptedElement));
        return document;
    }

    private void ValidateTargetEncryption(XElement document)
    {
        var encryptedSecret = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "encryptedSecret")
            ?? throw new CryptographicException("The rewrapped key contains no encrypted secret.");
        var decryptorTypeName = (string?)encryptedSecret.Attribute("decryptorType") ?? throw new CryptographicException("The rewrapped key contains no decryptor type.");
        var encryptedElement = encryptedSecret.Elements().SingleOrDefault() ?? throw new CryptographicException("The rewrapped key contains no encrypted payload.");
        var decryptor = CreateDecryptor(decryptorTypeName);
        var plaintext = decryptor.Decrypt(encryptedElement);
        if (plaintext.Name.LocalName != "masterKey") throw new CryptographicException("The rewrapped key did not decrypt to Data Protection key material.");
    }

    private IXmlDecryptor CreateDecryptor(string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: true) ?? throw new CryptographicException("The key decryptor type could not be loaded.");
        if (!typeof(IXmlDecryptor).IsAssignableFrom(type)) throw new CryptographicException("The key decryptor type is invalid.");
        return (IXmlDecryptor)ActivatorUtilities.CreateInstance(services, type);
    }

    private static void Save(XElement document, string path)
    {
        var settings = new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), Indent = true, CloseOutput = false };
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = XmlWriter.Create(stream, settings))
            {
                document.Save(writer);
                writer.Flush();
            }
            stream.Flush(flushToDisk: true);
        }
        SetPrivateFileMode(path);
    }

    private static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void SetPrivateFileMode(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

internal sealed record KeyRingStorageOptions(string Directory);
internal sealed record KeyRingMigrationResult(int KeyCount, string ProviderName, string BackupDirectory);
