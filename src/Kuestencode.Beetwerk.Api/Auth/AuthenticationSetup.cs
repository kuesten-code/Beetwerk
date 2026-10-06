using System.Threading.RateLimiting;
using Kuestencode.Beetwerk.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Kuestencode.Beetwerk.Api.Auth;

public static class AuthenticationSetup
{
    public static IServiceCollection AddBeetwerkAuthentication(this IServiceCollection services, BeetwerkOptions options)
    {
        services.AddDataProtection()
            .SetApplicationName("Beetwerk")
            .PersistKeysToFileSystem(new DirectoryInfo(options.KeyDirectory));

        services.AddScoped<UserService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<ImplicitUser>();

        var authentication = options.AuthMode == AuthMode.None
            ? services.AddAuthentication(ImplicitUserAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, ImplicitUserAuthenticationHandler>(ImplicitUserAuthenticationHandler.SchemeName, null)
            : services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);

        authentication.AddCookie(cookie =>
        {
            cookie.Cookie.Name = "beetwerk.auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
            cookie.SlidingExpiration = true;
            cookie.LoginPath = "/login";
            cookie.Events.OnValidatePrincipal = UserPrincipal.ValidateAsync;
            cookie.Events.OnRedirectToLogin = context =>
            {
                if (IsApiRequest(context.Request))
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    context.Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(context.Request.PathBase + context.Request.Path + context.Request.QueryString));
                return Task.CompletedTask;
            };
            cookie.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Zentral statt pro Endpunkt: Alles, was nicht ausdrücklich AllowAnonymous ist, verlangt eine Anmeldung –
        // auch statische Dateien, weil UseStaticFiles hinter UseAuthorization liegt.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(LoginEndpoints.RateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
            limiter.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Redirect("/login?error=locked");
                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>Bricht den Start ab, wenn eine öffentlich erreichbare Installation ohne Anmeldung laufen würde.</summary>
    public static void ValidateAuthConfiguration(BeetwerkOptions options, ILogger logger)
    {
        if (options.AuthMode != AuthMode.None)
            return;

        if (options.PublicUrl is not null && !options.AllowNoneAuthOnPublicUrl)
            throw new InvalidOperationException(
                $"AUTH_MODE=none ist zusammen mit PUBLIC_URL ({options.PublicUrl}) nicht erlaubt. " +
                "Entweder AUTH_MODE=login verwenden oder ausdrücklich AUTH_ALLOW_NONE_PUBLIC=true setzen.");

        logger.LogWarning(
            "ACHTUNG: AUTH_MODE=none – Beetwerk ist OHNE Anmeldung erreichbar. Nur in vertrauenswürdigen Netzen " +
            "(Heimnetz, VPN) oder hinter einem absichernden Reverse Proxy betreiben.");
    }

    public static bool IsApiRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        || request.Headers.Accept.Any(a => a?.Contains("application/json") == true);
}
