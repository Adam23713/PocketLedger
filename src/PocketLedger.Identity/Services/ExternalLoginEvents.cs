using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

namespace PocketLedger.Services;

public static class ExternalLoginEvents
{
    // Sends a cancelled or failed provider flow back to the account controller instead of an error page.
    public static Task RemoteFailureAsync(RemoteFailureContext context)
    {
        // The handler replaces provider errors such as access_denied with a generic message, so the provider's own reason is logged here.
        var query = context.Request.Query;
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ExternalLoginEvents)).LogWarning(context.Failure,
            "External login with {Scheme} failed. Provider error: {ProviderError}, description: {ProviderErrorDescription}.", context.Scheme.Name, query["error"].ToString(), query["error_description"].ToString());
        var callback = context.Properties?.RedirectUri ?? "/Account/ExternalLoginCallback";
        context.Response.Redirect(QueryHelpers.AddQueryString(callback, "remoteError", "ProviderError"));
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
