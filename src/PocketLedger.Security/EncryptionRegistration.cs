using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PocketLedger.Security;

public static class EncryptionRegistration
{
    public static IServiceCollection AddDatabaseEncryption(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, string applicationName)
    {
        var directory = configuration["Encryption:KeyDirectory"];
        var certificatePath = configuration["Encryption:CertificatePath"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(certificatePath)))
            throw new InvalidOperationException("Encryption:KeyDirectory and Encryption:CertificatePath are required outside Development. See docs/database-encryption.md.");

        directory = string.IsNullOrWhiteSpace(directory) ? Path.Combine(environment.ContentRootPath, ".local", "encryption") : directory;
        if (!environment.IsDevelopment() && !Directory.Exists(directory))
            throw new InvalidOperationException("The encryption key directory must already exist and be mounted before startup.");
        Directory.CreateDirectory(directory);
        var protection = services.AddDataProtection().SetApplicationName(applicationName).PersistKeysToFileSystem(new DirectoryInfo(directory));
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = LoadCertificate(certificatePath);
            protection.ProtectKeysWithCertificate(certificate);
            var oldCertificates = configuration.GetSection("Encryption:PreviousCertificatePaths").Get<string[]>() ?? [];
            protection.UnprotectKeysWithAnyCertificate([certificate, .. oldCertificates.Select(LoadCertificate)]);
        }
        services.AddSingleton<DatabaseEncryption>();
        services.AddHostedService<EncryptionStartupCheck>();
        return services;
    }

    private static X509Certificate2 LoadCertificate(string path)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.EphemeralKeySet);
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is null || rsa.KeySize < 3072) throw new InvalidOperationException("Encryption requires a certificate with an RSA private key of at least 3072 bits.");
        return certificate;
    }

    private sealed class EncryptionStartupCheck(IDataProtectionProvider provider) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var protector = provider.CreateProtector("PocketLedger.Encryption.StartupCheck.v1");
            var probe = RandomNumberGenerator.GetBytes(32);
            if (!CryptographicOperations.FixedTimeEquals(probe, protector.Unprotect(protector.Protect(probe))))
                throw new CryptographicException("Encryption startup check failed.");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
