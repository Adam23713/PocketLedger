using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PocketLedger.Security;

public static class EncryptionRegistration
{
    private const string DataProtectionHostedServiceTypeName = "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService";

    public static IServiceCollection AddDatabaseEncryption(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, string applicationName)
    {
        var providerName = configuration["Encryption:KeyProvider"]?.Trim() ?? EncryptionProviderNames.Local;
        var directory = configuration["Encryption:KeyDirectory"];
        var certificatePath = configuration["Encryption:CertificatePath"];
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Encryption:KeyDirectory is required outside Development. See docs/database-encryption.md.");

        directory = string.IsNullOrWhiteSpace(directory) ? Path.Combine(environment.ContentRootPath, ".local", "encryption") : directory;
        if (!environment.IsDevelopment() && !Directory.Exists(directory))
            throw new InvalidOperationException("The encryption key directory must already exist and be mounted before startup.");
        Directory.CreateDirectory(directory);
        services.AddSingleton(new KeyRingStorageOptions(directory));
        services.AddTransient<KeyRingMigration>();
        var requiresUnlock = providerName.Equals(EncryptionProviderNames.OciVault, StringComparison.OrdinalIgnoreCase);
        services.AddSingleton(new EncryptionRuntimeState(requiresUnlock));
        var protection = services.AddDataProtection().SetApplicationName(applicationName).PersistKeysToFileSystem(new DirectoryInfo(directory));
        var previousCertificatePaths = configuration.GetSection("Encryption:PreviousCertificatePaths").Get<string[]>() ?? [];
        var providerConfigured = false;
        if (providerName.Equals(EncryptionProviderNames.Local, StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(certificatePath))
                throw new InvalidOperationException("Encryption:CertificatePath is required for the Local key provider outside Development.");
            if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                services.AddSingleton<IKeyEncryptionProvider>(provider => new LocalKeyEncryptionProvider(LoadCertificate(certificatePath), provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
                providerConfigured = true;
            }
        }
        else if (providerName.Equals(EncryptionProviderNames.OciVault, StringComparison.OrdinalIgnoreCase))
        {
            var options = LoadOciOptions(configuration);
            services.AddSingleton(options);
            services.AddSingleton<LockedOciKeyEncryptionProvider>();
            services.AddSingleton<IKeyEncryptionProvider>(provider => provider.GetRequiredService<LockedOciKeyEncryptionProvider>());
            services.AddHostedService<OciUnlockSocketService>();
            providerConfigured = true;
        }
        else
        {
            throw new InvalidOperationException($"Unknown Encryption:KeyProvider '{providerName}'. Supported values are Local and OciVault.");
        }
        if (providerConfigured) services.AddOptions<KeyManagementOptions>().Configure<IKeyEncryptionProvider>((options, provider) => options.XmlEncryptor = provider);
        var decryptionCertificates = new List<X509Certificate2>();
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = LoadCertificate(certificatePath);
            decryptionCertificates.Add(certificate);
        }
        decryptionCertificates.AddRange(previousCertificatePaths.Select(LoadCertificate));
        if (decryptionCertificates.Count > 0) protection.UnprotectKeysWithAnyCertificate(decryptionCertificates.ToArray());
        services.AddSingleton<DatabaseEncryption>();
        if (!requiresUnlock) services.AddHostedService<EncryptionStartupCheck>();
        return services;
    }

    public static IServiceCollection CompleteDatabaseEncryptionRegistration(this IServiceCollection services, IConfiguration configuration)
    {
        var providerName = configuration["Encryption:KeyProvider"]?.Trim() ?? EncryptionProviderNames.Local;
        if (!providerName.Equals(EncryptionProviderNames.OciVault, StringComparison.OrdinalIgnoreCase)) return services;

        // Authentication packages can register the eager loader again, so remove it only after all application services are configured.
        RemoveEagerDataProtectionKeyRingLoader(services);
        return services;
    }

