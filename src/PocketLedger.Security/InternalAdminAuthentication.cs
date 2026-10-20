using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace PocketLedger.Security;

public static class InternalAdminAuthentication
{
    public const string HeaderName = "X-PocketLedger-Admin-Key";

    public static void AddCredential(HttpRequestMessage request, IConfiguration configuration)
        => request.Headers.Add(HeaderName, GetKey(configuration));

    public static bool IsAuthorized(HttpRequest request, IConfiguration configuration)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var suppliedValues)) return false;
        var supplied = suppliedValues.SingleOrDefault();
        if (string.IsNullOrEmpty(supplied)) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(GetKey(configuration));
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static string GetKey(IConfiguration configuration)
        => configuration["AdminOperations:Key"] ?? throw new InvalidOperationException("AdminOperations:Key is required.");
}
