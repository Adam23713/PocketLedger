using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public interface ICurrentUser
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : throw new InvalidOperationException("An authenticated user is required.");
        }
    }
}
