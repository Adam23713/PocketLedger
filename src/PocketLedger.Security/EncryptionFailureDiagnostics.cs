using System.Net.Http;

namespace PocketLedger.Security;

internal static class EncryptionFailureDiagnostics
{
    private const int MaximumDetailLength = 1024;
    private const int MaximumExceptionDepth = 5;

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var details = new List<string>();
        for (var current = exception; current is not null && details.Count < MaximumExceptionDepth; current = current.InnerException)
        {
            var category = current switch
            {
                OciCredentialException => "credential-decryption",
                HttpRequestException => "oci-network",
                TaskCanceledException => "oci-timeout",
                _ when current.GetType().FullName == "Oci.Common.Model.OciException" => "oci-service",
                _ => current.GetType().Name
            };
            var message = Normalize(current.Message);
            var detail = string.IsNullOrEmpty(message) ? category : $"{category}: {message}";
            if (!details.Contains(detail, StringComparer.Ordinal)) details.Add(detail);
        }
        return string.Join(" -> ", details);
    }

    private static string Normalize(string message)
    {
        var normalized = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaximumDetailLength ? normalized : normalized[..MaximumDetailLength] + "...";
    }
}
