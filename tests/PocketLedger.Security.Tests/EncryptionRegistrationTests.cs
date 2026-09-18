using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PocketLedger.Security;

namespace PocketLedger.Security.Tests;

public sealed class EncryptionRegistrationTests
{
    [Fact]
    public void EncryptedOciProductionPath_DoesNotRequireConfigOrPlaintextPemFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pocketledger-registration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var credentialPath = Path.Combine(root, "api.enc");
        File.WriteAllBytes(credentialPath, [1]);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(credentialPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Encryption:KeyProvider"] = "OciVault",
                ["Encryption:KeyDirectory"] = root,
                ["Encryption:OciVault:KeyId"] = "ocid1.key.oc1..test",
                ["Encryption:OciVault:CryptoEndpoint"] = "https://example.invalid",
                ["Encryption:OciVault:EncryptedCredentialPath"] = credentialPath,
                ["Encryption:OciVault:UnlockSocketPath"] = Path.Combine(root, "unlock.sock")
            }).Build();
            var services = new ServiceCollection();

            services.AddDatabaseEncryption(configuration, new ProductionEnvironment(root), "test");
            using var provider = services.BuildServiceProvider();

            Assert.IsType<LockedOciKeyEncryptionProvider>(provider.GetRequiredService<IKeyEncryptionProvider>());
            Assert.False(provider.GetRequiredService<EncryptionRuntimeState>().IsReady);
            Assert.Null(configuration["Encryption:OciVault:ConfigFilePath"]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ProductionEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "PocketLedger.Security.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
