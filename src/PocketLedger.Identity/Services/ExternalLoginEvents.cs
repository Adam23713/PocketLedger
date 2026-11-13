using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

namespace PocketLedger.Services;

public static class ExternalLoginEvents
{
    // Sends a cancelled or failed provider flow back to the account controller instead of an error page.
    public static Task RemoteFailureAsync(RemoteFailureContext context)
    {
        // Callback parameters and provider exceptions can contain untrusted text.
        var failureCategory = context.Request.Query["error"].ToString() switch
        {
            "access_denied" => "AccessDenied",
            "server_error" => "ProviderServerError",
            "temporarily_unavailable" => "ProviderUnavailable",
            _ => "Other"
        };
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ExternalLoginEvents)).LogWarning(
            "External login with {Scheme} failed. Failure category: {FailureCategory}.", context.Scheme.Name, failureCategory);
        var callback = context.Properties?.RedirectUri ?? "/Account/ExternalLoginCallback";
        context.Response.Redirect(QueryHelpers.AddQueryString(callback, "remoteError", "ProviderError"));
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
