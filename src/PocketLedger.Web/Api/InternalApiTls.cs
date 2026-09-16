using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PocketLedger.Web.Api;

internal static class InternalApiTls
{
    private const string TrustedRootPathKey = "Api:TlsTrustedRootCertificatePath";

    internal static void ValidateConfiguration(Uri apiBaseUrl, IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return;
        if (apiBaseUrl.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Api:BaseUrl must use HTTPS outside Development.");
        _ = GetTrustedRootPath(configuration);
    }

    internal static HttpMessageHandler CreateHandler(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return new HttpClientHandler();
        return new InternalApiCertificateHandler(GetTrustedRootPath(configuration));
    }

    private static string GetTrustedRootPath(IConfiguration configuration)
    {
        var path = configuration[TrustedRootPathKey];
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException($"{TrustedRootPathKey} is required outside Development.");
        if (!File.Exists(path)) throw new InvalidOperationException($"The internal API TLS root certificate does not exist at '{path}'.");
        return path;
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

    internal bool ValidateServerCertificate(HttpRequestMessage _, X509Certificate2? certificate, X509Chain? __, SslPolicyErrors errors)
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
