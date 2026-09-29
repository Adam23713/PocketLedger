using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oci.Common;
using Oci.Common.Auth;
using Oci.Common.Retry;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Oci.KeymanagementService;
using Oci.KeymanagementService.Models;
using Oci.KeymanagementService.Requests;

namespace PocketLedger.Security;

public interface IKeyEncryptionProvider : IXmlEncryptor
{
    string Name { get; }
}

internal sealed class LocalKeyEncryptionProvider(X509Certificate2 certificate, ILoggerFactory loggerFactory) : IKeyEncryptionProvider
{
    private readonly CertificateXmlEncryptor encryptor = new(certificate, loggerFactory);

    public string Name => EncryptionProviderNames.Local;
    public EncryptedXmlInfo Encrypt(XElement plaintextElement) => encryptor.Encrypt(plaintextElement);
}

internal interface IOciKmsClient : IDisposable
{
    EncryptedData Encrypt(EncryptDataDetails details);
    DecryptedData Decrypt(DecryptDataDetails details);
}

internal sealed class OciSdkKmsClient : IOciKmsClient
{
    private readonly KmsCryptoClient client;

    public OciSdkKmsClient(OciVaultOptions options, OciCredential credential)
    {
        var privateKey = (RsaKeyParameters)PrivateKeyFactory.CreateKey(credential.PrivateKeyPkcs8);
        var authentication = new SimpleAuthenticationDetailsProvider
        {
            TenantId = credential.TenancyId,
            UserId = credential.UserId,
            Fingerprint = credential.Fingerprint,
            Region = Region.FromRegionId(credential.Region),
            PrivateKeySupplier = new FixedPrivateKeySupplier(privateKey)
        };
        var retry = new RetryConfiguration
        {
            MaxAttempts = options.MaxAttempts,
            TotalElapsedTimeInSecs = options.TimeoutSeconds,
            GetNextDelayInSeconds = attempt => Math.Min(Math.Pow(2, attempt - 1), 4)
        };
        client = new KmsCryptoClient(authentication, new ClientConfiguration
        {
            TimeoutMillis = checked(options.TimeoutSeconds * 1000),
            RetryConfiguration = retry,
            ClientUserAgent = "PocketLedger/KeyEncryption"
        }, options.CryptoEndpoint);
    }

    public EncryptedData Encrypt(EncryptDataDetails details) => client.Encrypt(new EncryptRequest { EncryptDataDetails = details }).GetAwaiter().GetResult().EncryptedData;
    public DecryptedData Decrypt(DecryptDataDetails details) => client.Decrypt(new DecryptRequest { DecryptDataDetails = details }).GetAwaiter().GetResult().DecryptedData;
    public void Dispose() => client.Dispose();

    private sealed class FixedPrivateKeySupplier(RsaKeyParameters privateKey) : ISupplier<RsaKeyParameters>
    {
        public RsaKeyParameters GetKey() => privateKey;
    }
}

internal sealed class OciKmsKeyEncryptionProvider : IKeyEncryptionProvider, IDisposable
{
    internal const string ElementName = "ociKmsWrappedKey";
    internal const string Algorithm = "AES_256_GCM";
    private static readonly IReadOnlyDictionary<string, string> AssociatedData = new Dictionary<string, string> { ["purpose"] = "PocketLedger.DataProtectionKey.v1" };
    private readonly IOciKmsClient client;
    private readonly string keyId;

    public OciKmsKeyEncryptionProvider(OciVaultOptions options, OciCredential credential)
        : this(options.KeyId, new OciSdkKmsClient(options, credential))
    {
    }

    internal OciKmsKeyEncryptionProvider(string keyId, IOciKmsClient client)
    {
        this.keyId = keyId;
        this.client = client;
    }

    public string Name => EncryptionProviderNames.OciVault;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        string plaintext;
        try
        {
            plaintext = Convert.ToBase64String(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
        try
        {
            var encrypted = client.Encrypt(new EncryptDataDetails
            {
                KeyId = keyId,
                Plaintext = plaintext,
                EncryptionAlgorithm = EncryptDataDetails.EncryptionAlgorithmEnum.Aes256Gcm,
                AssociatedData = new Dictionary<string, string>(AssociatedData),
                LoggingContext = new Dictionary<string, string> { ["application"] = "PocketLedger", ["purpose"] = "DataProtectionKey" }
            });
            if (string.IsNullOrWhiteSpace(encrypted?.Ciphertext)) throw new CryptographicException("OCI KMS returned an empty ciphertext.");
            var element = new XElement(ElementName,
                new XAttribute("version", 1),
                new XAttribute("algorithm", Algorithm),
                new XAttribute("keyId", encrypted.KeyId ?? keyId),
                new XAttribute("keyVersionId", encrypted.KeyVersionId ?? string.Empty),
                new XElement("ciphertext", encrypted.Ciphertext));
            return new EncryptedXmlInfo(element, typeof(OciKmsXmlDecryptor));
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CryptographicException("OCI KMS could not wrap the Data Protection key.", exception);
        }
    }

    internal XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);
        if (encryptedElement.Name.LocalName != ElementName || (string?)encryptedElement.Attribute("version") != "1" || (string?)encryptedElement.Attribute("algorithm") != Algorithm)
            throw new CryptographicException("The OCI KMS wrapped key format is invalid.");
        var wrappedKeyId = (string?)encryptedElement.Attribute("keyId");
        var keyVersionId = (string?)encryptedElement.Attribute("keyVersionId");
        var ciphertext = (string?)encryptedElement.Element("ciphertext");
        if (string.IsNullOrWhiteSpace(wrappedKeyId) || string.IsNullOrWhiteSpace(ciphertext)) throw new CryptographicException("The OCI KMS wrapped key is incomplete.");
        if (!string.Equals(wrappedKeyId, keyId, StringComparison.Ordinal)) throw new CryptographicException("The OCI KMS wrapped key does not belong to the configured key.");
        try
        {
            var decrypted = client.Decrypt(new DecryptDataDetails
            {
                KeyId = wrappedKeyId,
                KeyVersionId = string.IsNullOrWhiteSpace(keyVersionId) ? null : keyVersionId,
                Ciphertext = ciphertext,
                EncryptionAlgorithm = DecryptDataDetails.EncryptionAlgorithmEnum.Aes256Gcm,
                AssociatedData = new Dictionary<string, string>(AssociatedData),
                LoggingContext = new Dictionary<string, string> { ["application"] = "PocketLedger", ["purpose"] = "DataProtectionKey" }
            });
            var plaintext = Convert.FromBase64String(decrypted?.Plaintext ?? throw new CryptographicException("OCI KMS returned an empty plaintext."));
            try
            {
                return XElement.Parse(Encoding.UTF8.GetString(plaintext), LoadOptions.None);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CryptographicException("OCI KMS could not unwrap the Data Protection key.", exception);
        }
    }

    public void Dispose() => client.Dispose();
}

public sealed class OciKmsXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
        => services.GetRequiredService<IKeyEncryptionProvider>() switch
        {
            OciKmsKeyEncryptionProvider provider => provider.Decrypt(encryptedElement),
            LockedOciKeyEncryptionProvider provider => provider.Decrypt(encryptedElement),
            _ => throw new CryptographicException("OCI KMS key decryption is not configured.")
        };
}

internal sealed record OciVaultOptions(string KeyId, string CryptoEndpoint, string EncryptedCredentialPath, string UnlockSocketPath, int TimeoutSeconds, int MaxAttempts);

public static class EncryptionProviderNames
{
    public const string Local = "Local";
    public const string OciVault = "OciVault";
}
