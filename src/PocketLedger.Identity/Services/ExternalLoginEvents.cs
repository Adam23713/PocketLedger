using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

namespace PocketLedger.Services;

public static class ExternalLoginEvents
{
    // Sends a cancelled or failed provider flow back to the account controller instead of an error page.
    public static Task RemoteFailureAsync(RemoteFailureContext context)
    {
        var callback = context.Properties?.RedirectUri ?? "/Account/ExternalLoginCallback";
        context.Response.Redirect(QueryHelpers.AddQueryString(callback, "remoteError", "ProviderError"));
        context.HandleResponse();
        return Task.CompletedTask;
    }
}