    private static OciVaultOptions LoadOciOptions(IConfiguration configuration)
    {
        var keyId = Required(configuration, "Encryption:OciVault:KeyId");
        var cryptoEndpoint = Required(configuration, "Encryption:OciVault:CryptoEndpoint");
        var encryptedCredentialPath = Required(configuration, "Encryption:OciVault:EncryptedCredentialPath");
        var unlockSocketPath = configuration["Encryption:OciVault:UnlockSocketPath"]?.Trim() ?? "/tmp/pocketledger-unlock/unlock.sock";
        var timeoutSeconds = configuration.GetValue("Encryption:OciVault:TimeoutSeconds", 10);
        var maxAttempts = configuration.GetValue("Encryption:OciVault:MaxAttempts", 3);
        if (!keyId.StartsWith("ocid1.key.", StringComparison.Ordinal)) throw new InvalidOperationException("Encryption:OciVault:KeyId must be an OCI key OCID.");
        if (!Uri.TryCreate(cryptoEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Encryption:OciVault:CryptoEndpoint must be an absolute HTTPS URI.");
        if (!File.Exists(encryptedCredentialPath)) throw new InvalidOperationException("Encryption:OciVault:EncryptedCredentialPath does not exist.");
        if (!Path.IsPathFullyQualified(unlockSocketPath)) throw new InvalidOperationException("Encryption:OciVault:UnlockSocketPath must be absolute.");
        if (timeoutSeconds is < 1 or > 120) throw new InvalidOperationException("Encryption:OciVault:TimeoutSeconds must be between 1 and 120.");
        if (maxAttempts is < 1 or > 5) throw new InvalidOperationException("Encryption:OciVault:MaxAttempts must be between 1 and 5.");
        return new OciVaultOptions(keyId, endpoint.ToString().TrimEnd('/'), encryptedCredentialPath, unlockSocketPath, timeoutSeconds, maxAttempts);
    }

    private static string Required(IConfiguration configuration, string key)
        => string.IsNullOrWhiteSpace(configuration[key]) ? throw new InvalidOperationException($"{key} is required for the OCI Vault key provider.") : configuration[key]!.Trim();

    private static void RemoveEagerDataProtectionKeyRingLoader(IServiceCollection services)
    {
        var registrations = services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.FullName == DataProtectionHostedServiceTypeName).ToArray();
        if (registrations.Length != 1) throw new InvalidOperationException("The Data Protection startup key-ring loader registration could not be identified safely.");
        services.Remove(registrations[0]);
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.EphemeralKeySet);
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null || rsa.KeySize < 3072) throw new InvalidOperationException("Encryption requires a certificate with an RSA private key of at least 3072 bits.");
        return certificate;
    }

    private sealed class EncryptionStartupCheck(IDataProtectionProvider provider, IKeyManager keyManager, KeyRingStorageOptions storage) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            EncryptionReadinessCheck.Verify(provider, keyManager, storage);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

internal static class EncryptionReadinessCheck
{
    public static void Verify(IDataProtectionProvider provider, IKeyManager keyManager, KeyRingStorageOptions storage)
    {
        if (File.Exists(Path.Combine(storage.Directory, KeyRingMigration.InProgressMarker)))
            throw new CryptographicException("An interrupted key-ring migration must be recovered before PocketLedger can start.");
        var persistedKeyCount = Directory.EnumerateFiles(storage.Directory, "key-*.xml", SearchOption.TopDirectoryOnly).Count();
        var keys = keyManager.GetAllKeys();
        if (keys.Count != persistedKeyCount) throw new CryptographicException("The Data Protection key ring did not load every persisted key.");
        foreach (var key in keys)
        {
            try
            {
                if (key.CreateEncryptor() is null) throw new CryptographicException("The Data Protection key produced no authenticated encryptor.");
            }
            catch (Exception exception)
            {
                throw new CryptographicException($"Data Protection key '{key.KeyId}' could not be activated.", exception);
            }
        }
        var protector = provider.CreateProtector("PocketLedger.Encryption.StartupCheck.v1");
        var probe = RandomNumberGenerator.GetBytes(32);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(probe, protector.Unprotect(protector.Protect(probe))))
                throw new CryptographicException("Encryption startup check failed.");
        }
        finally { CryptographicOperations.ZeroMemory(probe); }
    }
}
