using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PocketLedger.Controllers;

namespace PocketLedger.Tests;

public sealed class SessionControllerTests
{
    [Theory]
    [InlineData("/Statistics", "/Statistics")]
    [InlineData("https://attacker.example/redirect", "/")]
    public async Task CompleteSignIn_DiscardsExistingWebUserAndStartsOidcForTheIdentityUser(string returnUrl, string expectedDestination)
    {
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(authentication);

        var result = Assert.IsType<ChallengeResult>(await controller.CompleteSignIn(returnUrl));

        Assert.Equal("BffCookie", authentication.SignedOutScheme);
        Assert.Equal(["oidc"], result.AuthenticationSchemes);
        Assert.Equal(expectedDestination, result.Properties?.RedirectUri);
    }

    [Fact]
    public async Task Register_DiscardsExistingWebUserBeforeOpeningIdentityRegistration()
    {
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(authentication);

        var result = Assert.IsType<RedirectResult>(await controller.Register());

        Assert.Equal("BffCookie", authentication.SignedOutScheme);
        Assert.Equal("https://identity.example.test/Account/Register", result.Url);
    }

    [Fact]
    public async Task CompleteLogout_DiscardsTheWebUserBeforeReturningToTheLandingPage()
    {
        var authentication = new RecordingAuthenticationService();
        var controller = CreateController(authentication);

        var result = Assert.IsType<RedirectResult>(await controller.CompleteLogout());

        Assert.Equal("BffCookie", authentication.SignedOutScheme);
        Assert.Equal("https://landing.example.test", result.Url);
    }

    private static SessionController CreateController(IAuthenticationService authentication)
    {
        var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "adam")], "BffCookie"))
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
        return new SessionController(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Authority"] = "https://identity.example.test/",
            ["Landing:BaseUrl"] = "https://landing.example.test"
        }).Build())
        {
            ControllerContext = new ControllerContext(actionContext),
            Url = new UrlHelper(actionContext)
        };
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }
}
