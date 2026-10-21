using PocketLedger.Contracts;
using PocketLedger.Security;
using PocketLedger.Services;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (EntityNotFoundException exception)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new ApiError("not_found", exception.Message));
        }
        catch (BusinessRuleException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ApiError("business_rule", exception.Message));
        }
        // EF wraps converter failures, so the whole chain is checked: user data was touched without the session's data key.
        catch (Exception exception) when (HasInner<UserKeyUnavailableException>(exception) && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("data_key_required", "This request needs the session's data key. Sign in again."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API request failure.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiError("server_error", "An unexpected server error occurred."));
        }
    }

    private static bool HasInner<T>(Exception? exception) where T : Exception
    {
        for (; exception is not null; exception = exception.InnerException)
            if (exception is T) return true;
        return false;
    }
}
