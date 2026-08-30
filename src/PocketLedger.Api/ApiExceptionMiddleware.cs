using PocketLedger.Contracts;
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
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API request failure.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiError("server_error", "An unexpected server error occurred."));
        }
    }
}
