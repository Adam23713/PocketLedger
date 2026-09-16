using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PocketLedger.Web.Api;

namespace PocketLedger.Tests;

public sealed class InternalApiTlsTests
{
    [Fact]
    public void ValidateConfiguration_RejectsHttpOutsideDevelopment()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() => InternalApiTls.ValidateConfiguration(new Uri("http://api:5051/"), configuration, new TestEnvironment("Production")));

        Assert.Contains("must use HTTPS", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_RequiresExistingTrustedRootOutsideDevelopment()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:TlsTrustedRootCertificatePath"] = "/missing/internal-ca.crt"
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => InternalApiTls.ValidateConfiguration(new Uri("https://api:5051/"), configuration, new TestEnvironment("Production")));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public void ValidateConfiguration_AllowsDevelopmentHttpWithoutCustomRoot()
    {
        InternalApiTls.ValidateConfiguration(new Uri("http://localhost:5051/"), new ConfigurationBuilder().Build(), new TestEnvironment("Development"));
    }

    [Fact]
    public void CertificateHandler_AcceptsOnlyCertificatesChainedToConfiguredRootWithoutSslIdentityErrors()
    {
        using var fixture = new CertificateFixture();
        using var handler = new InternalApiCertificateHandler(fixture.RootPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api:5051/health");

        Assert.True(handler.ValidateServerCertificate(request, fixture.ServerCertificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(handler.ValidateServerCertificate(request, fixture.ServerCertificate, null, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.False(handler.ValidateServerCertificate(request, fixture.UntrustedServerCertificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "PocketLedger.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CertificateFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"pocketledger-tls-{Guid.NewGuid():N}");

        public CertificateFixture()
        {
            Directory.CreateDirectory(directory);
            using var trustedRoot = CreateCertificateAuthority("Trusted test root");
            using var untrustedRoot = CreateCertificateAuthority("Untrusted test root");
            RootPath = Path.Combine(directory, "ca.crt");
            File.WriteAllBytes(RootPath, trustedRoot.Export(X509ContentType.Cert));
            ServerCertificate = CreateServerCertificate(trustedRoot, "api");
            UntrustedServerCertificate = CreateServerCertificate(untrustedRoot, "api");
        }

        public string RootPath { get; }
        public X509Certificate2 ServerCertificate { get; }
        public X509Certificate2 UntrustedServerCertificate { get; }

        public void Dispose()
        {
            ServerCertificate.Dispose();
            UntrustedServerCertificate.Dispose();
            Directory.Delete(directory, true);
        }

        private static X509Certificate2 CreateCertificateAuthority(string commonName)
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        }

        private static X509Certificate2 CreateServerCertificate(X509Certificate2 issuer, string dnsName)
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(dnsName);
            request.CertificateExtensions.Add(san.Build());
            var serial = RandomNumberGenerator.GetBytes(16);
            return request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), serial);
        }
    }
}
