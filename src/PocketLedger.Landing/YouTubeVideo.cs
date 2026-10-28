using System.Text.RegularExpressions;

namespace PocketLedger.Landing;

// Turns a configured YouTube link (watch, youtu.be, embed or shorts) into a privacy-enhanced embed URL.
public static partial class YouTubeVideo
{
    public const string EmbedOrigin = "https://www.youtube-nocookie.com";

    public static string? GetEmbedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = host.ToLowerInvariant() switch
        {
            "youtu.be" => segments.FirstOrDefault(),
            "youtube.com" or "m.youtube.com" or "youtube-nocookie.com" => segments switch
            {
                ["watch"] => System.Web.HttpUtility.ParseQueryString(uri.Query)["v"],
                ["embed" or "shorts" or "live", var value, ..] => value,
                _ => null
            },
            _ => null
        };
        return id is not null && VideoId().IsMatch(id) ? $"{EmbedOrigin}/embed/{id}" : null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoId();
}
