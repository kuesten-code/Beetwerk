using Kuestencode.Beetwerk.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Kuestencode.Beetwerk.Api.Auth;

public static class LoginEndpoints
{
    public const string RateLimitPolicy = "login";

    public static void MapLoginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (string? returnUrl, string? error, BeetwerkOptions options) =>
                options.AuthMode == AuthMode.None
                    ? Results.Redirect("/")
                    : Results.Content(LoginPage.Render(SafeReturnUrl(returnUrl), error), "text/html; charset=utf-8"))
            .AllowAnonymous();

        app.MapPost("/login", async (HttpContext context, UserService users, LoginThrottle throttle, BeetwerkOptions options) =>
            {
                if (options.AuthMode == AuthMode.None)
                    return Results.Redirect("/");

                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var username = form["username"].ToString().Trim();
                var password = form["password"].ToString();
                var returnUrl = SafeReturnUrl(form["returnUrl"]);

                if (throttle.IsLocked(username))
                    return Results.Redirect(LoginUrl(returnUrl, "locked"));

                var user = await users.VerifyAsync(username, password, context.RequestAborted);
                if (user is null)
                {
                    throttle.RecordFailure(username);
                    return Results.Redirect(LoginUrl(returnUrl, "invalid"));
                }

                throttle.Reset(username);
                await UserPrincipal.SignInAsync(context, user);
                return Results.Redirect(returnUrl);
            })
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicy);

        app.MapPost("/logout", async (HttpContext context, BeetwerkOptions options) =>
        {
            if (options.AuthMode == AuthMode.Login)
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//")
        && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";

    private static string LoginUrl(string returnUrl, string error) =>
        $"/login?error={error}&returnUrl={Uri.EscapeDataString(returnUrl)}";
}
