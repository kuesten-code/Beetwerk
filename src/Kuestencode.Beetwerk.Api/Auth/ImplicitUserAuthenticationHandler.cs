using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Kuestencode.Beetwerk.Api.Auth;

/// <summary>AUTH_MODE=none: jede Anfrage gilt als der implizite Standardnutzer. Die Autorisierung bleibt trotzdem
/// zentral aktiv, damit beim Umschalten auf login keine Route vergessen werden kann.</summary>
public class ImplicitUserAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ImplicitUser implicitUser)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Implicit";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, implicitUser.Id.ToString()),
                new Claim(ClaimTypes.Name, UserService.ImplicitUsername)
            ],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
