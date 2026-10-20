using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PocketLedger.Services;

internal static class InternalApiTls
{
    internal static HttpMessageHandler CreateHandler(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return new HttpClientHandler();
        var path = configuration["Api:TlsTrustedRootCertificatePath"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidOperationException("Api:TlsTrustedRootCertificatePath must reference the internal TLS root certificate.");
        return new InternalApiCertificateHandler(path);
    }
}

internal sealed class InternalApiCertificateHandler : HttpClientHandler
{
    private readonly X509Certificate2 trustedRoot;

    internal InternalApiCertificateHandler(string trustedRootPath)
    {
        trustedRoot = X509CertificateLoader.LoadCertificateFromFile(trustedRootPath);
        ServerCertificateCustomValidationCallback = ValidateServerCertificate;
    }

    private bool ValidateServerCertificate(HttpRequestMessage _, X509Certificate2? certificate, X509Chain? __, SslPolicyErrors errors)
    {
        if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain.Build(certificate);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) trustedRoot.Dispose();
        base.Dispose(disposing);
    }
}
